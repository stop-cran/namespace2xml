using System.Collections.Immutable;
using Namespace2Xml.Budgets;
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
public sealed record DestinationContribution(OutputView View, DestinationPath Path, FoldKey Key)
{
    /// <summary>Orders destinations as Section 21.3 publishes them.</summary>
    /// <param name="contributions">The folded plan, one contribution per destination.</param>
    /// <returns>The contributions in Section 21.3 destination order.</returns>
    /// <remarks>
    /// Section 21.3 orders "by the minimum Section 17.5 fold key among contributions whose data or
    /// retained destination high-water state survives into the final folded plan, then by canonical
    /// relative path compared as unsigned UTF-8 bytes". The surviving key is what the fold leaves
    /// on the contribution, so the first component reads straight off it. The tie-break is not
    /// decoration: one declaration can write two destinations, so the key alone is not a total
    /// order, and Section 24 orders a whole diagnostic group by the resulting index.
    /// </remarks>
    public static IEnumerable<DestinationContribution> InPublicationOrder(
        IEnumerable<DestinationContribution> contributions) =>
        contributions
            .OrderBy(contribution => contribution.Key)
            .ThenBy(contribution => contribution.Path, DestinationPath.Utf8Bytes);
}

/// <summary>
/// Specification Section 15.1 steps 13 to 18: the transformation and output-planning phase.
/// </summary>
public static class PlanningPhase
{
    /// <summary>Section 15.1 step 13: expand wildcard scheme selectors and output declarations.</summary>
    /// <param name="configuration">Step 4's product.</param>
    /// <param name="model">Step 12's product, which supplies the concrete paths to expand against.</param>
    /// <param name="budget">The invocation's global budget, which selector candidates also consume.</param>
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
        GlobalBudget budget,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var expanded = ImmutableArray.CreateBuilder<OutputInstance>();

        // Section 12.4 counts a candidate check "once for a (rule,item) pair", and the pairs a
        // selector checks are disjoint from every template's, so this set starts empty rather than
        // continuing the evaluator's: no pair can appear in both.
        var considered = new HashSet<(string Selector, string Path)>();

        foreach (var instance in configuration.Outputs)
        {
            if (instance.Selector.Name is not { } pattern
                || WildcardMatch.LastWildcardPart(pattern) < 0)
            {
                expanded.Add(instance);
                continue;
            }

            if (!Expand(instance, pattern, model, expanded, considered, budget, diagnostics))
            {
                return StepOutcome.Failed<ImmutableArray<OutputInstance>>();
            }
        }

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<ImmutableArray<OutputInstance>>()
            : StepOutcome.Produced(Consolidate(expanded));
    }

    /// <summary>
    /// Section 15.2: "exact and wildcard declarations that literalize to the same concrete selector
    /// participate in one source-ordered override stream".
    /// </summary>
    /// <param name="expanded">Every instance produced by literalizing the declarations.</param>
    /// <returns>One effective instance per concrete selector, in declaration order.</returns>
    /// <remarks>
    /// <para>
    /// Until the declarations are folded they are two independent instances writing two files, so
    /// <c>a.*.output=namespace</c> followed by <c>a.x.output=ignore</c> still writes <c>a.x</c>. The
    /// Section 15.2 table gives <c>output=ignore</c> the scope "one concrete output instance" and the
    /// restoration "later non-ignore <c>output</c> declaration", which only means anything if both
    /// declarations are in one stream.
    /// </para>
    /// <para>
    /// Each setting is taken from the latest member that declared it, which is why the nullable
    /// settings and the declaration sites are consulted rather than the values: a member that never
    /// wrote <c>filemerge</c> carries the Section 16.11 default, and letting that default win over an
    /// earlier explicit declaration would invert the override. <see cref="OutputInstance.Captures"/>
    /// travels with <see cref="OutputInstance.FilenameTemplate"/> because it is only ever applied to
    /// it, and a template written on a wildcard declaration must be substituted with that
    /// declaration's captures rather than the winning one's.
    /// </para>
    /// </remarks>
    private static ImmutableArray<OutputInstance> Consolidate(
        ImmutableArray<OutputInstance>.Builder expanded)
    {
        var streams = new Dictionary<SelectorKey, OutputInstance>();
        var order = new List<SelectorKey>();

        foreach (var instance in expanded)
        {
            if (!streams.TryGetValue(instance.Selector, out var accumulated))
            {
                streams.Add(instance.Selector, instance);
                order.Add(instance.Selector);
                continue;
            }

            streams[instance.Selector] = instance with
            {
                FilenameTemplate = instance.FilenameDeclaration is null
                    ? accumulated.FilenameTemplate
                    : instance.FilenameTemplate,
                Captures = instance.FilenameDeclaration is null
                    ? accumulated.Captures
                    : instance.Captures,
                FilenameDeclaration = instance.FilenameDeclaration ?? accumulated.FilenameDeclaration,
                Root = instance.Root ?? accumulated.Root,
                Delimiter = instance.Delimiter ?? accumulated.Delimiter,
                IniOptions = instance.IniOptionsDeclaration is null
                    ? accumulated.IniOptions
                    : instance.IniOptions,
                IniOptionsDeclaration =
                    instance.IniOptionsDeclaration ?? accumulated.IniOptionsDeclaration,
                FileMerge = instance.FileMergeDeclaration is null
                    ? accumulated.FileMerge
                    : instance.FileMerge,
                FileMergeDeclaration =
                    instance.FileMergeDeclaration ?? accumulated.FileMergeDeclaration,
            };
        }

        // Section 16.1 compiles 'output=ignore' to an empty format set, and Section 14.2 says a
        // selector whose winning declaration is 'output=ignore' "creates no output instance". The
        // suppression happens here rather than at compile time because only the folded stream knows
        // which declaration won.
        return
        [
            .. order
                .Select(selector => streams[selector])
                .Where(instance => !instance.Formats.IsEmpty),
        ];
    }

    private static bool Expand(
        OutputInstance instance,
        QualifiedName pattern,
        OverlayNode model,
        ImmutableArray<OutputInstance>.Builder expanded,
        HashSet<(string Selector, string Path)> considered,
        GlobalBudget budget,
        DiagnosticBuffer diagnostics)
    {
        var depth = WildcardMatch.LastWildcardPart(pattern) + 1;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = 0;
        var selector = instance.Selector.ToString();

        foreach (var path in OverlayAddressing.Candidates(model, pattern.Parts, depth))
        {
            // Section 12.4: "Every wildcard rule category consumes the shared candidate-check limit
            // once per eligible pair", and the categories it names include "wildcard scheme
            // selectors". The walk above has already settled the first two conditions, so reaching
            // here is the pair being eligible; the set settles the third. The charge precedes the
            // match because "full capture matching may then succeed or fail without another
            // candidate charge" -- a selector that examines an item has spent the check whether or
            // not the captures agree.
            if (considered.Add((selector, CanonicalPath.Of(path) ?? string.Empty))
                && !budget.TryConsume(ResourceBound.MaxWildcardCandidates, 1, out var tooMany))
            {
                diagnostics.Add(new BufferedDiagnostic(
                    DiagnosticCodes.Wildcard002(
                        DiagnosticPhase.Planning,
                        "\u00A712.4",
                        $"wildcard selector expansion crosses {tooMany.Spelling}, whose limit is "
                        + $"{tooMany.Limit}.",
                        rule: [selector]),
                    StableOrderingKey.FromSource(instance.DeclarationOrder, 0)));

                return false;
            }

            if (!WildcardMatch.TryMatchPrefix(pattern.Parts, depth, path, out var captures))
            {
                continue;
            }

            var literalized = path;

            for (var i = depth; i < pattern.Parts.Length; i++)
            {
                literalized = literalized.Add(pattern.Parts[i]);
            }

            var selectorKey = new SelectorKey(new QualifiedName(literalized));

            // Section 14.1: "There is exactly one instance per unique capture tuple and literalized
            // selector, regardless of how many descendants matched beneath it." The depth bound
            // above is what enforces the "regardless of descendants" half; this enforces the other.
            //
            // Removing it is not observable, and deliberately so: the walk yields one path per
            // distinct node at the depth, and the Section 19.1 spelling of a path is injective, so
            // two candidates cannot literalize to one selector. It is kept because it states the
            // Section 14.1 rule at the point the rule is about. Do not write a test for it; the
            // whole corpus is green with it deleted.
            if (!seen.Add(selectorKey.ToString()))
            {
                continue;
            }

            expanded.Add(instance with
            {
                Selector = selectorKey,
                Captures = captures,
                WildcardMatchOrder = order++,
            });
        }

        if (order > 0)
        {
            return true;
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

        return true;
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
    /// <param name="model">Step 12's product, which holds every unresolved payload.</param>
    /// <param name="budget">The invocation's Section 23 budgets.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The views, re-descended into the resolved model.</returns>
    /// <remarks>
    /// <para>
    /// Section 14.4 makes the selected subtrees the reachability roots: "All entries reached
    /// transitively through references from selected entries are retained for evaluation", and
    /// defects "in entries unreachable from every concrete output instance do not fail the run".
    /// A view whose declaration is <c>output=ignore</c> is not a root, and Section 14.4 says so
    /// outright — but nothing is needed here for that, because step 13 already created no instance
    /// for it, so it contributes no view and therefore no root.
    /// </para>
    /// <para>
    /// The views are rebuilt rather than resolved one at a time. Two instances selecting overlapping
    /// subtrees must see one resolution of the shared entries: resolving per view would let the same
    /// reference be reported twice, which Section 22 forbids for a code counted once per reachable
    /// owning value.
    /// </para>
    /// </remarks>
    public static StepOutcome<ImmutableArray<OutputView>> ResolveReferences(
        ImmutableArray<OutputView> views,
        OverlayNode model,
        GlobalBudget budget,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var roots = views
            .Select(view => view.Instance.Selector.Name?.Parts ?? [])
            .ToImmutableArray();

        var resolved = ReferenceResolver.Resolve(model, roots, budget, diagnostics);

        if (diagnostics.HasBlockingError)
        {
            return StepOutcome.Failed<ImmutableArray<OutputView>>();
        }

        ImmutableArray<OutputView> rebuilt =
        [
            .. views.Select(view => view with
            {
                View = Descend(resolved, view.Instance.Selector.Name),
            }),
        ];

        return StepOutcome.Produced(rebuilt);
    }

    /// <summary>Section 15.1 step 16: apply path-scoped transformations to each view.</summary>
    /// <param name="views">Step 15's product.</param>
    /// <param name="configuration">Step 4's product, which carries the compiled rules.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The transformed views.</returns>
    /// <remarks>
    /// <c>root</c> is the last transformation in the step 16 order; it was resolved into
    /// <see cref="OutputView.Root"/> at step 14 because the same field carries Section 19.1's
    /// bare-scalar key, and it is applied at serialization, which is the same position in the order
    /// because it only wraps the finished view.
    /// </remarks>
    public static StepOutcome<ImmutableArray<OutputView>> ApplyTransformations(
        ImmutableArray<OutputView> views,
        SchemeConfiguration configuration,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (!configuration.Deferred.IsEmpty)
        {
            var entry = configuration.Deferred[0];

            return StepOutcome.Unsupported<ImmutableArray<OutputView>>(
                new UnsupportedCapability(
                    $"the {entry.Directive} directive",
                    $"'{entry.Declaration}' in {entry.Source} needs it.",
                    "\u00A716"));
        }

        if (configuration.Transforms.IsEmpty)
        {
            return StepOutcome.Produced(views);
        }

        var transformed = ImmutableArray.CreateBuilder<OutputView>(views.Length);
        var bound = new HashSet<StableOrderingKey>();

        foreach (var view in views)
        {
            var result = ViewTransformer.Transform(
                view.View,
                view.Instance.Selector.Name?.Parts ?? [],
                configuration.Transforms,
                (occurrence, order) => diagnostics.Add(new BufferedDiagnostic(occurrence, order)),
                bound);

            // Section 16.6 removes an ignored subtree "from the selected output instance only". An
            // ignore that reached the view root is already TYPE001, so a null here is unreachable
            // through a successful run; dropping the view keeps the invariant explicit rather than
            // relying on the diagnostic having been raised.
            if (result is not null)
            {
                // Section 19.1 states the bare-scalar rule about "the selected output root", and
                // rendering is Section 15.1 step 19 — after this pass. A view that was a sequence
                // when step 15 derived its root and is a scalar now needs the final selector part
                // as its key, or Section 19.1 has no key to write and rejects valid input; a view
                // that went the other way must lose the key it no longer needs. Step 15 stops the
                // run on its own TYPE001, so this cannot report the same condition twice.
                if (!TryRoot(view.Instance, result, diagnostics, out var root))
                {
                    continue;
                }

                transformed.Add(view with { View = result, Root = root });
            }
        }

        WarnUnbound(configuration.Transforms, bound, diagnostics);

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<ImmutableArray<OutputView>>()
            : StepOutcome.Produced(transformed.ToImmutable());
    }

    /// <summary>
    /// Section 15.2: an "output-view transformation that binds to no concrete output instance emits
    /// one scheme warning and is otherwise inert."
    /// </summary>
    /// <param name="rules">Every compiled rule, in source order.</param>
    /// <param name="bound">The rules that bound to a live path in at least one view.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <remarks>
    /// Section 22 counts <c>WARN009</c> once per declaration, so the union over every view is taken
    /// before warning: a rule that binds in one output instance and nowhere else has bound.
    /// </remarks>
    private static void WarnUnbound(
        ImmutableArray<TransformRule> rules,
        HashSet<StableOrderingKey> bound,
        DiagnosticBuffer diagnostics)
    {
        foreach (var rule in rules)
        {
            if (bound.Contains(rule.Order))
            {
                continue;
            }

            var path = rule.Path is { } pattern ? CanonicalPath.Of(pattern.Parts) : null;

            diagnostics.Add(new BufferedDiagnostic(
                DiagnosticCodes.Warn009(
                    DiagnosticPhase.Planning,
                    "\u00A715.2",
                    $"'{rule.Declaration.Text}' matches no path in any output instance, so it has "
                    + "no effect.",
                    cardinalityKey: $"{rule.Declaration.Source}:{rule.Declaration.Line}",
                    source: rule.Declaration.Source,
                    line: rule.Declaration.Line,
                    path: path,
                    declaration: rule.Declaration.Text),
                rule.Order));
        }
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
                    view.Instance.WildcardMatchOrder,
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
    /// <param name="budget">The invocation's Section 23 budgets.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The contributions, once no two share a destination.</returns>
    /// <remarks>
    /// <para>
    /// Section 17.5 folds "strictly left to right" by the four-component fold key, whose third
    /// component is the Section 12.4 wildcard match order. The order is the whole of the
    /// determinism argument here: the accumulated model depends on which contribution arrives
    /// first, so folding in dictionary order would make the file's contents depend on a hash seed.
    /// </para>
    /// <para>
    /// Because the key ascends, the first contribution seen at a destination carries the smallest
    /// key. That is what makes Section 17.5's publication-key rule a matter of not overwriting a
    /// field: "same-format <c>replace</c> preserves the earliest prior publication key even when no
    /// prior sequence high-water state exists; only cross-format replacement resets it".
    /// </para>
    /// <para>
    /// Section 16.3's <c>root</c> is folded into the model before merging rather than being applied
    /// at serialization, because Section 17.5 says "file-level merge operates on fully transformed
    /// contribution models after <c>type</c>, <c>key</c>, and <c>root</c> have been applied". Two
    /// contributions with different roots therefore merge as siblings under one document instead of
    /// overwriting each other at the document root.
    /// </para>
    /// <para>
    /// Section 6.2's <c>--max-outputs</c> bounds "planned destination files", and this is where a
    /// destination file becomes planned: step 17 groups contributions and step 18 is the last place
    /// one destination can still absorb another, so counting before the fold would charge for files
    /// that never exist. The charge is one per newly seen destination rather than a single tally at
    /// the end, because Section 23 accounts "before allocation or expansion whenever possible" and
    /// wants the crossing attributed to the destination that crossed it.
    /// </para>
    /// </remarks>
    public static StepOutcome<ImmutableArray<DestinationContribution>> FoldDestinationCollisions(
        ImmutableArray<DestinationContribution> contributions,
        GlobalBudget budget,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var folded = new Dictionary<string, DestinationContribution>(StringComparer.Ordinal);
        var order = new List<string>();
        var pending = new List<(string Canonical, DiagnosticOccurrence Occurrence)>();

        foreach (var contribution in contributions.OrderBy(c => c.Key))
        {
            var canonical = contribution.Path.Canonical;
            var rooted = Rooted(contribution);

            if (!folded.TryGetValue(canonical, out var accumulated))
            {
                if (!budget.TryConsume(ResourceBound.MaxOutputs, 1, out var tooMany))
                {
                    diagnostics.Add(new BufferedDiagnostic(
                        DiagnosticCodes.Limit001(
                            DiagnosticPhase.Planning,
                            "\u00A723",
                            $"planning this destination crosses {tooMany.Spelling}, whose limit is "
                            + $"{tooMany.Limit}.",
                            path: canonical),
                        StableOrderingKey.FromSource(
                            contribution.View.Instance.DeclarationOrder, 0)));

                    return StepOutcome.Failed<ImmutableArray<DestinationContribution>>();
                }

                folded.Add(canonical, rooted);
                order.Add(canonical);
                continue;
            }

            folded[canonical] = Fold(accumulated, rooted, pending, diagnostics);
        }

        Number(pending, order.Select(canonical => folded[canonical]), diagnostics);

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<ImmutableArray<DestinationContribution>>()
            : StepOutcome.Produced(
                ImmutableArray.CreateRange(order.Select(canonical => folded[canonical])));
    }

    /// <summary>
    /// Buffers step 18's destination diagnostics against the Section 21.3 index of the destination
    /// each concerns.
    /// </summary>
    /// <param name="pending">What the fold decided, in the order it decided it.</param>
    /// <param name="final">The folded plan, one contribution per destination.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <remarks>
    /// <para>
    /// Section 24's second group is ordered "in the Section 21.3 destination order", and Section
    /// 21.3 orders "by the minimum Section 17.5 fold key among contributions whose data or retained
    /// destination high-water state survives into the final folded plan, then by canonical relative
    /// path compared as unsigned UTF-8 bytes". Neither component is known while the fold is running.
    /// The surviving key is not, because a cross-format replacement discards the accumulated plan
    /// and resets the key to the replacing contribution, which may be any later contribution; and
    /// the path tie-break is not, because it only decides between destinations that have all been
    /// seen. Numbering a destination when it is first reached therefore records the order in which
    /// the fold happened to meet destinations, which is a different order whenever a cross-format
    /// replacement moves a key past another destination's, or two destinations share a key.
    /// </para>
    /// <para>
    /// The order is taken from <see cref="DestinationContribution.InPublicationOrder"/>, which step
    /// 19 also uses to number its own diagnostics and to publish. One expression, so the two steps
    /// cannot drift into disagreeing about the order of the same run's stream.
    /// </para>
    /// </remarks>
    private static void Number(
        List<(string Canonical, DiagnosticOccurrence Occurrence)> pending,
        IEnumerable<DestinationContribution> final,
        DiagnosticBuffer diagnostics)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var contribution in DestinationContribution.InPublicationOrder(final))
        {
            index[contribution.Path.Canonical] = index.Count;
        }

        foreach (var (canonical, occurrence) in pending)
        {
            diagnostics.Add(new BufferedDiagnostic(
                occurrence, DestinationOrder: index[canonical]));
        }
    }

    /// <summary>
    /// Section 16.3's <c>root</c>, applied to the model so that Section 17.5 can fold "after
    /// <c>type</c>, <c>key</c>, and <c>root</c> have been applied".
    /// </summary>
    /// <remarks>
    /// The wrapping nodes are intermediate rather than explicit mappings: Section 16.3 says
    /// <c>root</c> "wraps the selector's remaining content", and an explicit mapping presence would
    /// be a container contribution the user never wrote, which Section 4.4 would then let win a
    /// shape contest against a scalar arriving from another contribution.
    /// </remarks>
    private static DestinationContribution Rooted(DestinationContribution contribution)
    {
        var view = contribution.View;

        if (view.Root.IsDefaultOrEmpty)
        {
            return contribution;
        }

        var wrapped = view.View;

        for (var i = view.Root.Length - 1; i >= 0; i--)
        {
            wrapped = OverlayNode
                .Intermediate(view.View.Marks.Latest)
                .WithChild(view.Root[i], wrapped);
        }

        return contribution with { View = view with { View = wrapped, Root = [] } };
    }

    /// <summary>The Section 22 cardinality key identifying one folded contribution pair.</summary>
    /// <param name="canonical">The destination both contributions write.</param>
    /// <param name="accumulated">The plan accumulated for that destination so far.</param>
    /// <param name="later">The contribution being folded onto it.</param>
    /// <remarks>
    /// Section 22 counts both codes raised here "per folded contribution pair" and "per rejected
    /// contribution after the first", so the cardinality key has to identify the <i>pair</i>. Naming
    /// only the later contribution is not enough: one destination fed by a multi-format wildcard
    /// declaration meets the same two selectors once per format, and a key that dropped the format
    /// would silently retire the second report as a repeat of the first. That is the half the corpus
    /// exercises, through <c>one-destination-folds-by-format-before-match-order</c>. The accumulated
    /// side is here because the key is meant to name the pair rather than one end of it — a
    /// same-format <c>replace</c> moves the accumulated selector while the later one repeats — but no
    /// fixture separates it yet.
    /// </remarks>
    private static string PairKey(
        string canonical,
        DestinationContribution accumulated,
        DestinationContribution later) =>
        string.Join(
            '\u001F',
            canonical,
            accumulated.View.Format,
            accumulated.View.Instance.Selector,
            later.View.Format,
            later.View.Instance.Selector);

    /// <summary>Folds one later contribution onto the accumulated plan for its destination.</summary>
    private static DestinationContribution Fold(
        DestinationContribution accumulated,
        DestinationContribution later,
        List<(string Canonical, DiagnosticOccurrence Occurrence)> pending,
        DiagnosticBuffer diagnostics)
    {
        var canonical = later.Path.Canonical;
        var strategy = later.View.Instance.FileMerge;
        var crossFormat = accumulated.View.Format != later.View.Format;

        if (!crossFormat && strategy == MergeStrategy.Error)
        {
            // Appendix B maps "'filemerge=error' rejects a second destination contribution" to
            // COLLISION001, which is a different condition from the WARN005 fold the other three
            // strategies perform. No warning accompanies it: nothing was folded.
            pending.Add((canonical, DiagnosticCodes.Collision001(
                DiagnosticPhase.Planning,
                "\u00A716.11",
                $"'{later.View.Instance.Selector}' is a second contribution to "
                + $"'{canonical}', which '{accumulated.View.Instance.Selector}' already writes, "
                + "and the effective 'filemerge' is 'error'.",
                cardinalityKey: PairKey(canonical, accumulated, later),
                declaration: later.View.Instance.FileMergeDeclaration?.Text,
                destination: canonical)));

            return accumulated;
        }

        // Section 17.5: "For every destination collision, emit a warning identifying: destination
        // path; earlier declaration; later declaration; merge or replacement decision." WARN005
        // carries only 'destination' in Appendix B, so the other three facts are in the message.
        pending.Add((canonical, DiagnosticCodes.Warn005(
            DiagnosticPhase.Planning,
            "\u00A717.5",
            $"'{canonical}' is written by '{accumulated.View.Instance.Declaration.Text}' and "
            + $"'{later.View.Instance.Declaration.Text}'; "
            + (crossFormat
                ? $"the formats differ, so the later {later.View.Format} contribution replaces "
                    + $"the complete {accumulated.View.Format} plan"
                : $"the formats agree, so they are folded under 'filemerge="
                    + $"{strategy.ToString().ToLowerInvariant()}'")
            + ".",
            cardinalityKey: PairKey(canonical, accumulated, later),
            destination: canonical)));

        if (crossFormat)
        {
            // Section 17.5: "A cross-format replacement discards the complete accumulated plan for
            // that destination, including document data, comments, renderer state, sequence
            // provenance, and every destination high-water mark." Taking the later contribution
            // whole is exactly that, and it is also what resets the publication key.
            return later;
        }

        var merger = new OverlayMerger(
            MergeStrategyMap.Create([], strategy),
            diagnostics,
            context: MergeContext.Destination);

        // The accumulated key is kept rather than recomputed. Contributions arrive in ascending key
        // order, so it is already the earliest, which is what Section 17.5 requires a same-format
        // fold -- including 'replace' -- to preserve. The instance comes from the later declaration
        // because Section 16.11 says "the effective value is taken from the later colliding output
        // declaration", and Section 17.5 says the same of the renderer option set.
        return accumulated with
        {
            View = later.View with
            {
                View = merger.Merge(accumulated.View.View, later.View.View),
            },
        };
    }

    /// <summary>
    /// Section 14.2 strict prefix filtering, plus Section 14.1's unconditional removal of the
    /// concrete selector prefix.
    /// </summary>
    /// <remarks>
    /// Descent goes through <see cref="OverlayAddressing.TryAddress"/> because Section 15.1 step 9
    /// makes a canonical numeric mapping child and the sequence item with that value "one structural
    /// overlay node for merging, comments, references, selectors, generation, and wildcard
    /// candidacy". Walking only <see cref="OverlayNode.Children"/> here selects nothing at a native
    /// sequence item, and Section 14.1's rule that a namespace output with no records still emits
    /// its normalized empty file makes that loss silent.
    /// </remarks>
    private static OverlayNode Descend(OverlayNode model, QualifiedName? selector)
    {
        if (selector is null)
        {
            return model;
        }

        var node = model;

        foreach (var part in selector.Parts)
        {
            if (!OverlayAddressing.TryAddress(node, part, out var child))
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

        if (view.Instance.FilenameTemplate is { } template)
        {
            if (DestinationPathComposer.TryCompose(
                template, view.Instance.Captures, out var composed, out violation))
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

        // Section 22: 'declaration' names "one scheme declaration ... its canonical spelling". The
        // condition here is that this 'filename' composes to an illegal path, so it names the
        // 'filename' declaration rather than the directive's value. An instance taking a Section
        // 16.2 default name has no such declaration, and the member is omitted rather than being
        // filled from the 'output' declaration, which is not at fault.
        //
        // Appendix B gives PATH001 the members 'declaration' and 'destination' only, so the
        // declaration's source and line are not carried even though Section 22 would supply them
        // for a condition about a declaration.
        //
        // Section 22 counts PATH001 "once per destination". The key is therefore the written
        // destination that failed to compose, and it excludes the format: Section 16.2 gives an
        // explicit 'filename' no format extension, so 'output=namespace,ini' with one bad filename
        // is one rejected destination reached twice, not two.
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Path001(
                DiagnosticPhase.Planning,
                "\u00A716.2",
                $"the destination for selector '{view.Instance.Selector}' is rejected: {violation}",
                cardinalityKey: view.Instance.Filename ?? view.Instance.Selector.ToString(),
                declaration: view.Instance.FilenameDeclaration?.Text),
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
