using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

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

    /// <summary>Creates a merger.</summary>
    /// <param name="strategies">The effective Section 16.10 strategy at each path.</param>
    /// <param name="diagnostics">The buffer merge diagnostics accumulate in.</param>
    public OverlayMerger(MergeStrategyMap strategies, DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.strategies = strategies;
        this.diagnostics = diagnostics;
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
                    "merge=error rejects a second source contribution at this path.",
                    "\u00A716.10");
            }

            return DeepMerge(earlier, later, path);
        }

        return strategy switch
        {
            MergeStrategy.Deep => DeepMerge(earlier, later, path),
            MergeStrategy.Replace => ReplaceMerge(earlier, later),
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
            children = children.SetItem(
                name,
                children.TryGetValue(name, out var existing)
                    ? MergeNode(existing, child, path.Add(name))
                    : child);
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
    /// Section 17.2 makes the high-water mark the one thing replacement does not undo: it "removes
    /// the earlier visible sequence projection but does not lower the path's allocation high-water
    /// mark", so later automatic allocation never reuses a removed value. The earlier node's own
    /// comments survive because Section 17.1 keeps comments whenever their logical path survives,
    /// and this path does; comments on descendants the replacement removes go with them.
    /// </remarks>
    private static OverlayNode ReplaceMerge(OverlayNode earlier, OverlayNode later) =>
        OverlayNode.Compose(
            earlier.Marks.Combine(later.Marks),
            later.Payload,
            later.HasExplicitMapping,
            later.Children,
            later.Sequence,
            earlier.Comments.AddRange(later.Comments),
            Math.Max(earlier.SequenceHighWater, later.SequenceHighWater));

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
                "merge=append needs a sequence contribution: this one is neither a sequence nor a "
                + "nonempty mapping whose child names are all canonical ordering values.",
                "\u00A716.10");

            return DeepMerge(earlier, later, path);
        }

        // Section 15.1 step 8 rebases "when a strictly earlier surviving sequence-eligible
        // contribution exists"; otherwise "the earliest or sole contribution retains its supplied
        // ordering values". Rebasing regardless would shift the first sequence at a path off the
        // values its author wrote, which Section 5.4 forbids in the same words.
        if (!TryReadSequenceContribution(earlier, out _))
        {
            return DeepMerge(earlier, later, path);
        }

        var allocator = SequenceOrderingAllocator.From(earlier.SequenceHighWater);
        var sequence = earlier.Sequence;

        // Section 5.4: "process items in ascending original ordering value. For each item, first
        // raise the current high-water mark to at least its supplied value, then allocate its new
        // value as high-water + 1." Any other order gives the items different values, because each
        // raise affects every allocation after it.
        foreach (var (supplied, item) in items.OrderBy(entry => entry.Key))
        {
            if (!allocator.TryRebase(supplied, out var rebased))
            {
                ReportOverflow(path);
                break;
            }

            sequence = sequence.SetItem(rebased, item);
        }

        return OverlayNode.Compose(
            AppendedMarks(earlier.Marks, later.Marks),
            LaterPayload(earlier, later),
            earlier.HasExplicitMapping,
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
    /// </remarks>
    private static bool TryReadSequenceContribution(
        OverlayNode node, out ImmutableDictionary<long, SequenceItem> items)
    {
        if (!node.Sequence.IsEmpty)
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

        foreach (var (value, item) in later.Sequence.OrderBy(entry => entry.Key))
        {
            if (item.Provenance == OrderingProvenance.Explicit)
            {
                // Section 17.1: "explicit later ordering values patch matching items".
                allocator.Supply(value);
                sequence = sequence.SetItem(value, item);
                continue;
            }

            // Section 17.1: "implicit later items concatenate".
            if (!allocator.TryAllocate(out var allocated))
            {
                ReportOverflow(path);
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

    private void Report(ImmutableArray<NamePart> path, string message, string spec)
    {
        var text = PathText(path);

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Type001(
                DiagnosticPhase.Input,
                spec,
                message,
                cardinalityKey: text ?? string.Empty,
                path: text)));
    }

    private void ReportOverflow(ImmutableArray<NamePart> path) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Limit001(
                DiagnosticPhase.Input,
                "\u00A75.4",
                "this sequence has no ordering value left: Section 5.4 allocates above the "
                + "greatest value ever used at the path, and that is already the maximum.",
                path: PathText(path))));

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
    /// A name containing a <c>Q{...}</c> URI that contains the delimiter has no escaped spelling at
    /// that delimiter, so encoding can fail. The structural form is used then: it is stable and
    /// distinguishes the path from every other, which is what the cardinality key needs, even
    /// though it is not the spelling a user would write.
    /// </para>
    /// </remarks>
    private static string? PathText(ImmutableArray<NamePart> path)
    {
        if (path.IsEmpty)
        {
            return null;
        }

        var name = new QualifiedName(path);

        return NamespaceEncoder.TryEncodeName(
            name, NamespaceEncoder.DefaultDelimiter, out var text, out _)
            ? text!
            : name.ToString();
    }
}
