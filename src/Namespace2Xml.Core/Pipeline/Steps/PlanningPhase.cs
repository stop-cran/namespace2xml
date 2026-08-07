using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Overlay;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scheme;

namespace Namespace2Xml.Pipeline.Steps;

/// <summary>One concrete output instance's selected view in one format.</summary>
/// <param name="Instance">The output instance the view belongs to.</param>
/// <param name="Format">The format this view renders in.</param>
/// <param name="FormatOrdinal">Its position within one comma-separated <c>output</c> value.</param>
/// <param name="View">The Section 14.1 selected view, with the selector prefix already removed.</param>
/// <param name="Root">
/// The Section 16.3 root parts to prefix, empty when none apply. A bare scalar view under a
/// non-root selector carries the final selector part here, which is Section 19.1's "retains the
/// final concrete selector part as the emitted key".
/// </param>
public sealed record OutputView(
    OutputInstance Instance,
    OutputFormat Format,
    int FormatOrdinal,
    OverlayNode View,
    ImmutableArray<NamePart> Root);

/// <summary>One output view bound to the destination it will be written to.</summary>
/// <param name="View">The view.</param>
/// <param name="Path">Its Section 17.5 canonical destination path.</param>
/// <param name="Key">Its Section 17.5 fold key.</param>
public sealed record DestinationContribution(OutputView View, DestinationPath Path, FoldKey Key);

/// <summary>
/// Specification Section 15.1 steps 13 to 18: the transformation and output-planning phase.
/// </summary>
public static class PlanningPhase
{
    /// <summary>Section 15.1 step 13: expand wildcard scheme selectors and output declarations.</summary>
    /// <param name="configuration">Step 4's product.</param>
    /// <param name="model">Step 12's product, which supplies the concrete paths to expand against.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The concrete output instances, once no selector needs expanding.</returns>
    /// <remarks>
    /// <para>
    /// Section 14.1 fixes both the depth and the breadth of the expansion. The depth is "the last
    /// wildcard-containing selector part": for data under <c>a.x.y</c> the declaration <c>a.*</c>
    /// creates one <c>a.x</c> instance and "never creates <c>a.x.y</c>". The breadth is "exactly one
    /// instance per unique capture tuple and literalized selector, regardless of how many
    /// descendants matched beneath it", which is why matches are deduplicated by the literalized
    /// selector rather than counted.
    /// </para>
    /// <para>
    /// Literal parts after the last wildcard are appended to the matched prefix rather than looked
    /// up, mirroring Section 12.3's treatment of a generative template's literal suffix. A concrete
    /// instance whose subtree does not exist is still planned, because Section 14.1 plans an
    /// instance "even when no data path currently matches its literal prefix" — the wildcard part
    /// must match something, the literal suffix need not.
    /// </para>
    /// <para>
    /// The enumeration order is the one Section 12.4 gives wildcard candidates, through the walk
    /// both share. It is what <see cref="OutputInstance.WildcardMatchOrder"/> records, and Section
    /// 17.5 folds destinations by that order, so the two must be the same order.
    /// </para>
    /// </remarks>
    public static StepOutcome<ImmutableArray<OutputInstance>> ExpandWildcards(
        SchemeConfiguration configuration,
        OverlayNode model,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var expanded = ImmutableArray.CreateBuilder<OutputInstance>();

        foreach (var instance in configuration.Outputs)
        {
            if (instance.Selector.Name is not { } pattern
                || WildcardMatch.LastWildcardPart(pattern) < 0)
            {
                expanded.Add(instance);
                continue;
            }

            Expand(instance, pattern, model, expanded, diagnostics);
        }

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<ImmutableArray<OutputInstance>>()
            : StepOutcome.Produced(expanded.ToImmutable());
    }

    private static void Expand(
        OutputInstance instance,
        QualifiedName pattern,
        OverlayNode model,
        ImmutableArray<OutputInstance>.Builder expanded,
        DiagnosticBuffer diagnostics)
    {
        var depth = WildcardMatch.LastWildcardPart(pattern) + 1;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = 0;

        foreach (var path in OverlayAddressing.Candidates(model, depth))
        {
            if (!WildcardMatch.TryMatchPrefix(pattern.Parts, depth, path, out var captures))
            {
                continue;
            }

            var literalized = path;

            for (var i = depth; i < pattern.Parts.Length; i++)
            {
                literalized = literalized.Add(pattern.Parts[i]);
            }

            var selector = new SelectorKey(new QualifiedName(literalized));

            // Section 14.1: "There is exactly one instance per unique capture tuple and literalized
            // selector, regardless of how many descendants matched beneath it." The depth bound
            // above is what enforces the "regardless of descendants" half; this enforces the other.
            //
            // Removing it is not observable, and deliberately so: the walk yields one path per
            // distinct node at the depth, and the Section 19.1 spelling of a path is injective, so
            // two candidates cannot literalize to one selector. It is kept because it states the
            // Section 14.1 rule at the point the rule is about. Do not write a test for it; the
            // whole corpus is green with it deleted.
            if (!seen.Add(selector.ToString()))
            {
                continue;
            }

            expanded.Add(instance with
            {
                Selector = selector,
                Captures = captures,
                WildcardMatchOrder = order++,
            });
        }

        if (order > 0)
        {
            return;
        }

        // Section 14.1: "A wildcard output declaration that produces no concrete selector instance
        // emits WARN009 and creates no file." The instance is dropped rather than planned empty,
        // which is the one case Section 14.1's empty-view rule does not cover: an empty view means
        // the instance exists and selects nothing, and here there is no instance to select with.
        //
        // Section 22 supplies source, line, and declaration because the condition is a property of
        // the written declaration, and path because it names the selector that matched nothing.
        // There is no column: the condition is about the whole record, not a position inside it.
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Warn009(
                DiagnosticPhase.Planning,
                "\u00A714.1",
                $"the wildcard selector '{instance.Selector}' matches no concrete path, so it "
                + "creates no output instance and no file.",
                cardinalityKey: instance.Selector.ToString(),
                source: instance.Declaration.Source,
                line: instance.Declaration.Line,
                path: instance.Selector.ToString(),
                declaration: instance.Declaration.Text),
            StableOrderingKey.FromSource(instance.DeclarationOrder, 0)));
    }

    /// <summary>Section 15.1 step 14: build output instances and apply strict prefix filtering.</summary>
    /// <param name="instances">Step 13's product.</param>
    /// <param name="model">Step 12's product.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>One view per instance and format.</returns>
    public static StepOutcome<ImmutableArray<OutputView>> BuildOutputInstances(
        ImmutableArray<OutputInstance> instances,
        OverlayNode model,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var views = ImmutableArray.CreateBuilder<OutputView>();

        foreach (var instance in instances)
        {
            for (var ordinal = 0; ordinal < instance.Formats.Length; ordinal++)
            {
                var format = instance.Formats[ordinal];

                if (!format.TryAsFlat(out _))
                {
                    return StepOutcome.Unsupported<ImmutableArray<OutputView>>(
                        new UnsupportedCapability(
                            $"{format} output",
                            $"the declaration for selector '{instance.Selector}' asks for "
                            + $"{format}.",
                            "\u00A719"));
                }

                // Section 14.1: an instance is planned "even when no data path currently matches
                // its literal prefix", so a missing subtree is an empty view rather than no view.
                var selected = Descend(model, instance.Selector.Name);

                if (!TryRoot(instance, selected, diagnostics, out var root))
                {
                    continue;
                }

                views.Add(new OutputView(instance, format, ordinal, selected, root));
            }
        }

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<ImmutableArray<OutputView>>()
            : StepOutcome.Produced(views.ToImmutable());
    }

    /// <summary>Section 15.1 step 15: resolve references within each instance's closure.</summary>
    /// <param name="views">Step 14's product.</param>
    /// <param name="contributions">Step 5's product, which carries every unresolved value.</param>
    /// <returns>The same views, once every value is known to be already resolved.</returns>
    /// <remarks>
    /// Entries under a Section 8.6 mask are skipped. A suppressed path is never rendered and can
    /// never be a reference target, so its value is never resolved and its presence must not stand
    /// in the way of a run that is otherwise reference-free.
    /// </remarks>
    public static StepOutcome<ImmutableArray<OutputView>> ResolveReferences(
        ImmutableArray<OutputView> views,
        ImmutableArray<InputContribution> contributions)
    {
        var mask = InputPhase.MasksOf(contributions);

        foreach (var contribution in contributions)
        {
            var unresolved = contribution.Contribution.UnresolvedValues
                .Where(entry => !mask.Suppresses(entry.Name.Parts))
                .ToImmutableArray();

            if (unresolved.IsEmpty)
            {
                continue;
            }

            var entry = unresolved[0];

            return StepOutcome.Unsupported<ImmutableArray<OutputView>>(
                new UnsupportedCapability(
                    entry.Value.ContainsReference ? "references" : "value wildcards",
                    $"{contribution.Origin.Identity} line {entry.Line} carries one, and step 15 "
                    + "resolves it against the output instance's closure.",
                    "\u00A713.1"));
        }

        return StepOutcome.Produced(views);
    }

    /// <summary>Section 15.1 step 16: apply path-scoped transformations to each view.</summary>
    /// <param name="views">Step 15's product.</param>
    /// <param name="configuration">Step 4's product, whose deferred entries are the transformations.</param>
    /// <returns>The same views, once no transformation directive remains.</returns>
    /// <remarks>
    /// <c>root</c> is a step 16 transformation that this build does perform; it was resolved into
    /// <see cref="OutputView.Root"/> at step 14 because the same field carries Section 19.1's
    /// bare-scalar key, and one field with one meaning is worth more than an extra pass.
    /// </remarks>
    public static StepOutcome<ImmutableArray<OutputView>> ApplyTransformations(
        ImmutableArray<OutputView> views,
        SchemeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.Deferred.IsEmpty)
        {
            var entry = configuration.Deferred[0];

            return StepOutcome.Unsupported<ImmutableArray<OutputView>>(
                new UnsupportedCapability(
                    $"the {entry.Directive} directive",
                    $"'{entry.Declaration}' in {entry.Source} needs it.",
                    "\u00A716"));
        }

        return StepOutcome.Produced(views);
    }

    /// <summary>Section 15.1 step 17: group contributions by canonical destination path.</summary>
    /// <param name="views">Step 16's product.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>One contribution per view, each bound to its destination.</returns>
    public static StepOutcome<ImmutableArray<DestinationContribution>> GroupByDestination(
        ImmutableArray<OutputView> views,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var bound = ImmutableArray.CreateBuilder<DestinationContribution>();

        foreach (var view in views)
        {
            if (!TryDestination(view, diagnostics, bound.Count, out var path))
            {
                continue;
            }

            bound.Add(new DestinationContribution(
                view,
                path,
                new FoldKey(
                    view.Instance.DeclarationOrder,
                    view.FormatOrdinal,
                    WildcardMatchOrder: 0,
                    view.Instance.Selector.ToString())));
        }

        var contributions = bound.ToImmutable();

        // Section 17.5: "Two nonidentical canonical paths with the same portability key are a
        // blocking PATH001 collision rather than a merge." The check is on the whole plan rather
        // than per destination, because the colliding pair is by definition two destinations.
        var byPortability = new Dictionary<string, DestinationContribution>(StringComparer.Ordinal);

        foreach (var contribution in contributions.OrderBy(c => c.Key))
        {
            if (byPortability.TryGetValue(contribution.Path.PortabilityKey, out var earlier)
                && !string.Equals(
                    earlier.Path.Canonical, contribution.Path.Canonical, StringComparison.Ordinal))
            {
                diagnostics.Add(new BufferedDiagnostic(
                    DiagnosticCodes.Path001(
                        DiagnosticPhase.Planning,
                        "\u00A717.5",
                        $"'{contribution.Path.Canonical}' and '{earlier.Path.Canonical}' differ "
                        + "only by ASCII case, so they are one file on some platforms and two on "
                        + "others.",
                        cardinalityKey: contribution.Path.PortabilityKey,
                        destination: contribution.Path.Canonical),
                    DestinationOrder: byPortability.Count));

                continue;
            }

            byPortability.TryAdd(contribution.Path.PortabilityKey, contribution);
        }

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<ImmutableArray<DestinationContribution>>()
            : StepOutcome.Produced(contributions);
    }

    /// <summary>Section 15.1 step 18: fold same-format collisions and cross-format overrides.</summary>
    /// <param name="contributions">Step 17's product.</param>
    /// <returns>The contributions, once no two share a destination.</returns>
    public static StepOutcome<ImmutableArray<DestinationContribution>> FoldDestinationCollisions(
        ImmutableArray<DestinationContribution> contributions)
    {
        var seen = new Dictionary<string, DestinationContribution>(StringComparer.Ordinal);

        foreach (var contribution in contributions.OrderBy(c => c.Key))
        {
            if (seen.TryGetValue(contribution.Path.Canonical, out var earlier))
            {
                return StepOutcome.Unsupported<ImmutableArray<DestinationContribution>>(
                    new UnsupportedCapability(
                        "destination collisions",
                        $"'{earlier.View.Instance.Selector}' and "
                        + $"'{contribution.View.Instance.Selector}' both write "
                        + $"'{contribution.Path.Canonical}', which step 18 folds under 'filemerge'.",
                        "\u00A717.5"));
            }

            seen.Add(contribution.Path.Canonical, contribution);
        }

        return StepOutcome.Produced(contributions);
    }


    /// <summary>
    /// Section 14.2 strict prefix filtering, plus Section 14.1's unconditional removal of the
    /// concrete selector prefix.
    /// </summary>
    private static OverlayNode Descend(OverlayNode model, QualifiedName? selector)
    {
        if (selector is null)
        {
            return model;
        }

        var node = model;

        foreach (var part in selector.Parts)
        {
            if (!node.Children.TryGetValue(part, out var child))
            {
                return OverlayNode.Empty(node.Marks);
            }

            node = child;
        }

        return node;
    }

    /// <summary>
    /// The Section 16.3 root parts of a view, applying Section 14.1's rule that a flat format
    /// cannot render a bare scalar without a key.
    /// </summary>
    private static bool TryRoot(
        OutputInstance instance,
        OverlayNode selected,
        DiagnosticBuffer diagnostics,
        out ImmutableArray<NamePart> root)
    {
        if (instance.Root is { } configured)
        {
            root = configured.Parts;
            return true;
        }

        if (selected.Payload is null)
        {
            root = [];
            return true;
        }

        // Section 19.1 and 19.2: "When the selected output root is a bare scalar, namespace output
        // retains the final concrete selector part as the emitted key."
        if (instance.Selector.Name is { } name)
        {
            root = [name.Parts[^1]];
            return true;
        }

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Type001(
                DiagnosticPhase.Planning,
                "\u00A714.1",
                "the empty root selector selects a bare scalar, and namespace, quoted namespace, "
                + "and INI require an explicit 'root' because no key identity exists otherwise.",
                cardinalityKey: instance.Selector.ToString(),
                declaration: $"output={instance.Formats[0]}"),
            OrderingKey: StableOrderingKey.First));

        root = [];
        return false;
    }

    private static bool TryDestination(
        OutputView view,
        DiagnosticBuffer diagnostics,
        int order,
        out DestinationPath path)
    {
        string? violation;

        if (view.Instance.Filename is { } written)
        {
            if (DestinationPathComposer.TryCompose(written, out var composed, out violation))
            {
                path = composed;
                return true;
            }
        }
        else if (DefaultDestination(view, out var derived, out violation))
        {
            path = derived!;
            return true;
        }

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Path001(
                DiagnosticPhase.Planning,
                "\u00A716.2",
                $"the destination for selector '{view.Instance.Selector}' is rejected: {violation}",
                cardinalityKey: view.Instance.Selector.ToString() + '\u001F' + view.Format,
                declaration: view.Instance.Filename),
            DestinationOrder: order));

        path = new DestinationPath(string.Empty);
        return false;
    }

    /// <summary>
    /// Section 16.2's default file name: the dot-joined concrete selector, each part encoded by the
    /// portable rules with literal <c>.</c> additionally encoded, forming one filename segment.
    /// </summary>
    /// <remarks>
    /// The result is composed directly rather than through
    /// <see cref="DestinationPathComposer.TryCompose"/>, which would encode the <c>%</c> of every
    /// escape this method just produced and turn <c>a%2Eb.properties</c> into
    /// <c>a%252Eb.properties</c>.
    /// </remarks>
    private static bool DefaultDestination(
        OutputView view,
        out DestinationPath? path,
        out string? violation)
    {
        path = null;
        violation = null;

        var stem = "output";

        if (view.Instance.Selector.Name is { } name)
        {
            var parts = new List<string>(name.Parts.Length);

            foreach (var part in name.Parts)
            {
                if (!PortableSegment.TryEncode(Spell(part), out var encoded, out violation))
                {
                    violation = $"the selector part is not portable: {violation}";
                    return false;
                }

                parts.Add(encoded!.Replace(".", "%2E", StringComparison.Ordinal));
            }

            stem = string.Join('.', parts);
        }

        // DefaultExtension carries its own leading dot, so the stem is concatenated rather than
        // joined: a second dot would produce 'a..properties'.
        path = new DestinationPath(stem + view.Format.DefaultExtension());
        return true;
    }

    private static string Spell(NamePart part) =>
        part is OrdinaryPart { LiteralText: { } text } ? text : part.ToString();
}
