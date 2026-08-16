using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

/// <summary>
/// Where a merge is happening, which decides how its diagnostics are attributed.
/// </summary>
/// <param name="Phase">The Section 6.4.3 phase the merge runs in.</param>
/// <param name="Spec">The clause the strategy comes from.</param>
/// <param name="Directive">The directive that selected the strategy, as written.</param>
/// <remarks>
/// Section 15.1 step 8 and step 18 apply the same strategies to the same node shapes, so they share
/// this class; they do not share an anchor, a phase, or a directive name. Reporting a step 18
/// destination fold as a step 8 <c>merge</c> conflict under Section 16.10 would name a directive the
/// user did not write, in a phase that had already finished.
/// </remarks>
public sealed record MergeContext(DiagnosticPhase Phase, string Spec, string Directive)
{
    /// <summary>Section 15.1 step 8: literal-path input merging under Section 16.10.</summary>
    public static MergeContext Input { get; } =
        new(DiagnosticPhase.Input, "\u00A716.10", "merge");

    private static MergeContext DestinationFold { get; } =
        new(DiagnosticPhase.Planning, "\u00A717.5", "filemerge");

    /// <summary>
    /// What Section 22 counts this merge's diagnostics against, beyond the path, or
    /// <see langword="null"/> when the path alone is the whole identity.
    /// </summary>
    /// <remarks>
    /// Section 22 counts TYPE001 "once per path and applicable source/output instance". Step 8
    /// merges into the one common model, so a path names the occurrence completely and this is
    /// null. Step 18 runs one merge per destination, and every destination folds from its own view
    /// root, so a path does not name the occurrence at all: <c>filemerge=append</c> onto a mapping
    /// is refused at the root of every destination it is declared on, and Appendix A.2 gives the
    /// root no spelling. Keyed on the path alone, a run reports the first such destination and
    /// retires every other one as a repeat.
    /// </remarks>
    public string? Scope { get; init; }

    /// <summary>Section 15.1 step 18: destination folding under Sections 16.11 and 17.5.</summary>
    /// <param name="destination">The canonical relative path being folded.</param>
    /// <returns>The context for that destination's fold.</returns>
    public static MergeContext ForDestination(string destination) =>
        DestinationFold with { Scope = destination };

    /// <summary>
    /// Whether this merge runs after wildcards, references and selectors have finished.
    /// </summary>
    /// <remarks>
    /// Step 8 merges the one common model, and everything that reads the overlay by path — Section
    /// 12 wildcard candidacy, Section 13 reference resolution, Section 14 selector matching — runs
    /// after it. Step 18 folds fully transformed contribution models, and Section 17.5 says so:
    /// "file-level merge operates on fully transformed contribution models after <c>type</c>,
    /// <c>key</c>, and <c>root</c> have been applied". Nothing addresses a path by name after this
    /// point except rendering, which is what makes a node that carries only a Section 5.4 mark
    /// safe here and unsafe at step 8.
    /// </remarks>
    public bool IsDestinationFold => Phase == DiagnosticPhase.Planning;
}

/// <summary>
/// Pipeline step 8: merges source contributions one at a time in source order under the Section 17
/// literal-path merge rules.
/// </summary>
/// <remarks>
/// <para>
/// Merging is pairwise and left-to-right because Section 15.1 step 8 says so — "merge source
/// contributions one at a time in source order" — and because the alternative changes results. The
/// Section 5.4 high-water mark advances as each contribution is folded in, so a later contribution's
/// implicit items land above everything merged before them. Merging a set all at once, or in any
/// other order, would allocate different ordering values.
/// </para>
/// <para>
/// Each source contribution arrives with its own entries already folded, which the reader does when
/// it grafts them into one overlay.
/// </para>
/// </remarks>
public sealed class OverlayMerger
{
    private readonly MergeStrategyMap strategies;
    private readonly DiagnosticBuffer diagnostics;
    private readonly MergeStrategyMap? sourceCompatibility;
    private readonly MergeContext context;

    /// <summary>Creates a merger.</summary>
    /// <param name="strategies">The effective Section 16.10 strategy at each path.</param>
    /// <param name="diagnostics">The buffer merge diagnostics accumulate in.</param>
    /// <param name="sourceCompatibility">
    /// The declared <c>merge</c> directives, when this merger should report the Section 8.7
    /// compatibility warning, or <see langword="null"/> when it should not.
    /// </param>
    /// <remarks>
    /// The directives are supplied separately from <paramref name="strategies"/> because step 9
    /// reuses the merger with no strategies at all -- it folds a numeric mapping child into the
    /// sequence item Section 15.1 makes the same structural node, which is not a Section 16.10
    /// literal-path merge -- and yet that fold is exactly where two sources' native arrays can
    /// first meet at one path, one through each address. It still has to answer Section 8.7's
    /// question about whether a directive was declared, and the answer lives in the map it does
    /// not otherwise use.
    /// </remarks>
    /// <param name="context">
    /// Where the merge is happening, which decides the phase and anchor its diagnostics carry.
    /// Defaults to step 8's literal-path input merge.
    /// </param>
    public OverlayMerger(
        MergeStrategyMap strategies,
        DiagnosticBuffer diagnostics,
        MergeStrategyMap? sourceCompatibility = null,
        MergeContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.strategies = strategies;
        this.diagnostics = diagnostics;
        this.sourceCompatibility = sourceCompatibility;
        this.context = context ?? MergeContext.Input;
    }

    /// <summary>Merges contributions in CLI source order.</summary>
    /// <param name="contributions">The per-source overlays, in source order.</param>
    public OverlayNode MergeAll(IEnumerable<OverlayNode> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);

        OverlayNode? merged = null;

        foreach (var contribution in contributions)
        {
            merged = merged is null ? contribution : Merge(merged, contribution);
        }

        return merged ?? OverlayNode.Empty(NodeMarks.At(StableOrderingKey.First));
    }

    /// <summary>Merges one later contribution into everything merged so far.</summary>
    /// <param name="earlier">The contributions already merged, in source order.</param>
    /// <param name="later">The next contribution in source order.</param>
    public OverlayNode Merge(OverlayNode earlier, OverlayNode later)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentNullException.ThrowIfNull(later);

        return MergeNode(earlier, later, []);
    }

    /// <summary>
    /// Merges two contributions that share a path the root-relative pass could not see as one.
    /// </summary>
    /// <param name="earlier">The earlier contribution in source order.</param>
    /// <param name="later">The later contribution in source order.</param>
    /// <param name="path">The shared path, from the overlay root.</param>
    /// <remarks>
    /// Step 9 needs this because a sequence item and a numeric mapping child only become one
    /// literal path once ordering values are exposed. The path has to be supplied rather than
    /// rediscovered: the Section 16.10 strategy is an exact per-path lookup, so merging such a pair
    /// as if it sat at the root would silently apply the wrong strategy and report the wrong path.
    /// </remarks>
    internal OverlayNode MergeAt(
        OverlayNode earlier, OverlayNode later, ImmutableArray<NamePart> path)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentNullException.ThrowIfNull(later);

        return MergeNode(earlier, later, path);
    }

    /// <summary>The effective Section 16.10 strategy at one path.</summary>
    /// <param name="path">The path, from the overlay root.</param>
    /// <remarks>
    /// Section 12.4 merges a generated contribution "using the effective input-path strategy of its
    /// target", and Section 16.10 puts a contribution "at path <c>P</c>" when it contributes "any
    /// descendant under <c>P</c>". A generated entry is therefore a contribution at every path it
    /// passes through, and the caller placing it has to know where a strategy other than deep
    /// merge takes over from ordinary descent.
    /// </remarks>
    internal MergeStrategy StrategyAt(ImmutableArray<NamePart> path) => strategies.For(path);

    private OverlayNode MergeNode(
        OverlayNode earlier, OverlayNode later, ImmutableArray<NamePart> path)
    {
        var strategy = strategies.For(path);

        if (strategy == MergeStrategy.Error)
        {
            // Section 16.10: "any distinct second source or generated contribution at the path is
            // an error". Section 15.4 keeps going after a blocking error so that one run reports
            // every problem, so the merge still produces a value; nothing will be published.
            if (IsContributionAt(later))
            {
                Report(
                    path,
                    $"{context.Directive}=error rejects a second contribution at this path.",
                    later.Marks.Latest);
            }

            return DeepMerge(earlier, later, path);
        }

        return strategy switch
        {
            MergeStrategy.Deep => DeepMerge(earlier, later, path),
            MergeStrategy.Replace => ReplaceMerge(earlier, later, path),
            MergeStrategy.Append => AppendMerge(earlier, later, path),
            _ => throw new InvalidOperationException($"'{strategy}' is not a {nameof(MergeStrategy)}."),
        };
    }

    /// <summary>
    /// Section 16.10: "A contribution is <b>at path P</b> when it contributes a payload, explicit
    /// container presence, sequence projection, or any descendant under P."
    /// </summary>
    private static bool IsContributionAt(OverlayNode node) => !node.IsEmpty;

    private OverlayNode DeepMerge(
        OverlayNode earlier, OverlayNode later, ImmutableArray<NamePart> path)
    {
        var children = earlier.Children;

        foreach (var (name, child) in later.Children)
        {
            if (children.TryGetValue(name, out var existing))
            {
                children = children.SetItem(name, MergeNode(existing, child, path.Add(name)));
                continue;
            }

            ReportAliasedComponent(path, name, earlier.Children, child.Marks.Latest);
            children = children.SetItem(name, child);
        }

        var (sequence, highWater) = MergeSequence(
            earlier.Sequence, earlier.SequenceHighWater, later, path);

        return OverlayNode.Compose(
            earlier.Marks.Combine(later.Marks),
            // Section 17.1: "later payload wins", judged on the Section 4.4 payload mark rather
            // than on which node merged second, because a node can carry a payload from a source
            // that is earlier than the other node's mapping contribution.
            LaterPayload(earlier, later),
            earlier.HasExplicitMapping || later.HasExplicitMapping,
            earlier.HasExplicitSequence || later.HasExplicitSequence,
            children,
            sequence,
            // Section 17.1: comments "accumulate and survive merge whenever their logical path
            // survives".
            earlier.Comments.AddRange(later.Comments),
            Math.Max(highWater, ReservedByChildren(later.Children)));
    }

    /// <summary>
    /// Section 16.10 <c>replace</c>: "the later complete value replaces the earlier value".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 17.2 makes the high-water mark the one thing replacement does not undo: it "removes
    /// the earlier visible sequence projection but does not lower the path's allocation high-water
    /// mark", so later automatic allocation never reuses a removed value. The earlier node's own
    /// comments survive because Section 17.1 keeps comments whenever their logical path survives,
    /// and this path does; comments on descendants the replacement removes go with them.
    /// </para>
    /// <para>
    /// The position mark survives the replacement even though nothing else about the earlier value
    /// does. Section 5.2 governs where a key sits in mapping order and is not a merge strategy:
    /// <c>replace</c> decides what the node contains, and an intermediate node that exists only
    /// because something deeper needed it still keeps the earliest position that required it. Every
    /// other mark describes part of the value Section 16.10 has just removed, so
    /// <see cref="NodeMarks.AfterReplacement"/> takes those from the replacement.
    /// </para>
    /// <para>
    /// Descendant high-water marks survive too, through <c>CarryMarks</c>. Section 5.4 gives
    /// "each sequence path" a mark recording the greatest value ever supplied there "including
    /// values later removed or replaced", and Section 17.5 requires an output contribution to carry
    /// "its complete per-path high-water map, including marks raised by items hidden by output
    /// projection". Keeping only this node's mark satisfies neither: it is the mark at the path
    /// <c>replace</c> was declared on, and the sequences are usually beneath it.
    /// </para>
    /// <para>
    /// Retaining the mark is only half of it. Section 17.5 has the accumulator "absorb the incoming
    /// high-water mark for a path before allocating or patching incoming items at that path", and
    /// rebases "an implicit item from a later output contribution onto the next fresh destination
    /// ordering value" whatever the strategy — the strategy-specific bullet beside it says only that
    /// <c>replace</c> discards the accumulated projection. So the replacement's own implicit items
    /// allocate above the retained mark rather than restarting at zero, which is the only thing that
    /// makes retaining the mark observable: a fresh dense index (Section 5.4) hides the values, and
    /// only a third contribution addressing one of them can tell the two apart.
    /// </para>
    /// </remarks>
    private OverlayNode ReplaceMerge(
        OverlayNode earlier, OverlayNode later, ImmutableArray<NamePart> path)
    {
        // The empty projection is what Section 17.2 removes; the mark it is allocated against is
        // what Section 17.2 declines to lower.
        var (sequence, highWater) = MergeSequence(
            ImmutableDictionary<long, SequenceItem>.Empty,
            Math.Max(earlier.SequenceHighWater, later.SequenceHighWater),
            later,
            path);

        return OverlayNode.Compose(
            earlier.Marks.AfterReplacement(later.Marks),
            later.Payload,
            later.HasExplicitMapping,
            later.HasExplicitSequence,
            CarryMarks(earlier.Children, later.Children, path),
            sequence,
            earlier.Comments.AddRange(later.Comments),
            Math.Max(highWater, ReservedByChildren(later.Children)));
    }

    /// <summary>
    /// Carries the replaced children's high-water marks onto the replacement's, without carrying
    /// any of the value the replacement removed.
    /// </summary>
    /// <param name="earlier">The replaced children.</param>
    /// <param name="later">The replacement's children.</param>
    /// <param name="path">The path both sets of children hang from.</param>
    /// <returns>The replacement's children, each raised to the mark the replaced path had reached.</returns>
    /// <remarks>
    /// <para>
    /// A child the replacement also names is replaced by the same rules, recursively, which is what
    /// carries the absorb-then-allocate order of Section 17.5 down to the sequence paths beneath the
    /// declaration. <see cref="MergeNode"/> cannot do this: <c>replace</c> resolves at the path it is
    /// declared on and never asks the strategy map about a descendant.
    /// </para>
    /// <para>
    /// At step 8 a child the replacement does not name keeps nothing, not even its mark. Section
    /// 17.5 asks for the "complete per-path high-water map", which is a map and not part of the
    /// tree; materialising an empty node to hold it there would put a path Section 16.10 has just
    /// removed back into <see cref="OverlayNode.Children"/>, where wildcards, references and
    /// selectors would all find it again.
    /// </para>
    /// <para>
    /// At step 18 that objection does not apply, and the mark is kept. Section 17.5 folds "fully
    /// transformed contribution models after <c>type</c>, <c>key</c>, and <c>root</c> have been
    /// applied", so every stage that addresses a path by name has already run and rendering is all
    /// that remains. The retained node carries marks and nothing else — no payload, no shape, no
    /// comments, no items, and a position that loses every Section 5.2 comparison so a recreated
    /// path takes the recreating contribution's place rather than the discarded one's. It exists
    /// only until the fold for its destination finishes, at which point
    /// <see cref="StripHighWaterCarriers"/> removes whatever no contribution has recreated.
    /// </para>
    /// </remarks>
    private ImmutableDictionary<NamePart, OverlayNode> CarryMarks(
        ImmutableDictionary<NamePart, OverlayNode> earlier,
        ImmutableDictionary<NamePart, OverlayNode> later,
        ImmutableArray<NamePart> path)
    {
        if (earlier.IsEmpty)
        {
            return later;
        }

        if (later.IsEmpty && !context.IsDestinationFold)
        {
            return later;
        }

        var carried = later;

        foreach (var (name, node) in earlier)
        {
            if (later.TryGetValue(name, out var replacement))
            {
                carried = carried.SetItem(name, ReplaceMerge(node, replacement, path.Add(name)));
                continue;
            }

            if (context.IsDestinationFold && MarksOnly(node) is { } retained)
            {
                carried = carried.SetItem(name, retained);
            }
        }

        return carried;
    }

    /// <summary>
    /// The Section 5.4 high-water marks of a subtree the replacement removed, with none of the
    /// value.
    /// </summary>
    /// <param name="node">The removed subtree.</param>
    /// <returns>
    /// A node carrying only marks, or <see langword="null"/> when the subtree raised none and there
    /// is nothing to keep.
    /// </returns>
    private static OverlayNode? MarksOnly(OverlayNode node)
    {
        var children = ImmutableDictionary<NamePart, OverlayNode>.Empty;

        foreach (var (name, child) in node.Children)
        {
            if (MarksOnly(child) is { } retained)
            {
                children = children.SetItem(name, retained);
            }
        }

        foreach (var (value, item) in node.Sequence)
        {
            if (MarksOnly(item.Node) is { } retained)
            {
                children = children.SetItem(OrderingValues.ToNamePart(value), retained);
            }
        }

        if (children.IsEmpty && node.SequenceHighWater == SequenceOrderingAllocator.InitialHighWaterMark)
        {
            return null;
        }

        return OverlayNode.Compose(
            NodeMarks.At(StableOrderingKey.Last),
            payload: null,
            hasExplicitMapping: false,
            hasExplicitSequence: false,
            children,
            ImmutableDictionary<long, SequenceItem>.Empty,
            ImmutableList<BoundComment>.Empty,
            node.SequenceHighWater);
    }

    /// <summary>
    /// Removes the Section 5.4 mark carriers a destination fold left behind, once the fold that
    /// needed them has finished.
    /// </summary>
    /// <param name="node">The folded plan for one destination.</param>
    /// <returns>The same plan with every unrecreated carrier removed.</returns>
    /// <remarks>
    /// <para>
    /// A carrier holds a high-water mark for a path <c>filemerge=replace</c> removed, so that a
    /// later file in the same fold recreating that path allocates above the removed items rather
    /// than reusing their ordering values. Once every contribution to the destination has been
    /// folded nothing allocates again — Section 5.4 numbering is dense at render time, from the
    /// surviving items — so a carrier no contribution recreated is state with no remaining reader.
    /// Left in place it renders: an exclusive destination emits it as an empty mapping, which is a
    /// path the replacement removed reappearing in the output.
    /// </para>
    /// <para>
    /// The test is the conjunction of the two facts that define a carrier, not either alone.
    /// <see cref="StableOrderingKey.Last"/> is the position no contribution can hold, and Section
    /// 5.2 resolves it away the moment one lands on the path; structural emptiness alone would also
    /// describe a node some future contribution shape could produce legitimately.
    /// </para>
    /// </remarks>
    public static OverlayNode StripHighWaterCarriers(OverlayNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var children = node.Children;

        foreach (var (name, child) in node.Children)
        {
            var stripped = StripHighWaterCarriers(child);

            children = IsHighWaterCarrier(stripped)
                ? children.Remove(name)
                : children.SetItem(name, stripped);
        }

        var sequence = node.Sequence;

        foreach (var (value, item) in node.Sequence)
        {
            var stripped = StripHighWaterCarriers(item.Node);

            sequence = IsHighWaterCarrier(stripped)
                ? sequence.Remove(value)
                : sequence.SetItem(value, item with { Node = stripped });
        }

        return ReferenceEquals(children, node.Children) && ReferenceEquals(sequence, node.Sequence)
            ? node
            : OverlayNode.Compose(
                node.Marks,
                node.Payload,
                node.HasExplicitMapping,
                node.HasExplicitSequence,
                children,
                sequence,
                node.Comments,
                node.SequenceHighWater);
    }

    private static bool IsHighWaterCarrier(OverlayNode node) =>
        node.Marks.Position == StableOrderingKey.Last
        && node.Payload is null
        && !node.HasExplicitMapping
        && !node.HasExplicitSequence
        && node.Comments.IsEmpty
        && node.Children.IsEmpty
        && node.Sequence.IsEmpty;

    /// <summary>
    /// Section 16.10 <c>append</c>: "every item in the later sequence contribution, including
    /// explicitly indexed items, is rebased in ascending original ordering value onto fresh implicit
    /// ordering values above the current high-water mark".
    /// </summary>
    private OverlayNode AppendMerge(
        OverlayNode earlier, OverlayNode later, ImmutableArray<NamePart> path)
    {
        if (!TryReadSequenceContribution(later, out var items))
        {
            // Section 16.10: "other non-sequence use is an error."
            Report(
                path,
                $"{context.Directive}=append needs a sequence contribution: this one is neither a "
                + "sequence nor a nonempty mapping whose child names are all canonical ordering "
                + "values.",
                later.Marks.Latest);

            return DeepMerge(earlier, later, path);
        }

        // Section 15.1 step 8 rebases "when a strictly earlier surviving sequence-eligible
        // contribution exists"; otherwise "the earliest or sole contribution retains its supplied
        // ordering values". Rebasing regardless would shift the first sequence at a path off the
        // values its author wrote, which Section 5.4 forbids in the same words.
        //
        // "The earliest or sole contribution" is the case where nothing is at the path yet, not the
        // case where something is there and is not a sequence. Section 16.10 closes that second
        // case in the same sentence that opens the first: "other non-sequence use is an error".
        // Checking only the later side let append run over a scalar or an ordinary mapping and
        // silently degrade to a deep merge, so a run that asked to append to something unappendable
        // succeeded and published a scalar and a sequence coexisting at one path.
        if (!TryReadSequenceContribution(earlier, out var seeded))
        {
            if (IsContributionAt(earlier))
            {
                Report(
                    path,
                    $"{context.Directive}=append has nothing to append to here: the earlier "
                    + "contribution at this path is neither a sequence nor a nonempty mapping "
                    + "whose child names are all canonical ordering values.",
                    earlier.Marks.Latest);
            }

            return DeepMerge(earlier, later, path);
        }

        var allocator = SequenceOrderingAllocator.From(earlier.SequenceHighWater);

        // The earlier side is read through the same route as the later one. A contribution that is
        // sequence-eligible as "a nonempty all-in-range-canonical-numeric mapping" carries its items
        // in its children, not in its sequence, so seeding from the sequence alone starts the fold
        // from nothing and every item the earlier side supplied is absent from the result. Section
        // 16.10 appends "every item in the later sequence contribution" to what is already there,
        // and what is already there is whatever the earlier contribution holds however it spelled
        // it. Section 15.1 makes a numeric mapping child and the item at its ordering value one
        // structural node, so placing them in the sequence beside the retained children restates
        // that identity rather than duplicating anything.
        var sequence = seeded;

        // Section 5.4: "process items in ascending original ordering value. For each item, first
        // raise the current high-water mark to at least its supplied value, then allocate its new
        // value as high-water + 1." Any other order gives the items different values, because each
        // raise affects every allocation after it.
        //
        // The rebased item is implicit whatever it was before: Section 16.10 rebases "onto fresh
        // implicit ordering values", and Section 5.4 adds that "the original value is no longer
        // addressable for that rebased item". Keeping the explicit provenance would advertise a
        // supplied value the item no longer has.
        foreach (var (supplied, item) in items.OrderBy(entry => entry.Key))
        {
            if (!allocator.TryRebase(supplied, out var rebased))
            {
                ReportOverflow(path, item.Node.Marks.Latest);
                break;
            }

            sequence = sequence.SetItem(rebased, SequenceItem.Native(item.Node));
        }

        return OverlayNode.Compose(
            AppendedMarks(earlier.Marks, later.Marks),
            LaterPayload(earlier, later),
            earlier.HasExplicitMapping,
            earlier.HasExplicitSequence || later.HasExplicitSequence,
            earlier.Children,
            sequence,
            earlier.Comments.AddRange(later.Comments),
            allocator.HighWaterMark);
    }

    /// <summary>
    /// The marks of an <c>append</c> result, in which the later contribution's container is a
    /// sequence contribution however it was spelled.
    /// </summary>
    /// <remarks>
    /// Section 15.1 step 8: <c>append</c> "consumes and rebases the later mapping as a sequence
    /// contribution and leaves no mapping projection for later inference". Carrying the later
    /// node's mapping shape-mark across would leave a mapping projection with no children behind
    /// it, and Section 4.4 would then let that phantom win the exclusive-shape contest against the
    /// sequence the items actually landed in. The items are descendants, so they refresh shape
    /// without moving the node.
    /// </remarks>
    private static NodeMarks AppendedMarks(NodeMarks earlier, NodeMarks later)
    {
        var marks = later.PayloadMark is { } payloadMark
            ? earlier.WithPayload(payloadMark)
            : earlier;

        return marks.WithSequenceItem(later.ContainerShape ?? later.Position);
    }

    /// <summary>
    /// Reads a contribution as the sequence Section 16.10 <c>append</c> rebases.
    /// </summary>
    /// <remarks>
    /// Section 15.1 step 8 makes "a nonempty all-in-range-canonical-numeric mapping" sequence-
    /// eligible here and says <c>append</c> "consumes and rebases the later mapping as a sequence
    /// contribution and leaves no mapping projection for later inference", which is why the merged
    /// node keeps only the earlier node's children.
    /// <para>
    /// The mapping route requires a nonempty mapping, but an explicit sequence does not have to
    /// carry an item. Section 16.10 rebases "every item in the later sequence contribution" and
    /// makes an error of "other non-sequence use"; an empty native sequence is a sequence
    /// contribution with nothing to rebase, so it appends nothing and is not an error. Judging it
    /// by its items alone rejects the one shape the strategy is named after.
    /// </para>
    /// </remarks>
    private static bool TryReadSequenceContribution(
        OverlayNode node, out ImmutableDictionary<long, SequenceItem> items)
    {
        if (node.HasExplicitSequence || !node.Sequence.IsEmpty)
        {
            items = node.Sequence;
            return true;
        }

        var builder = ImmutableDictionary.CreateBuilder<long, SequenceItem>();

        foreach (var (name, child) in node.Children)
        {
            if (!OrderingValues.TryRead(name, out var value))
            {
                items = ImmutableDictionary<long, SequenceItem>.Empty;
                return false;
            }

            builder[value] = SequenceItem.Numbered(child);
        }

        items = builder.ToImmutable();
        return !items.IsEmpty;
    }

    /// <summary>
    /// Folds a later node's sequence contribution into an earlier sequence under Section 17.1.
    /// </summary>
    /// <remarks>
    /// The later node's own high-water mark is deliberately not consulted. Section 5.4 measures
    /// implicit values from the mark at the path, and the later contribution allocated its own
    /// implicit values from a mark that began at -1, so its numbers mean nothing in the merged
    /// path's coordinates. Its <b>explicit</b> values do mean something there, and they are
    /// supplied to the allocator one at a time as they are placed.
    /// </remarks>
    private (ImmutableDictionary<long, SequenceItem> Sequence, long HighWater) MergeSequence(
        ImmutableDictionary<long, SequenceItem> earlier,
        long highWater,
        OverlayNode later,
        ImmutableArray<NamePart> path)
    {
        if (later.Sequence.IsEmpty)
        {
            return (earlier, highWater);
        }

        var allocator = SequenceOrderingAllocator.From(highWater);
        var sequence = earlier;

        // Section 8.7: "When multiple sources contribute native implicit sequences at one path and
        // no explicit merge directive applies, emit one compatibility warning explaining that
        // implicit items concatenate while explicit ordering values patch." Only the earlier side
        // is tested for implicit items: every item a reader produces is implicit, so a later
        // contribution reaching here at all is a native sequence, and an explicit index is a
        // numeric mapping child that becomes an item only at step 9.
        if (sourceCompatibility is { } declared
            && !declared.Declares(path)
            && earlier.Values.Any(item => item.Provenance == OrderingProvenance.Implicit))
        {
            ReportImplicitConcatenation(path, later.Marks.Latest);
        }

        foreach (var (value, item) in later.Sequence.OrderBy(entry => entry.Key))
        {
            if (item.Provenance == OrderingProvenance.Explicit)
            {
                allocator.Supply(value);

                // Section 17.1: an explicit later ordering value "addresses the item already at
                // that value and the two items are then combined by the rules of this section,
                // recursively. Provenance decides which item the later contribution meets, not how
                // the two combine; a later contribution therefore never removes a sibling key it
                // does not name." Replacing the item outright would make a.0.port=2 delete an
                // a.0.name it never mentioned, which no other addressing form in this
                // specification does.
                //
                // The earlier item's provenance survives, because provenance records how this
                // slot's own value came to be and not what later addressed it. Adopting the later
                // item's Explicit here would silently retire the Section 8.7 concatenation warning
                // for every subsequent native contribution at the path.
                sequence = sequence.SetItem(
                    value,
                    sequence.TryGetValue(value, out var existing)
                        ? existing with
                        {
                            Node = MergeNode(
                                existing.Node,
                                item.Node,
                                path.Add(OrderingValues.ToNamePart(value))),
                        }
                        : item);
                continue;
            }

            // Section 17.1: "implicit later items concatenate".
            if (!allocator.TryAllocate(out var allocated))
            {
                ReportOverflow(path, item.Node.Marks.Latest);
                break;
            }

            sequence = sequence.SetItem(allocated, item);
        }

        return (sequence, allocator.HighWaterMark);
    }

    /// <summary>
    /// The Section 5.4 reservation every canonically numeric mapping child makes at its own path,
    /// "whether or not its containing mapping ultimately qualifies for sequence inference".
    /// </summary>
    /// <remarks>
    /// Only the newly arriving names need scanning: the earlier node's own numeric children raised
    /// its high-water mark when they were added, and Section 5.4 never lowers that mark.
    /// </remarks>
    private static long ReservedByChildren(
        ImmutableDictionary<NamePart, OverlayNode> children)
    {
        var reserved = SequenceOrderingAllocator.InitialHighWaterMark;

        foreach (var name in children.Keys)
        {
            if (OrderingValues.TryRead(name, out var value) && value > reserved)
            {
                reserved = value;
            }
        }

        return reserved;
    }

    private static ScalarPayload? LaterPayload(OverlayNode earlier, OverlayNode later) =>
        (earlier.Marks.PayloadMark, later.Marks.PayloadMark) switch
        {
            (null, _) => later.Payload,
            (_, null) => earlier.Payload,
            ({ } left, { } right) => right > left ? later.Payload : earlier.Payload,
        };

    /// <summary>The Section 22 cardinality key of one merge diagnostic.</summary>
    /// <param name="path">The Appendix A spelling of the path, or null at the root.</param>
    /// <returns>A key that separates exactly the occurrences Section 22 counts separately.</returns>
    /// <remarks>
    /// The two parts are length-prefixed rather than joined by a separator. A canonical path escapes
    /// only the delimiter, <c>=</c>, <c>}</c>, <c>*</c> and line terminators, and a destination is a
    /// relative file path, so any separator can occur inside either; a cardinality key is a
    /// suppression rule, and two keys that collide cost a diagnostic.
    /// </remarks>
    private string CardinalityKey(string? path) =>
        string.Concat(
            new[] { context.Scope, path }.Select(part => $"{part?.Length ?? -1}\u0000{part}"));

    private void Report(
        ImmutableArray<NamePart> path, string message, StableOrderingKey key)
    {
        var text = PathText(path);

        // Appendix B gives TYPE001 a 'destination' member, and a step 18 fold is refused at the
        // destination's view root, where Appendix A.2 leaves 'path' absent. Without it two
        // destinations refusing the same fold are byte-identical, and the run names neither of the
        // files it failed to produce. Step 8 has no destination and omits the member.
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Type001(
                context.Phase,
                context.Spec,
                message,
                cardinalityKey: CardinalityKey(text),
                path: text,
                destination: context.Scope),
            key));
    }

    /// <summary>
    /// Section 11.4 <c>WARN011</c>: a later unmarked component that is the simple alias of an XML
    /// component already present at the node "adds a second, ordinary component; it does not
    /// override the existing one".
    /// </summary>
    /// <param name="path">The node's path, from the overlay root.</param>
    /// <param name="name">The component the later contribution is adding.</param>
    /// <param name="existing">The children already merged at the node.</param>
    /// <param name="key">The added component's ordering key.</param>
    /// <remarks>
    /// <para>
    /// Only step 8 reports it. A step 18 destination fold merges two views that both already
    /// passed through here, so the pair it sees is the pair this run has reported once already.
    /// Dropping the phase test leaves the whole corpus green, because the fold's second report
    /// carries the same code and the same canonical path and the once-per-path cardinality
    /// discards it; the test states the scoping rather than defending an observed defect.
    /// </para>
    /// <para>
    /// A <c>Q{}x</c> component is excluded because Section 11.4 has it "bypass that index and name
    /// one canonical component outright" — the contribution has said which of the two it means,
    /// and it means the element. The name is reached only on the branch where no child of that
    /// exact name exists, which is also why an ordinary component has no alias of its own here:
    /// an ordinary component aliases to itself, and a self-match would be the merge branch.
    /// </para>
    /// </remarks>
    private void ReportAliasedComponent(
        ImmutableArray<NamePart> path,
        NamePart name,
        ImmutableDictionary<NamePart, OverlayNode> existing,
        StableOrderingKey key)
    {
        if (context.Phase != DiagnosticPhase.Input
            || name is not OrdinaryPart { IsExplicitlyCanonical: false } ordinary)
        {
            return;
        }

        NamePart? canonical = null;

        foreach (var candidate in existing.Keys)
        {
            // More than one XML component can alias to one name, so the report names the
            // Section 24 smallest rather than whichever the hash order offered first.
            if (SimpleAliasOf(candidate) == ordinary
                && (canonical is null || NamePartOrder.Instance.Compare(candidate, canonical) < 0))
            {
                canonical = candidate;
            }
        }

        if (canonical is null)
        {
            return;
        }

        var added = PathText(path.Add(name));
        var overridden = PathText(path.Add(canonical));

        // The rival decides the clause: Section 11.4 admits an attribute and an element in a
        // namespace as separate simple-alias competitors, and saying "an attribute" for a
        // namespaced element names a component the run does not contain.
        var rival = canonical is AttributePart
            ? "an attribute and an element of the same name"
            : "an element in a namespace and an unmarked component of the same local name";

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Warn011(
                DiagnosticPhase.Input,
                "\u00A711.4",
                $"'{added}' adds an ordinary component beside '{overridden}', which already "
                + $"exists here. Section 11.4 makes {rival} "
                + "different components, so this contribution does not override that one: write "
                + $"'{overridden}' to override it.",
                cardinalityKey: CardinalityKey(added),
                path: added),
            key));
    }

    /// <summary>
    /// The Section 13.1 simple alias of one XML component, or <see langword="null"/> when the
    /// component is not one that aliases to a different name.
    /// </summary>
    /// <param name="part">The canonical component.</param>
    /// <returns>The ordinary component it aliases to, or null.</returns>
    /// <remarks>
    /// Section 13.1 "replaces every <c>Q{uri}local</c> or <c>@Q{uri}local</c> part with
    /// <c>local</c>" and "replaces every <c>@local</c> part with <c>local</c>". A content token is
    /// excluded: Section 13.1 removes the part rather than renaming it, so it aliases to its
    /// owning element's path and never competes for a name at this node.
    /// </remarks>
    private static OrdinaryPart? SimpleAliasOf(NamePart part) => part switch
    {
        AttributePart attribute => Unqualified(attribute.Name),
        QualifiedElementPart qualified => new OrdinaryPart(qualified.Local),
        _ => null,
    };

    /// <summary>The ordinary spelling of an XML name component.</summary>
    /// <param name="component">The component.</param>
    /// <returns>The component itself when it is already ordinary, otherwise its local name.</returns>
    private static OrdinaryPart Unqualified(XmlNameComponent component) => component switch
    {
        QualifiedElementPart qualified => new OrdinaryPart(qualified.Local),
        OrdinaryPart ordinary => ordinary,
        _ => throw new InvalidOperationException(
            $"'{component}' is not an Appendix A.2 xml-name-component."),
    };

    private void ReportImplicitConcatenation(
        ImmutableArray<NamePart> path, StableOrderingKey key)
    {
        var text = PathText(path);

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Warn004(
                DiagnosticPhase.Input,
                "\u00A78.7",
                "more than one source contributes a native implicit sequence here, and implicit "
                + "items concatenate while explicit ordering values patch: declare a merge "
                + "strategy at this path to say which was meant.",
                cardinalityKey: CardinalityKey(text),
                path: text),
            key));
    }

    private void ReportOverflow(ImmutableArray<NamePart> path, StableOrderingKey key) => diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Limit001(
                DiagnosticPhase.Input,
                "\u00A75.4",
                "this sequence has no ordering value left: Section 5.4 allocates above the "
                + "greatest value ever used at the path, and that is already the maximum.",
                path: PathText(path)),
            key));

    /// <summary>
    /// The Appendix A spelling of a path, for the diagnostic's <c>path</c> field, or
    /// <see langword="null"/> at the overlay root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Appendix A.2 spells a name as one or more components, so the root has no spelling and the
    /// field is absent rather than empty. A sequence at the root is reachable, so this is not a
    /// theoretical case.
    /// </para>
    /// <para>
    /// A name whose <c>Q{...}</c> URI carries a scalar Section 19.1 forbids has no ordinary
    /// spelling, so encoding can fail. <see cref="CanonicalPath"/>'s approximation is used then: it
    /// writes those scalars as <c>\u{HEX}</c>, which does not read back but is total and injective,
    /// so the path is still distinguished from every other. That is what the cardinality key needs
    /// — a fallback that collapsed two paths onto one text would put them in one cardinality slot
    /// and cost the second diagnostic entirely.
    /// </para>
    /// </remarks>
    private static string? PathText(ImmutableArray<NamePart> path) => CanonicalPath.Of(path);
}
