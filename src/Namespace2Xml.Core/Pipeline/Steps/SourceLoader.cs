using System.Collections.Immutable;
using System.Text;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Profiles;
using Namespace2Xml.Text;

namespace Namespace2Xml.Pipeline.Steps;

/// <summary>One source that was read, decoded, classified, and charged to the budgets.</summary>
/// <param name="Origin">How diagnostics name it.</param>
/// <param name="Ordinal">Its Section 4.7 CLI source ordinal.</param>
/// <param name="Records">Its Section 8.1 classified records, or empty when it contributed none.</param>
/// <param name="Tally">What it consumed, for the Section 7.3 global join.</param>
/// <param name="Admitted">
/// Whether the Section 7.3 global join let it contribute. A source at or after the one that crossed
/// a global bound "contributes no parsed model", so it is loaded and then dropped.
/// </param>
public sealed record LoadedSource(
    ProfileSource Origin,
    long Ordinal,
    ImmutableArray<NamespaceRecord> Records,
    SourceTally Tally,
    bool Admitted)
{
    /// <summary>
    /// The parsed native document, when Section 7.1 selected a structured format for this source.
    /// </summary>
    /// <remarks>
    /// A structured source has no Section 8.1 records, so <see cref="Records"/> is empty for one and
    /// this carries its contribution instead. It is parsed before admission for the reason Section
    /// 7.3 gives: a source that crosses a bound "still had to be parsed for its contribution to be
    /// known".
    /// </remarks>
    public StructuredNode? Document { get; init; }

    /// <summary>
    /// The Section 7.1 structured format this source was parsed as, or <see langword="null"/> for a
    /// namespace profile.
    /// </summary>
    /// <remarks>
    /// Section 11.4's cross-contribution classification is a rule about XML contributions to one
    /// element, so <see cref="XmlClassification"/> has to know which documents are XML. The shapes
    /// alone cannot say: a JSON mapping and an XML element-only element are the same shape by
    /// construction, which is the point of <see cref="StructuredNode"/>.
    /// </remarks>
    public string? Format { get; init; }
}

/// <summary>
/// Reads, decodes, and classifies one source, charging Section 23 per-source budgets as it goes.
/// </summary>
/// <remarks>
/// <para>
/// Shared by step 1 and step 5 because Section 7 gives scheme files and input files one set of
/// rules: the same extension table, the same missing-file behaviour, the same encoding detection,
/// and the same per-source budgets. Two copies would drift, and the copy that drifted would be the
/// one with no fixture.
/// </para>
/// <para>
/// Section 7.3 forbids a parser from observing a global total: "it accumulates its own source's
/// contribution and reports it, and nothing more". This class therefore owns a
/// <see cref="SourceBudget"/> and never touches <see cref="GlobalBudget"/>; admission is a separate
/// pass in CLI order, which is what makes the outcome independent of read scheduling.
/// </para>
/// </remarks>
public sealed class SourceLoader
{
    private readonly ISourceReader reader;
    private readonly ResourceLimits limits;
    private readonly IOperationalLog log;

    /// <summary>Creates a loader.</summary>
    /// <param name="reader">Where bytes come from.</param>
    /// <param name="limits">The Section 23 bounds.</param>
    /// <param name="log">Where Section 6.2 per-file parsing messages go.</param>
    public SourceLoader(ISourceReader reader, ResourceLimits limits, IOperationalLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(limits);

        this.reader = reader;
        this.limits = limits;
        this.log = log ?? SilentOperationalLog.Instance;
    }

    /// <summary>The Section 7.1 extension of a native structured input format.</summary>
    /// <param name="path">The path as written on the command line.</param>
    /// <returns>The format name when it is one, otherwise <c>null</c>.</returns>
    /// <remarks>
    /// Section 7.1 lists exactly four extensions and sends "every other extension" to
    /// namespace-profile parsing, naming <c>.ini</c> and <c>.sh</c> as deliberate members of that
    /// remainder. The test is on the written path rather than on content, so it answers the same way
    /// whether or not the file exists.
    /// </remarks>
    public static string? StructuredFormat(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var mark = path.LastIndexOfAny(['.', '/', '\\']);

        if (mark < 0 || path[mark] != '.')
        {
            return null;
        }

        return path[mark..].ToUpperInvariant() switch
        {
            ".JSON" => "JSON",
            ".YAML" or ".YML" => "YAML",
            ".XML" => "XML",
            _ => null,
        };
    }

    /// <summary>Reads one file source.</summary>
    /// <param name="path">The path as written on the command line.</param>
    /// <param name="ordinal">The Section 4.7 CLI source ordinal.</param>
    /// <param name="phase">The phase its diagnostics report.</param>
    /// <param name="diagnostics">The buffer its diagnostics accumulate in.</param>
    /// <returns>The loaded source, or <c>null</c> when it contributed nothing at all.</returns>
    public LoadedSource? LoadFile(
        string path, long ordinal, DiagnosticPhase phase, DiagnosticBuffer diagnostics) =>
        LoadFile(path, ordinal, phase, diagnostics, InputOptions.Default, structured: false);

    /// <summary>Reads one file source.</summary>
    /// <param name="path">The path as written on the command line.</param>
    /// <param name="ordinal">The Section 4.7 CLI source ordinal.</param>
    /// <param name="phase">The phase its diagnostics report.</param>
    /// <param name="diagnostics">The buffer its diagnostics accumulate in.</param>
    /// <param name="structured">
    /// Whether Section 7.1's extension table applies. Section 15 lets a scheme file carry a
    /// structured extension too, and <c>SchemePhase</c> passes <see langword="true"/> for a scheme:
    /// a <c>.json</c> or <c>.yaml</c> scheme is parsed natively and projected by
    /// <c>StructuredSchemeReader</c>. An <c>.xml</c> scheme never reaches here, because Section 15
    /// states no projection for one.
    /// </param>
    /// <param name="options">The Section 16.8 input options the readers run under.</param>
    /// <returns>The loaded source, or <c>null</c> when it contributed nothing at all.</returns>
    public LoadedSource? LoadFile(
        string path,
        long ordinal,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        InputOptions options,
        bool structured)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var origin = ProfileSource.OfFile(Normalize(path), ordinal);
        var key = StableOrderingKey.FromSource(ordinal, 0);
        var read = reader.Read(path);

        switch (read.Status)
        {
            case SourceReadStatus.Missing:
                diagnostics.Add(new BufferedDiagnostic(
                    DiagnosticCodes.Warn001(
                        phase,
                        "\u00A77.2",
                        $"'{read.ResolvedPath}' does not exist, so it contributes no data. "
                        + "Section 7.2 does not make a missing file a failure by itself.",
                        cardinalityKey: origin.SourceKey,
                        source: origin.File),
                    key));
                return null;

            case SourceReadStatus.Failed:
                diagnostics.Add(new BufferedDiagnostic(
                    DiagnosticCodes.Parse001(
                        phase,
                        "\u00A77.2",
                        $"'{read.ResolvedPath}' exists but could not be read: {read.Reason} "
                        + "Section 7.2 makes that a blocking source error rather than the warning "
                        + "a missing file receives.",
                        cardinalityKey: origin.SourceKey,
                        source: origin.File),
                    key));
                return null;

            default:
                break;
        }

        var budget = new SourceBudget(limits, ordinal);

        if (!budget.TryAddInputBytes(read.Bytes!.Length))
        {
            EmitLimit(budget.Fault!.Value, origin, phase, diagnostics, key);
            return null;
        }

        var decoded = InputDecoder.Decode(read.Bytes, origin.File!, phase);

        if (!decoded.Succeeded)
        {
            diagnostics.Add(new BufferedDiagnostic(decoded.Diagnostic!, key));
            return Attempted(origin, ordinal, budget);
        }

        if (structured && StructuredFormat(path) is { } format)
        {
            var document = format switch
            {
                "JSON" => JsonInputReader.Read(
                    decoded.Text!, limits, budget, origin, phase, diagnostics, key),
                "YAML" => YamlInputReader.Read(
                    decoded.Text!, limits, budget, origin, phase, diagnostics, key),
                "XML" => XmlInputReader.Read(
                    decoded.Text!,
                    decoded.Encoding!.Value,
                    options.Xml,
                    budget,
                    origin,
                    phase,
                    diagnostics,
                    key),
                _ => throw new InvalidOperationException(
                    $"Section 7.1 names no structured format '{format}'."),
            };

            if (document is null)
            {
                if (budget.Fault is { } fault)
                {
                    EmitLimit(fault, origin, phase, diagnostics, key);
                }

                return Attempted(origin, ordinal, budget);
            }

            log.Write(
                OperationalLevel.Trace,
                $"parsed '{origin.File}' as {format}: {read.Bytes.Length} byte(s), "
                + $"{decoded.Encoding!.Value}.");

            return new LoadedSource(origin, ordinal, [], budget.Tally, Admitted: true)
            {
                Document = document,
                Format = format,
            };
        }
        var records = NamespaceRecordClassifier.Classify(PhysicalRecordReader.Read(decoded.Text!));

        // Section 6.2 level 1 shows "per-file parsing" detail. The counts are what a reader wants
        // when a file appears to contribute less than expected: an encoding that decoded to fewer
        // records than the file has lines is visible here and nowhere else.
        log.Write(
            OperationalLevel.Trace,
            $"parsed '{origin.File}' as a namespace profile: {records.Length} record(s), "
            + $"{read.Bytes.Length} byte(s), {decoded.Encoding!.Value}.");

        return Charge(records, origin, ordinal, budget, phase, diagnostics, key);
    }

    /// <summary>
    /// A source whose bytes were read and whose parse attempt produced no model, carrying only what
    /// it consumed.
    /// </summary>
    /// <param name="origin">How diagnostics name it.</param>
    /// <param name="ordinal">Its Section 4.7 CLI source ordinal.</param>
    /// <param name="budget">The per-source budget it was charged against.</param>
    /// <remarks>
    /// Section 7.3 evaluates the global budgets "after all independently readable sources finish
    /// their parse attempt", over a stream of "all scheme files in <c>-s</c> order, then all input
    /// files in <c>-i</c> order, then command-line variables". A file that was read and then failed
    /// to decode or to parse is an independently readable source that finished its parse attempt,
    /// so its bytes belong in that stream. Dropping it would not merely lose a <c>LIMIT001</c>: it
    /// would shorten the stream, so the bound would be judged to be crossed by a later file than
    /// the one that actually crossed it, and "the first source whose cumulative contribution would
    /// cross a global bound" would name the wrong source.
    /// </remarks>
    private static LoadedSource Attempted(
        ProfileSource origin, long ordinal, SourceBudget budget) =>
        new(origin, ordinal, [], budget.Tally, Admitted: true);

    /// <summary>Reads one <c>--variables</c> argument as a source.</summary>
    /// <param name="text">The argument, which Section 8.1 makes exactly one namespace record.</param>
    /// <param name="position">Its one-based position in <c>-v</c> token order.</param>
    /// <param name="ordinal">The Section 4.7 CLI source ordinal.</param>
    /// <param name="diagnostics">The buffer its diagnostics accumulate in.</param>
    /// <returns>The loaded source, or <c>null</c> when it contributed nothing at all.</returns>
    public LoadedSource? LoadVariable(
        string text, int position, long ordinal, DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var origin = ProfileSource.OfVariable(position, ordinal);
        var key = StableOrderingKey.FromSource(ordinal, 0);
        var budget = new SourceBudget(limits, ordinal);

        // Section 23 applies --max-input-bytes to a variable "after encoding a variable as UTF-8",
        // so the charge is the encoded length rather than the character count.
        if (!budget.TryAddInputBytes(Encoding.UTF8.GetByteCount(text)))
        {
            EmitLimit(budget.Fault!.Value, origin, DiagnosticPhase.Input, diagnostics, key);
            return null;
        }

        var record = NamespaceRecordClassifier.Classify(text, 1);

        if (record.Kind is NamespaceRecordKind.Comment or NamespaceRecordKind.Ignored)
        {
            diagnostics.Add(new BufferedDiagnostic(
                DiagnosticCodes.Parse001(
                    DiagnosticPhase.Input,
                    "\u00A78.1",
                    origin.Say(
                        "this is a comment record, and Section 8.1 accepts ordinary entries "
                        + "and permanent '!' masks as variables but not comment records."),
                    cardinalityKey: origin.Key("kind")),
                key));
            return null;
        }

        return Charge([record], origin, ordinal, budget, DiagnosticPhase.Input, diagnostics, key);
    }

    /// <summary>
    /// Applies the Section 7.3 global join to sources already loaded, in the one ordered stream
    /// Section 23 fixes.
    /// </summary>
    /// <param name="sources">The loaded sources, in that stream's order.</param>
    /// <param name="budget">The invocation's global budget.</param>
    /// <param name="phaseOf">The phase each source's diagnostics report.</param>
    /// <param name="diagnostics">The buffer the <c>LIMIT001</c> accumulates in.</param>
    /// <returns>The same sources, with those the join excluded marked unadmitted.</returns>
    /// <remarks>
    /// Runs over already-parsed sources rather than during parsing, because Section 7.3 evaluates
    /// global budgets "after all independently readable sources finish their parse attempt". A
    /// source that crosses a bound still had to be parsed for its contribution to be known.
    /// </remarks>
    public static ImmutableArray<LoadedSource> Admit(
        IEnumerable<LoadedSource> sources,
        GlobalBudget budget,
        Func<LoadedSource, DiagnosticPhase> phaseOf,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(phaseOf);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var admitted = ImmutableArray.CreateBuilder<LoadedSource>();

        foreach (var source in sources)
        {
            if (budget.TryAdmit(source.Tally, source.Ordinal, out var fault))
            {
                admitted.Add(source);
                continue;
            }

            if (fault is { } crossing)
            {
                EmitLimit(
                    crossing,
                    source.Origin,
                    phaseOf(source),
                    diagnostics,
                    StableOrderingKey.FromSource(source.Ordinal, 0));
            }

            admitted.Add(source with { Admitted = false });
        }

        return admitted.ToImmutable();
    }

    /// <summary>The Section 6.4.3 spelling of a path: relative, with <c>/</c> separators.</summary>
    internal static string Normalize(string path) => path.Replace('\\', '/');

    private static LoadedSource? Charge(
        ImmutableArray<NamespaceRecord> records,
        ProfileSource origin,
        long ordinal,
        SourceBudget budget,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key)
    {
        foreach (var record in records)
        {
            switch (record.Kind)
            {
                case NamespaceRecordKind.Comment:
                    budget.AddComments(1, Encoding.UTF8.GetByteCount(record.Comment!));
                    break;

                case NamespaceRecordKind.Entry:
                    // One node per name part is what this source alone can know: whether two
                    // sources share an ancestor is a global fact, and Section 7.3 forbids a parser
                    // from observing one.
                    var depth = Depth(record.Name!);
                    budget.AddNodes(depth);

                    if (!budget.TryEnterDepth(depth, record.Line))
                    {
                        EmitLimit(budget.Fault!.Value, origin, phase, diagnostics, key);
                        return null;
                    }

                    break;

                default:
                    break;
            }
        }

        return new LoadedSource(origin, ordinal, records, budget.Tally, Admitted: true);
    }

    /// <summary>
    /// Counts a written name's parts without lexing it, so a name too deep to accept is refused
    /// before it is built.
    /// </summary>
    /// <remarks>
    /// Section 8.2 separates parts at an unescaped <c>.</c>, so a preceding backslash suppresses the
    /// separator. Counting on raw text can only agree with, or overcount, the lexed name, and it
    /// never has to hold the name that the depth bound exists to refuse.
    /// </remarks>
    private static int Depth(string name)
    {
        var parts = 1;

        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '\\')
            {
                i++;
            }
            else if (name[i] == '.')
            {
                parts++;
            }
        }

        return parts;
    }

    private static void EmitLimit(
        BudgetFault fault,
        ProfileSource origin,
        DiagnosticPhase phase,
        DiagnosticBuffer diagnostics,
        StableOrderingKey key) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Limit001(
                phase,
                "\u00A723",
                $"'{origin.Identity}' crosses {fault.Spelling}, whose limit is {fault.Limit}.",
                source: origin.File),
            key));
}
