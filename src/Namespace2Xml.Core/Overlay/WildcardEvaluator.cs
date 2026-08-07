using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

/// <summary>
/// Pipeline step 10: evaluates wildcard templates to the Section 12.4 deterministic fixed point.
/// </summary>
/// <remarks>
/// <para>
/// Section 12.4 fixes both the shape of the search and its accounting. Evaluation "proceeds in
/// deterministic breadth waves": one wave evaluates every eligible pair against the items present
/// when the wave began, and items generated during the wave become eligible in the next one. A
/// <c>(rule, item)</c> pair is considered at most once for the whole run, which is what makes the
/// search terminate on any rule set whose generated names stay inside the tree.
/// </para>
/// <para>
/// Candidacy is not the same question as matching, and the clause separates them deliberately: a
/// pair is charged against <c>--max-wildcard-candidates</c> when the item is deep enough and every
/// literal part before the rule's last wildcard part agrees, and "full capture matching may then
/// succeed or fail without another candidate charge". Charging on match instead would make the
/// limit measure results rather than work, and a pathological rule set does its damage in the
/// checks it forces, not in the entries it produces.
/// </para>
/// <para>
/// Masks take part as rules rather than as a filter applied afterwards. Section 12.4 counts
/// "permanent wildcard ignore masks" among the categories that "consume the shared candidate-check
/// limit once per eligible pair", and Section 8.6 makes them predicates that "suppress every
/// matching candidate whenever it appears". A mask therefore occupies a place in the worklist,
/// charges its candidate checks, and generates nothing.
/// </para>
/// </remarks>
public sealed class WildcardEvaluator
{
    private readonly ImmutableArray<WildcardRule> rules;
    private readonly ImmutableArray<int> depths;
    private readonly ExclusionMask mask;
    private readonly OverlayMerger merger;
    private readonly GlobalBudget budget;
    private readonly DiagnosticBuffer diagnostics;
    private readonly HashSet<(int Rule, string Path)> considered = [];
    private readonly long[] matches;
    private long chargedWave;

    /// <summary>Creates an evaluator for one run's rules.</summary>
    /// <param name="rules">Every wildcard rule, in Section 12.4 source order.</param>
    /// <param name="mask">The run's Section 8.6 mask union.</param>
    /// <param name="merger">The merger carrying the effective Section 16.10 strategy at each path.</param>
    /// <param name="budget">The invocation's global budget.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    public WildcardEvaluator(
        ImmutableArray<WildcardRule> rules,
        ExclusionMask mask,
        OverlayMerger merger,
        GlobalBudget budget,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(merger);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.rules = rules.IsDefault ? [] : rules;
        this.mask = mask;
        this.merger = merger;
        this.budget = budget;
        this.diagnostics = diagnostics;

        // Section 12.3 matches a template "through its last wildcard-containing name part", and
        // Section 12.4 counts candidates at that same depth, so the depth is a property of the rule
        // and is computed once rather than per wave.
        depths = [.. this.rules.Select(rule => WildcardMatch.LastWildcardPart(rule.Name) + 1)];
        matches = new long[this.rules.Length];
    }

    /// <summary>
    /// Rejects the Section 12.2 capture faults that are properties of a rule rather than of a match.
    /// </summary>
    /// <param name="rules">The run's rules.</param>
    /// <param name="diagnostics">The buffer the faults accumulate in.</param>
    /// <returns>The rules that are well formed, in the order they were given.</returns>
    /// <remarks>
    /// Both faults are decidable without looking at any data, and Section 22 makes
    /// <c>WILDCARD001</c> once per rule, so they are reported here and the rule is dropped. Dropping
    /// rather than aborting is what lets one run report every bad rule: Section 15.4 continues after
    /// a blocking error, and a run that stopped at the first would hide the rest.
    /// </remarks>
    public static ImmutableArray<WildcardRule> Validate(
        ImmutableArray<WildcardRule> rules,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var accepted = ImmutableArray.CreateBuilder<WildcardRule>();

        foreach (var rule in rules)
        {
            var captures = QualifiedNameLexer.Wildcards(rule.Name).ToList();

            if (captures.Exists(capture => capture.CaptureId is null)
                && captures.Exists(capture => capture.CaptureId is not null))
            {
                Reject(
                    rule,
                    "Section 12.2: a single rule must not mix explicit and legacy unnamed captures.",
                    diagnostics);
                continue;
            }

            var declared = captures
                .Select(capture => capture.CaptureId)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

            var undefined = rule.Value is null
                ? null
                : rule.Value.Tokens
                    .OfType<ValueWildcardToken>()
                    .Select(token => token.CaptureId)
                    .OfType<string>()
                    .FirstOrDefault(id => !declared.Contains(id));

            if (undefined is not null)
            {
                Reject(
                    rule,
                    $"Section 12.2: the value substitutes '*[{undefined}]', which this rule's name "
                    + "does not define.",
                    diagnostics);
                continue;
            }

            accepted.Add(rule);
        }

        return accepted.ToImmutable();
    }

    /// <summary>
    /// Charges the candidate checks a mask performs against contributions the prune removes.
    /// </summary>
    /// <param name="unmasked">Step 5's contribution overlays, before any mask is applied.</param>
    /// <returns>Whether the run may continue.</returns>
    /// <remarks>
    /// Section 8.6 discards a masked contribution "before literal-path merge validation", so by the
    /// time <see cref="Evaluate"/> sees the model the pairs a successful mask checked are gone from
    /// it. Charging only what survives makes the limit measure the mask's failures and not its
    /// work, which is the opposite of the Section 12.4 rule that "full capture matching may then
    /// succeed or fail without another candidate charge": the charge is levied on eligibility,
    /// before the outcome is known, so a mask that suppresses every item it checks is charged for
    /// every one of them.
    /// <para>
    /// Only masks are charged here. Section 8.6 says "suppressed paths and descendants never become
    /// wildcard candidates", so a generative rule is charged for none of them; a path that no mask
    /// suppresses is present in the evaluated model and is charged there. The
    /// <see cref="considered"/> set spans both passes, so Section 12.4's third condition holds
    /// across them and a surviving path charged here is not charged again.
    /// </para>
    /// <para>
    /// Contributions are enumerated rather than the merged model, because the merged model is the
    /// masked one. Merging is a union over paths, so the distinct depth-<c>k</c> prefixes of the
    /// contributions are the distinct depth-<c>k</c> prefixes the unmasked model would have had.
    /// </para>
    /// </remarks>
    public bool ChargeMaskedCandidates(IEnumerable<OverlayNode> unmasked)
    {
        ArgumentNullException.ThrowIfNull(unmasked);

        foreach (var contribution in unmasked)
        {
            for (var index = 0; index < rules.Length; index++)
            {
                if (rules[index].IsGenerative)
                {
                    continue;
                }

                foreach (var path in OverlayAddressing.Candidates(contribution, depths[index]))
                {
                    if (!Eligible(rules[index], depths[index], path))
                    {
                        continue;
                    }

                    if (!TryCharge(index, path, out _))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>Evaluates every rule against an exposed overlay until the fixed point is reached.</summary>
    /// <param name="root">Step 9's product, with the run's masks already applied.</param>
    /// <returns>The overlay including every generated contribution.</returns>
    public OverlayNode Evaluate(OverlayNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (rules.IsEmpty)
        {
            return root;
        }

        if (!budget.TryConsume(ResourceBound.MaxWildcardRules, rules.Length, out var tooManyRules))
        {
            ReportLimit(tooManyRules, rules);
            return root;
        }

        var current = root;

        // Section 12.4: "Evaluation continues until no new match pair or generated contribution
        // exists." A wave that grafts nothing settles both halves at once — the tree is unchanged,
        // so the next wave would enumerate the same candidates and find every pair already
        // considered — which is why the loop can stop on that one observation.
        for (var wave = 1L; ; wave++)
        {
            if (!TryRunWave(current, wave, out current, out var generated))
            {
                return current;
            }

            if (!generated)
            {
                return current;
            }
        }
    }

    private bool TryRunWave(OverlayNode start, long wave, out OverlayNode result, out bool generated)
    {
        // Section 12.4: one wave "evaluates eligible (rule,item) pairs against the items present at
        // the start of that wave", so candidates come from the snapshot while contributions land in
        // the running overlay. Enumerating the running overlay instead would let a rule see, within
        // one wave, an item another rule had just generated -- and which rule saw which item would
        // then depend on worklist position rather than on the wave the item appeared in.
        var snapshot = start;
        result = start;
        generated = false;

        for (var index = 0; index < rules.Length; index++)
        {
            var rule = rules[index];
            var depth = depths[index];

            foreach (var path in OverlayAddressing.Candidates(snapshot, depth))
            {
                if (!Eligible(rule, depth, path))
                {
                    continue;
                }

                // Section 8.6: "suppressed paths and descendants never become wildcard candidates".
                // A mask still checks them -- that check is how they came to be suppressed, and
                // ChargeMaskedCandidates is where it is paid for -- but a generative rule never
                // sees them, so it is not charged for them either.
                //
                // Removing this is not observable today: the mask is applied to the tree before
                // Evaluate runs, so no suppressed path reaches the snapshot. It is kept because it
                // states the rule at the point the rule is about. Do not write a test for it; the
                // whole suite is green with it deleted.
                if (rule.IsGenerative && mask.Suppresses(path))
                {
                    continue;
                }

                if (!TryCharge(index, path, out var fresh))
                {
                    return false;
                }

                if (!fresh
                    || !rule.IsGenerative
                    || !WildcardMatch.TryMatchPrefix(rule.Name.Parts, depth, path, out var captures))
                {
                    continue;
                }

                if (!TryGenerate(result, rule, index, depth, wave, path, captures, out result))
                {
                    return false;
                }

                generated = true;
            }
        }

        return true;
    }

    /// <summary>Levies Section 12.4's candidate charge for one eligible pair, once.</summary>
    /// <param name="index">The rule's position in the worklist.</param>
    /// <param name="path">The candidate item.</param>
    /// <param name="fresh">Whether Section 12.4's third condition held, so the pair was charged.</param>
    /// <returns>Whether the run may continue.</returns>
    private bool TryCharge(int index, ImmutableArray<NamePart> path, out bool fresh)
    {
        fresh = considered.Add((index, CanonicalPath.Of(path) ?? string.Empty));

        if (!fresh || budget.TryConsume(ResourceBound.MaxWildcardCandidates, 1, out var tooMany))
        {
            return true;
        }

        ReportLimit(tooMany, [rules[index]]);
        return false;
    }

    private bool TryGenerate(
        OverlayNode current,
        WildcardRule rule,
        int index,
        int depth,
        long wave,
        ImmutableArray<NamePart> path,
        WildcardCaptures captures,
        out OverlayNode result)
    {
        result = current;

        // Section 12.3: "Literal suffix parts are appended to generated names." The matched prefix
        // is taken from the concrete path rather than from the pattern, which is what makes the
        // capture text ordinary literal text in one name part under Section 12.2.
        var generated = path;

        for (var i = depth; i < rule.Name.Parts.Length; i++)
        {
            generated = generated.Add(rule.Name.Parts[i]);
        }

        if (mask.Suppresses(generated))
        {
            return true;
        }

        // Section 12.4: "Breadth-wave iteration counts apply only to generative templates." The
        // charge therefore falls here, on the first contribution a wave produces, rather than on
        // every pass of the loop: the pass that establishes the fixed point generates nothing, and
        // charging it would make --max-wildcard-iterations=1 unsatisfiable for every rule set that
        // matches anything at all. Section 23 asks for accounting "before allocation or expansion",
        // and this is before the graft.
        //
        // The chargedWave guard is an optimisation, not a semantic one: TryEnter compares a level
        // against a bound and holds no running total, so charging the same wave repeatedly decides
        // the same way. Mutating the guard away is therefore inert, and pinning it with a test
        // would assert an implementation detail rather than the limit.
        if (chargedWave != wave)
        {
            if (!budget.TryEnter(ResourceBound.MaxWildcardIterations, wave, out var tooManyWaves))
            {
                ReportLimit(tooManyWaves, Generating());
                return false;
            }

            chargedWave = wave;
        }

        // Section 23: "--max-generated counts newly materialized overlay nodes, including carrier
        // containers required for a generated descendant. A generated contribution targeting only
        // already-existing nodes consumes no generated-node count."
        var fresh = Materialized(current, generated);

        if (fresh > 0 && !budget.TryConsume(ResourceBound.MaxGenerated, fresh, out var tooMany))
        {
            ReportLimit(tooMany, [rule]);
            return false;
        }

        // Section 12.4: a generated contribution is "merged at its deterministic rule/match
        // position", and Section 12.5 makes "the later deterministic match ordinal" win. The rule's
        // own key supplies the first two Section 4.7 components, so a generated contribution loses
        // to every concrete contribution from a later source, as Section 12.4 requires: "Source
        // order controls precedence, not visibility."
        var order = rule.Order with { MatchOrdinal = ++matches[index] };
        var payload = ScalarPayload.Untyped(WildcardSubstitution.Apply(rule.Value!, captures));
        var leaf = rule.Comments.Aggregate(
            OverlayNode.OfPayload(payload, order),
            (node, comment) => node.WithComment(comment));

        result = Graft(current, generated, 0, leaf, order);
        return true;
    }

    /// <summary>
    /// Places one generated contribution, following the addressing Section 15.1 step 9 established.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walk descends into the existing tree rather than building a root-anchored contribution
    /// and merging it from the top, because Section 12.3 requires that "when a generated suffix
    /// targets a sequence item, it is deep-merged into that item's overlay node". A contribution
    /// grafted as an ordinary mapping child named <c>0</c> would instead give the parent a second,
    /// competing container facet, and the item the suffix was aimed at would never see it.
    /// </para>
    /// <para>
    /// Section 12.4 merges the contribution "using the effective input-path strategy of its target",
    /// so the strategy is consulted once, at the target path, by
    /// <see cref="OverlayMerger.MergeAt"/>. Nothing above the target is a contribution in its own
    /// right; those nodes are the carriers Section 23 counts.
    /// </para>
    /// <para>
    /// Where an address resolves through both facets at once -- the one structural node Section
    /// 15.1 makes of a numeric mapping child and the item at its ordering value -- only the facet
    /// that currently wins Section 4.4's container contest is refreshed. Refreshing both would give
    /// them equal marks, and an equal mark resolves to the mapping, so adding a descendant to an
    /// item would silently turn the sequence into a mapping.
    /// </para>
    /// <para>
    /// The descent is what deep merge means, so it continues only while deep merge is the effective
    /// strategy. Section 16.10 places a contribution "at path <c>P</c>" when it contributes "any
    /// descendant under <c>P</c>", which a generated entry does at every path it passes through, and
    /// the other three strategies act on the complete value at such a path rather than on the parts
    /// of it the entry happens to name. Where one of them is in force, the whole generated value
    /// from that depth down is handed to <see cref="OverlayMerger.MergeAt"/> and the descent stops.
    /// </para>
    /// <para>
    /// Which of the two is the later contribution is decided by Section 12.4 -- "the rule mark still
    /// controls conflict precedence" -- and not by the fact that generation runs after every
    /// concrete source has been read. A template from an earlier source that generates into a path
    /// a later source also wrote is the earlier contribution there, and passing it as the later one
    /// makes a later source lose.
    /// </para>
    /// </remarks>
    private OverlayNode Graft(
        OverlayNode node,
        ImmutableArray<NamePart> path,
        int depth,
        OverlayNode leaf,
        StableOrderingKey order)
    {
        if (depth == path.Length)
        {
            return Fold(node, leaf, path, order);
        }

        if (merger.StrategyAt(Prefix(path, depth)) != MergeStrategy.Deep)
        {
            return Fold(node, Spine(path, depth, leaf, order), Prefix(path, depth), order);
        }

        var part = path[depth];
        var hasChild = node.Children.TryGetValue(part, out var child);
        var value = 0L;
        SequenceItem? item = null;

        if (OrderingValues.TryRead(part, out var ordering)
            && node.Sequence.TryGetValue(ordering, out var found))
        {
            value = ordering;
            item = found;
        }

        if (!hasChild && item is null)
        {
            return node.WithChild(part, Spine(path, depth + 1, leaf, order));
        }

        var updated = Graft(hasChild ? child! : item!.Node, path, depth + 1, leaf, order);

        if (hasChild && item is null)
        {
            return node.WithChild(part, updated);
        }

        if (!hasChild)
        {
            return OverlayNode.Compose(
                node.Marks.WithSequenceItem(updated.Marks.Latest),
                node.Payload,
                node.HasExplicitMapping,
                node.HasExplicitSequence,
                node.Children,
                node.Sequence.SetItem(value, item! with { Node = updated }),
                node.Comments,
                node.SequenceHighWater);
        }

        return OverlayNode.Compose(
            node.Marks.ContainerIsSequence
                ? node.Marks.WithSequenceItem(updated.Marks.Latest)
                : node.Marks.WithDescendant(updated.Marks.Latest),
            node.Payload,
            node.HasExplicitMapping,
            node.HasExplicitSequence,
            node.Children.SetItem(part, updated),
            node.Sequence.SetItem(value, item! with { Node = updated }),
            node.Comments,
            node.SequenceHighWater);
    }

    /// <summary>The carrier chain a generated descendant needs below an address that does not exist.</summary>
    /// <param name="path">The generated path, from the overlay root.</param>
    /// <param name="depth">The depth the chain starts at.</param>
    /// <param name="leaf">The generated contribution itself.</param>
    /// <param name="order">The rule and match position the contribution merges at.</param>
    private static OverlayNode Spine(
        ImmutableArray<NamePart> path, int depth, OverlayNode leaf, StableOrderingKey order)
    {
        var node = leaf;

        for (var i = path.Length - 1; i >= depth; i--)
        {
            node = OverlayNode.Intermediate(order).WithChild(path[i], node);
        }

        return node;
    }

    /// <summary>
    /// Merges a generated value into what is already at a path, in Section 12.4 rule-mark order.
    /// </summary>
    /// <param name="existing">What the overlay already holds at the path.</param>
    /// <param name="generated">The generated value at the same path.</param>
    /// <param name="path">The shared path, from the overlay root.</param>
    /// <param name="order">The rule and match position the generated value merges at.</param>
    /// <remarks>
    /// Section 12.4 merges every generated result "at its deterministic rule/match position", and
    /// the position is the rule's, not the moment generation ran: "Every template must be matched
    /// against every eligible concrete or generated entry present in the current fixed-point
    /// evaluation, regardless of whether the matched entry originated before or after the template.
    /// Source order controls precedence, not visibility."
    /// </remarks>
    private OverlayNode Fold(
        OverlayNode existing,
        OverlayNode generated,
        ImmutableArray<NamePart> path,
        StableOrderingKey order) =>
        order.CompareTo(existing.Marks.Latest) < 0
            ? merger.MergeAt(generated, existing, path)
            : merger.MergeAt(existing, generated, path);

    /// <summary>The leading <paramref name="depth"/> components of a path.</summary>
    /// <param name="path">The path to take a prefix of.</param>
    /// <param name="depth">How many leading components the prefix has.</param>
    private static ImmutableArray<NamePart> Prefix(ImmutableArray<NamePart> path, int depth) =>
        depth == path.Length ? path : ImmutableArray.Create(path, 0, depth);

    /// <summary>How many nodes a generated contribution at a path would newly materialize.</summary>
    private static long Materialized(OverlayNode root, ImmutableArray<NamePart> path)
    {
        var node = root;
        var depth = 0;

        while (depth < path.Length
            && OrderingValueExposer.TryResolve(node, path[depth], out var next))
        {
            node = next;
            depth++;
        }

        return path.Length - depth;
    }

    /// <summary>
    /// Whether Section 12.4's first two candidate conditions hold, so the pair is charged at all.
    /// </summary>
    /// <remarks>
    /// The first condition is satisfied by construction: candidates are enumerated at exactly the
    /// depth the rule needs. The second — "every literal name part before that point equals the
    /// corresponding item part" — is what this tests. Parts that contain a wildcard are not literal
    /// and are settled by the match itself.
    /// </remarks>
    private static bool Eligible(WildcardRule rule, int depth, ImmutableArray<NamePart> path)
    {
        for (var i = 0; i < depth; i++)
        {
            var part = rule.Name.Parts[i];

            if (!WildcardMatch.HasWildcard(part) && !part.Equals(path[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The generative rules that have produced something, for a limit report.</summary>
    private IEnumerable<WildcardRule> Generating()
    {
        for (var index = 0; index < rules.Length; index++)
        {
            if (matches[index] > 0)
            {
                yield return rules[index];
            }
        }
    }

    private void ReportLimit(BudgetFault fault, IEnumerable<WildcardRule> responsible)
    {
        var blamed = responsible.ToList();

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Wildcard002(
                DiagnosticPhase.Input,
                "\u00A712.4",
                $"wildcard evaluation crosses {fault.Spelling}, whose limit is {fault.Limit}.",
                rule: [.. blamed.Select(rule => rule.CanonicalName)]),
            blamed.Count == 0 ? StableOrderingKey.First : blamed[0].Order));
    }

    private static void Reject(WildcardRule rule, string message, DiagnosticBuffer diagnostics) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Wildcard001(
                DiagnosticPhase.Input,
                "\u00A712.2",
                message,
                cardinalityKey: rule.RuleKey,
                source: rule.Source,
                line: rule.Source is null ? null : rule.Line,
                path: CanonicalPath.Of(rule.Name)),
            rule.Order));
}
