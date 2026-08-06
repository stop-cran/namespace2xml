using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scalars;
using Namespace2Xml.Scheme;

namespace Namespace2Xml.Pipeline.Steps;

/// <summary>One input source, read into the parts Section 15.1 steps 5 and 7 separate.</summary>
/// <param name="Origin">How diagnostics name it.</param>
/// <param name="Contribution">Its parsed contribution.</param>
public sealed record InputContribution(ProfileSource Origin, ProfileContribution Contribution);

/// <summary>
/// Specification Section 15.1 steps 5 to 12: the input phase.
/// </summary>
/// <remarks>
/// Steps 6 and 7 do not appear as separate methods because
/// <see cref="NamespaceProfileReader"/> performs them as it reads: it lexes every value, which is
/// step 6, and returns templates and masks separately from concrete contributions, which is step 7.
/// Splitting them out would mean re-walking the records to assert work already done. The driver
/// still runs them as steps so that the order remains checkable, and their bodies are the identity.
/// </remarks>
public static class InputPhase
{
    /// <summary>Section 15.1 step 5: parse input documents into typed overlays.</summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="loader">The shared source loader.</param>
    /// <param name="budget">The invocation's global budget, already charged for scheme sources.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>Every admitted input contribution, in Section 7.3 stream order.</returns>
    public static StepOutcome<ImmutableArray<InputContribution>> ParseInputs(
        CommandLine command,
        SourceLoader loader,
        GlobalBudget budget,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(diagnostics);

        foreach (var input in command.Inputs)
        {
            if (SourceLoader.StructuredFormat(input) is { } format)
            {
                return StepOutcome.Unsupported<ImmutableArray<InputContribution>>(
                    new UnsupportedCapability(
                        $"{format} input",
                        $"'{input}' has a {format} extension, which Section 7.1 parses as {format} "
                        + "rather than as a namespace profile.",
                        "\u00A77.1"));
            }
        }

        // Section 7.3 fixes one ordered stream for the whole invocation: scheme files, then input
        // files, then variables. Scheme ordinals were 0 through Schemes.Length - 1, so inputs
        // continue from there and the global budget sees the stream in the order it names.
        var next = (long)command.Schemes.Length;
        var loaded = ImmutableArray.CreateBuilder<LoadedSource>();

        foreach (var input in command.Inputs)
        {
            var source = loader.LoadFile(input, next++, DiagnosticPhase.Input, diagnostics);

            if (source is not null)
            {
                loaded.Add(source);
            }
        }

        for (var i = 0; i < command.Variables.Length; i++)
        {
            var source = loader.LoadVariable(
                command.Variables[i], i + 1, next++, diagnostics);

            if (source is not null)
            {
                loaded.Add(source);
            }
        }

        var admitted = SourceLoader.Admit(loaded, budget, _ => DiagnosticPhase.Input, diagnostics);
        var contributions = ImmutableArray.CreateBuilder<InputContribution>();

        foreach (var source in admitted)
        {
            if (!source.Admitted)
            {
                continue;
            }

            contributions.Add(
                new InputContribution(
                    source.Origin,
                    NamespaceProfileReader.Read(
                        source.Records, source.Ordinal, source.Origin, diagnostics)));
        }

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<ImmutableArray<InputContribution>>()
            : StepOutcome.Produced(contributions.ToImmutable());
    }

    /// <summary>Section 15.1 step 8: fold within each contribution, then merge in source order.</summary>
    /// <param name="contributions">Step 5's product.</param>
    /// <param name="configuration">Step 4's product, whose merge directives govern the fold.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The merged overlay.</returns>
    public static StepOutcome<OverlayNode> MergeContributions(
        ImmutableArray<InputContribution> contributions,
        SchemeConfiguration configuration,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var paths = configuration.InputMerges
            .Where(merge => merge.Path is not null)
            .Select(merge => KeyValuePair.Create(merge.Path!, merge.Strategy));

        var root = configuration.InputMerges
            .Where(merge => merge.Path is null)
            .Select(merge => merge.Strategy)
            .DefaultIfEmpty(MergeStrategy.Deep)
            .Last();

        var merger = new OverlayMerger(MergeStrategyMap.Create(paths, root), diagnostics);
        var merged = merger.MergeAll(contributions.Select(c => c.Contribution.Overlay));

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<OverlayNode>()
            : StepOutcome.Produced(merged);
    }

    /// <summary>Section 15.1 step 9: expose ordering values and numeric mapping keys as path parts.</summary>
    /// <param name="merged">Step 8's product.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The overlay with ordering values exposed.</returns>
    public static StepOutcome<OverlayNode> ExposeOrderingValues(
        OverlayNode merged,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(merged);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var exposer = new OrderingValueExposer(
            new OverlayMerger(MergeStrategyMap.Create([]), diagnostics));

        var exposed = exposer.Expose(merged);

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<OverlayNode>()
            : StepOutcome.Produced(exposed);
    }

    /// <summary>Section 15.1 step 10: evaluate templates under the permanent exclusion masks.</summary>
    /// <param name="exposed">Step 9's product.</param>
    /// <param name="contributions">Step 5's product, which carries the templates and masks.</param>
    /// <returns>The overlay, once every template and mask is known to be absent.</returns>
    public static StepOutcome<OverlayNode> EvaluateTemplates(
        OverlayNode exposed,
        ImmutableArray<InputContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(exposed);

        foreach (var contribution in contributions)
        {
            if (!contribution.Contribution.Templates.IsEmpty)
            {
                return StepOutcome.Unsupported<OverlayNode>(
                    new UnsupportedCapability(
                        "wildcard templates",
                        $"{contribution.Origin.Identity} declares a wildcard entry on line "
                        + $"{contribution.Contribution.Templates[0].Line}.",
                        "\u00A712"));
            }

            if (!contribution.Contribution.Masks.IsEmpty)
            {
                return StepOutcome.Unsupported<OverlayNode>(
                    new UnsupportedCapability(
                        "permanent exclusion masks",
                        $"{contribution.Origin.Identity} declares a '!' mask on line "
                        + $"{contribution.Contribution.Masks[0].Line}.",
                        "\u00A78.6"));
            }
        }

        return StepOutcome.Produced(exposed);
    }

    /// <summary>Section 15.1 step 11: project inferable mappings as explicit indexed sequences.</summary>
    /// <param name="evaluated">Step 10's product.</param>
    /// <returns>The overlay, once no mapping is sequence-inferable.</returns>
    /// <remarks>
    /// The check is a whole-tree walk rather than a per-source one, because Section 8.7 infers over
    /// the merged model: <c>a.0=x</c> in one file and <c>a.1=y</c> in another form one inferable
    /// mapping that neither source can see alone.
    /// </remarks>
    public static StepOutcome<OverlayNode> InferSequences(OverlayNode evaluated)
    {
        ArgumentNullException.ThrowIfNull(evaluated);

        return FindInferable(evaluated, []) is { } path
            ? StepOutcome.Unsupported<OverlayNode>(
                new UnsupportedCapability(
                    "sequence inference",
                    $"the mapping at '{path}' has only canonical non-negative decimal keys, which "
                    + "Section 8.7 projects as an indexed sequence rather than as a mapping.",
                    "\u00A78.7"))
            : StepOutcome.Produced(evaluated);
    }

    /// <summary>Section 15.1 step 12: infer scalar kinds for remaining untyped payloads.</summary>
    /// <param name="projected">Step 11's product.</param>
    /// <returns>The overlay with every untyped payload settled.</returns>
    public static StepOutcome<OverlayNode> InferScalarKinds(OverlayNode projected)
    {
        ArgumentNullException.ThrowIfNull(projected);

        return StepOutcome.Produced(Settle(projected));
    }

    private static OverlayNode Settle(OverlayNode node)
    {
        var settled = node;

        if (node.Payload is { IsUntyped: true } payload)
        {
            settled = settled.WithPayload(
                ScalarInference.Infer(payload), node.Marks.PayloadMark ?? node.Marks.Position);
        }

        foreach (var (name, child) in node.OrderedChildren)
        {
            var inferred = Settle(child);

            if (!ReferenceEquals(inferred, child))
            {
                settled = settled.WithChild(name, inferred);
            }
        }

        foreach (var (ordering, item) in node.OrderedSequence)
        {
            var inferred = Settle(item.Node);

            if (!ReferenceEquals(inferred, item.Node))
            {
                settled = settled.WithSequenceItem(ordering, item with { Node = inferred });
            }
        }

        return settled;
    }

    /// <summary>
    /// The path of the first mapping Section 8.7 would project as a sequence, in the order Section
    /// 5.2 fixes, or <c>null</c> when there is none.
    /// </summary>
    private static string? FindInferable(OverlayNode node, ImmutableArray<string> prefix)
    {
        if (IsInferable(node))
        {
            return prefix.IsEmpty ? "(root)" : string.Join('.', prefix);
        }

        foreach (var (name, child) in node.OrderedChildren)
        {
            if (FindInferable(child, prefix.Add(Spell(name))) is { } found)
            {
                return found;
            }
        }

        foreach (var (ordering, item) in node.OrderedSequence)
        {
            if (FindInferable(
                item.Node,
                prefix.Add(ordering.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static bool IsInferable(OverlayNode node) =>
        !node.Children.IsEmpty && node.Children.Keys.All(IsCanonicalIndex);

    /// <summary>
    /// Section 8.7's canonical nonnegative decimal ordering value: <c>0</c> or a nonzero digit
    /// followed by decimal digits, with a value of at most 9,223,372,036,854,775,807.
    /// </summary>
    /// <remarks>
    /// Both exclusions are stated as preventing sequence interpretation, so returning
    /// <see langword="false"/> for a leading-zero or out-of-range spelling makes the whole mapping
    /// an ordinary one, which is what Section 8.7 asks for.
    /// </remarks>
    private static bool IsCanonicalIndex(NamePart part) =>
        part is OrdinaryPart { LiteralText: { } text }
        && text.Length > 0
        && (text.Length == 1 || text[0] != '0')
        && text.All(c => c is >= '0' and <= '9')
        && long.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out _);

    private static string Spell(NamePart part) =>
        part is OrdinaryPart { LiteralText: { } text } ? text : part.ToString();
}
