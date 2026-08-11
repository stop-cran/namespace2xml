using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Scheme;

namespace Namespace2Xml.Pipeline.Steps;

/// <summary>
/// Specification Section 15.1 steps 1 to 4: the scheme phase.
/// </summary>
/// <remarks>
/// <para>
/// The four steps are separate because Section 15.1 separates them, and because each one is a
/// distinct opportunity for this build to discover that it cannot do the job. A step that cannot
/// perform its own work declines with an <see cref="UnsupportedCapability"/> instead of passing its
/// input through; see <see cref="UnsupportedCapability"/> for why silence is the worse failure.
/// </para>
/// <para>
/// Scheme sources are read here rather than before step 1 so that their diagnostics carry the
/// scheme phase, and they are admitted to the global budget here so that the Section 7.3 ordered
/// stream — schemes, then inputs, then variables — is charged in that order across the two phases
/// that read it.
/// </para>
/// </remarks>
public static class SchemePhase
{
    /// <summary>Section 15.1 step 1: parse scheme syntax and resolve scheme-internal references.</summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="loader">The shared source loader.</param>
    /// <param name="budget">The invocation's global budget.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>Every scheme entry, in Section 4.7 order across sources.</returns>
    public static StepOutcome<ImmutableArray<SchemeEntry>> ParseSchemes(
        CommandLine command,
        SourceLoader loader,
        GlobalBudget budget,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(diagnostics);

        // Section 15 gives scheme files the case-insensitive '.json', '.yaml' and '.yml'
        // extensions and routes every other extension, including none at all, to namespace-profile
        // parsing. Sections 9.1 and 10.4 say how a JSON or YAML key projects to a name part.
        // '.xml' is excluded by name: Section 15 defines no projection from an XML document to a
        // qualified directive path, and says so rather than leaving the question open -- see issue
        // #72. Reported before anything is read, so the namespace parser never sees the file and
        // never reports Section 8.1 against a document written to no such contract.
        var rejected = false;

        foreach (var path in command.Schemes)
        {
            if (SourceLoader.StructuredFormat(path) != "XML")
            {
                continue;
            }

            rejected = true;

            diagnostics.Add(new BufferedDiagnostic(DiagnosticCodes.Parse001(
                DiagnosticPhase.Scheme,
                "\u00A715",
                $"'{path}' names the XML format, which Section 15 excludes from the scheme file "
                + "extensions because it defines no projection from an XML document to a "
                + "qualified directive path. Scheme files are namespace profiles, JSON, or YAML, "
                + "and Section 15 calls the namespace-profile form canonical.",
                cardinalityKey: path,
                source: path)));
        }

        if (rejected)
        {
            return StepOutcome.Failed<ImmutableArray<SchemeEntry>>();
        }

        var loaded = ImmutableArray.CreateBuilder<LoadedSource>();

        for (var i = 0; i < command.Schemes.Length; i++)
        {
            var source = loader.LoadFile(
                command.Schemes[i],
                i,
                DiagnosticPhase.Scheme,
                diagnostics,
                InputOptions.Default,
                structured: true);

            if (source is not null)
            {
                loaded.Add(source);
            }
        }

        var admitted = SourceLoader.Admit(
            loaded, budget, _ => DiagnosticPhase.Scheme, diagnostics);

        var entries = ImmutableArray.CreateBuilder<SchemeEntry>();

        foreach (var source in admitted)
        {
            if (!source.Admitted)
            {
                continue;
            }

            entries.AddRange(
                source.Document is { } document
                    ? StructuredSchemeReader.Read(
                        document,
                        source.Ordinal,
                        source.Origin.Identity,
                        diagnostics).Entries
                    : SchemeReader.Read(
                        source.Records,
                        source.Ordinal,
                        source.Origin.Identity,
                        diagnostics).Entries);
        }

        var parsed = entries.ToImmutable();

        // Section 15.1 step 1 also resolves references among scheme entries, before step 2 compiles
        // a single directive value. Handing one on unresolved would make 'filename=${a}' name a
        // file literally called '${a}', which is a plausible file, a wrong file, and
        // indistinguishable from a correct one.
        var resolved = SchemeReferenceResolver.Resolve(parsed, budget, diagnostics);

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<ImmutableArray<SchemeEntry>>()
            : StepOutcome.Produced(resolved);
    }

    /// <summary>Section 15.1 step 2: compile root-level input options.</summary>
    /// <param name="entries">Step 1's product.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The same entries, paired with the options every input reader runs under.</returns>
    public static StepOutcome<(ImmutableArray<SchemeEntry> Entries, InputOptions Options)>
        CompileInputOptions(ImmutableArray<SchemeEntry> entries, DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var options = SchemeCompiler.CompileInputOptions(entries, diagnostics);

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<(ImmutableArray<SchemeEntry>, InputOptions)>()
            : StepOutcome.Produced((entries, options));
    }

    /// <summary>Section 15.1 step 3: compile <c>substitute</c> path patterns.</summary>
    /// <param name="entries">Step 2's product.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The same entries, paired with the Section 16.7 mode every input entry reads.</returns>
    /// <remarks>
    /// The patterns are compiled here, three steps before the inputs they govern are parsed,
    /// because step 6 lexes a value according to them: whether a <c>${</c> begins a reference at
    /// all is decided before the value is read, not corrected afterwards. Section 15.1 lists
    /// <c>substitute</c> and literal input <c>merge</c> as "the explicit earlier-phase exceptions"
    /// to scheme paths addressing step 11's stable paths for exactly this reason.
    /// </remarks>
    public static StepOutcome<(ImmutableArray<SchemeEntry> Entries, SubstituteModeMap Substitutes)>
        CompileSubstitutePatterns(
            ImmutableArray<SchemeEntry> entries,
            DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var substitutes = SchemeCompiler.CompileSubstitutes(entries, diagnostics);

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<(ImmutableArray<SchemeEntry>, SubstituteModeMap)>()
            : StepOutcome.Produced((entries, substitutes));
    }

    /// <summary>Section 15.1 step 4: compile literal-path input <c>merge</c> directives.</summary>
    /// <param name="entries">Step 3's product.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The compiled configuration.</returns>
    public static StepOutcome<SchemeConfiguration> CompileInputMerges(
        ImmutableArray<SchemeEntry> entries,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var configuration = SchemeCompiler.Compile(entries, diagnostics);

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<SchemeConfiguration>()
            : StepOutcome.Produced(configuration);
    }
}
