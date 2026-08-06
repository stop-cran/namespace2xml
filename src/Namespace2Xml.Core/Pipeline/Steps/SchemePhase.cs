using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
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

        var loaded = ImmutableArray.CreateBuilder<LoadedSource>();

        for (var i = 0; i < command.Schemes.Length; i++)
        {
            var source = loader.LoadFile(
                command.Schemes[i], i, DiagnosticPhase.Scheme, diagnostics);

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
                SchemeReader.Read(
                    source.Records,
                    source.Ordinal,
                    source.Origin.Identity,
                    diagnostics).Entries);
        }

        var parsed = entries.ToImmutable();

        // Section 15.1 step 1 also resolves references among scheme entries. A value that contains
        // one is therefore not a value this step may hand on unresolved: doing so would make
        // 'filename=${a}' name a file literally called '${a}'.
        foreach (var entry in parsed)
        {
            if (entry.Value.ContainsReference)
            {
                return StepOutcome.Unsupported<ImmutableArray<SchemeEntry>>(
                    new UnsupportedCapability(
                        "references in scheme values",
                        $"'{entry.Declaration}' in {entry.Source} contains a reference, and step 1 "
                        + "resolves references among scheme entries.",
                        "\u00A715.1"));
            }
        }

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<ImmutableArray<SchemeEntry>>()
            : StepOutcome.Produced(parsed);
    }

    /// <summary>Section 15.1 step 2: compile root-level input options.</summary>
    /// <param name="entries">Step 1's product.</param>
    /// <returns>The same entries, once every input-options directive is known to be absent.</returns>
    public static StepOutcome<ImmutableArray<SchemeEntry>> CompileInputOptions(
        ImmutableArray<SchemeEntry> entries) =>
        Decline(
            entries,
            directive => directive
                is SchemeDirective.XmlInputOptions
                or SchemeDirective.JsonInputOptions
                or SchemeDirective.YamlInputOptions,
            "structured input options",
            "\u00A715.1");

    /// <summary>Section 15.1 step 3: compile <c>substitute</c> path patterns.</summary>
    /// <param name="entries">Step 2's product.</param>
    /// <returns>The same entries, once every <c>substitute</c> directive is known to be absent.</returns>
    public static StepOutcome<ImmutableArray<SchemeEntry>> CompileSubstitutePatterns(
        ImmutableArray<SchemeEntry> entries) =>
        Decline(
            entries,
            directive => directive is SchemeDirective.Substitute,
            "the substitute directive",
            "\u00A715.1");

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

    private static StepOutcome<ImmutableArray<SchemeEntry>> Decline(
        ImmutableArray<SchemeEntry> entries,
        Func<SchemeDirective, bool> unsupported,
        string capability,
        string spec)
    {
        foreach (var entry in entries)
        {
            if (unsupported(entry.Directive))
            {
                return StepOutcome.Unsupported<ImmutableArray<SchemeEntry>>(
                    new UnsupportedCapability(
                        capability,
                        $"'{entry.Declaration}' in {entry.Source} needs it.",
                        spec));
            }
        }

        return StepOutcome.Produced(entries);
    }
}
