using System.Collections.Immutable;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

/// <summary>
/// One node of the Section 4.2 overlay tree.
/// </summary>
/// <remarks>
/// <para>
/// The facets are deliberately <b>not</b> a discriminated union. Section 4.2 states that one
/// logical path may simultaneously retain a payload, an explicit mapping presence, ordered mapping
/// children and a sequence, and that "mapping, sequence, scalar, and null are therefore projections
/// of an overlay node, not mutually exclusive internal node kinds". The obvious C# model — an
/// abstract node with <c>ScalarNode</c>, <c>MappingNode</c> and <c>SequenceNode</c> subclasses —
/// cannot express <c>a.x=1</c> together with <c>a.x.z=3</c> without discarding one of them at
/// ingestion, which Section 4.2 explicitly forbids. Choosing a shape is a rendering decision made
/// per output instance under Section 4.4, and it is made against a node that still holds both.
/// </para>
/// <para>
/// Ordering is derived, never stored. Mapping order is a function of the children's position marks
/// (Section 5.2) and sequence order is a function of the ordering values (Section 5.4), so
/// <see cref="OrderedChildren"/> and <see cref="OrderedSequence"/> sort on demand rather than
/// maintaining an insertion order that could disagree with the marks. An insertion order kept in
/// parallel with the marks is a second source of truth, and the failure it produces — output whose
/// key order drifts from what the marks say — is invisible to any test that only checks which keys
/// are present.
/// </para>
/// </remarks>
public sealed class OverlayNode
{
    private OverlayNode(
        NodeMarks marks,
        ScalarPayload? payload,
        bool hasExplicitMapping,
        bool hasExplicitSequence,
        ImmutableDictionary<NamePart, OverlayNode> children,
        ImmutableDictionary<long, SequenceItem> sequence,
        ImmutableList<BoundComment> comments,
        long sequenceHighWater)
    {
        Marks = marks;
        Payload = payload;
        HasExplicitMapping = hasExplicitMapping;
        HasExplicitSequence = hasExplicitSequence;
        Children = children;
        Sequence = sequence;
        Comments = comments;
        SequenceHighWater = sequenceHighWater;
    }

    /// <summary>The Section 4.4 marks that decide this node's order and rendered shape.</summary>
    public NodeMarks Marks { get; }

    /// <summary>
    /// The optional scalar or null payload. Absent is not the same as
    /// <see cref="ScalarPayload.Null"/>: Section 4.2 lists "an optional scalar or null payload", so
    /// a node with no payload and a node whose payload is null are different facts, and JSON must
    /// be able to emit the second.
    /// </summary>
    public ScalarPayload? Payload { get; }

    /// <summary>
    /// Whether an explicit mapping-presence contribution addressed this node, including an empty
    /// mapping. Section 4.4 gives empty mappings precedence participation, so this cannot be
    /// inferred from <see cref="Children"/> being nonempty.
    /// </summary>
    public bool HasExplicitMapping { get; }

    /// <summary>
    /// Whether Section 4.4 recorded an explicit sequence-presence contribution at this node.
    /// </summary>
    /// <remarks>
    /// Section 4.4 makes the sequence shape-mark "the latest surviving sequence contribution", and
    /// an empty native sequence is one. Without this flag an empty JSON array and a node that was
    /// never a sequence are the same node, and the array vanishes on the way through the tool for
    /// the same reason Section 4.2 gives for keeping empty mappings.
    /// </remarks>
    public bool HasExplicitSequence { get; }

    /// <summary>Mapping children by name, unordered. Use <see cref="OrderedChildren"/> to render.</summary>
    public ImmutableDictionary<NamePart, OverlayNode> Children { get; }

    /// <summary>
    /// Sequence items by Section 5.4 ordering value, unordered. Use <see cref="OrderedSequence"/>
    /// to render.
    /// </summary>
    public ImmutableDictionary<long, SequenceItem> Sequence { get; }

    /// <summary>Comments bound to this node, unordered. Use <see cref="OrderedComments"/> to render.</summary>
    public ImmutableList<BoundComment> Comments { get; }

    /// <summary>
    /// The Section 5.4 high-water mark of this path: "the greatest ordering value ever allocated or
    /// explicitly supplied at that path earlier in source order, including values later removed or
    /// replaced".
    /// </summary>
    /// <remarks>
    /// Stored rather than derived from <see cref="Sequence"/>, because the two differ exactly when
    /// it matters. Section 5.4 forbids automatic allocation from reusing a value "because an item
    /// was removed or replaced", and Section 17.2 states that <c>merge=replace</c> "does not lower
    /// the path's allocation high-water mark". A high-water read off the surviving items would fall
    /// after a replacement and hand the next item an ordering value a removed item once held, so a
    /// later reference or explicit contribution addressing that value would silently hit a
    /// different item.
    /// </remarks>
    public long SequenceHighWater { get; }

    /// <summary>A node with no payload, no children, no sequence and no comments.</summary>
    /// <param name="marks">The marks of the contribution that created it.</param>
    public static OverlayNode Empty(NodeMarks marks) =>
        new(marks, null, false, false, ImmutableDictionary<NamePart, OverlayNode>.Empty,
            ImmutableDictionary<long, SequenceItem>.Empty, ImmutableList<BoundComment>.Empty,
            SequenceOrderingAllocator.InitialHighWaterMark);

    /// <summary>
    /// Builds a node from facets that already exist, for the merge engine.
    /// </summary>
    /// <remarks>
    /// Section 17.1 merges facets independently, so the merged node's marks are not reachable by
    /// replaying <c>With*</c> calls: replaying would advance the position mark for each replayed
    /// contribution and lose the distinction between a contribution at the node and one beneath it.
    /// Internal because it is the one way to build a node whose marks do not follow from its own
    /// construction, and only the merge engine has independent evidence for them.
    /// </remarks>
    internal static OverlayNode Compose(
        NodeMarks marks,
        ScalarPayload? payload,
        bool hasExplicitMapping,
        bool hasExplicitSequence,
        ImmutableDictionary<NamePart, OverlayNode> children,
        ImmutableDictionary<long, SequenceItem> sequence,
        ImmutableList<BoundComment> comments,
        long sequenceHighWater) =>
        new(
            marks, payload, hasExplicitMapping, hasExplicitSequence, children, sequence, comments,
            sequenceHighWater);

    /// <summary>This node with a Section 11.4 content-token ordering value recorded.</summary>
    /// <param name="contentToken">The value this node's XML parent assigned it.</param>
    public OverlayNode WithContentToken(long contentToken) =>
        Compose(
            Marks.WithContentToken(contentToken),
            Payload,
            HasExplicitMapping,
            HasExplicitSequence,
            Children,
            Sequence,
            Comments,
            SequenceHighWater);

    /// <summary>
    /// An intermediate node materialised only because something deeper needed a container.
    /// </summary>
    /// <param name="position">The key of the contribution that first required it.</param>
    public static OverlayNode Intermediate(StableOrderingKey position) =>
        Empty(NodeMarks.At(position));

    /// <summary>A leaf node carrying a payload.</summary>
    /// <param name="payload">The scalar or null payload.</param>
    /// <param name="position">The contribution's position mark.</param>
    public static OverlayNode OfPayload(ScalarPayload payload, StableOrderingKey position)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return new OverlayNode(
            NodeMarks.ForPayload(position),
            payload,
            false,
            false,
            ImmutableDictionary<NamePart, OverlayNode>.Empty,
            ImmutableDictionary<long, SequenceItem>.Empty,
            ImmutableList<BoundComment>.Empty,
            SequenceOrderingAllocator.InitialHighWaterMark);
    }

    /// <summary>
    /// Mapping children in Section 5.2 order: by position mark, then by name as unsigned UTF-8
    /// bytes.
    /// </summary>
    public IEnumerable<KeyValuePair<NamePart, OverlayNode>> OrderedChildren =>
        Children.OrderBy(child => child, MappingOrder.Instance);

    /// <summary>Sequence items in ascending Section 5.4 ordering value.</summary>
    public IEnumerable<KeyValuePair<long, SequenceItem>> OrderedSequence =>
        Sequence.OrderBy(item => item.Key);

    /// <summary>Comments in Section 4.5 accumulation order.</summary>
    public IEnumerable<BoundComment> OrderedComments => Comments.Sort(BoundComment.SourceOrder);

    /// <summary>Whether this node holds anything a format could render.</summary>
    public bool IsEmpty =>
        Payload is null
        && !HasExplicitMapping
        && !HasExplicitSequence
        && Children.IsEmpty
        && Sequence.IsEmpty;

    /// <summary>Records a payload contribution at this node.</summary>
    /// <param name="payload">The scalar or null payload.</param>
    /// <param name="position">The contribution's position mark.</param>
    /// <remarks>
    /// The surviving payload is the one from the later contribution, judged against the Section 4.4
    /// payload mark rather than the position mark. The position mark also advances for explicit
    /// mapping and sequence contributions, so judging against it would let an intervening
    /// <c>a={}</c> make a genuinely later <c>a=2</c> lose to an earlier <c>a=1</c>.
    /// </remarks>
    public OverlayNode WithPayload(ScalarPayload payload, StableOrderingKey position)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var winner = Marks.PayloadMark is { } existing && existing > position ? Payload : payload;

        return new OverlayNode(
            Marks.WithPayload(position), winner, HasExplicitMapping, HasExplicitSequence,
            Children, Sequence, Comments, SequenceHighWater);
    }

    /// <summary>Records an explicit mapping-presence contribution at this node.</summary>
    /// <param name="position">The contribution's position mark.</param>
    public OverlayNode WithExplicitMapping(StableOrderingKey position) =>
        new(Marks.WithMapping(position), Payload, true, HasExplicitSequence, Children, Sequence,
            Comments, SequenceHighWater);

    /// <summary>Records an explicit sequence-presence contribution at this node.</summary>
    /// <param name="position">The contribution's position mark.</param>
    /// <remarks>
    /// Section 4.4 counts an empty native sequence as a sequence contribution, exactly as it counts
    /// an empty mapping as a mapping contribution, so this exists for the same reason
    /// <see cref="WithExplicitMapping"/> does. A sequence that has items records its shape through
    /// them and does not need this.
    /// </remarks>
    public OverlayNode WithExplicitSequence(StableOrderingKey position) =>
        new(Marks.WithSequence(position), Payload, HasExplicitMapping, true, Children, Sequence,
            Comments, SequenceHighWater);

    /// <summary>Replaces or adds a mapping child, refreshing this node's mapping shape-mark.</summary>
    /// <param name="name">The child's name part.</param>
    /// <param name="child">The child node.</param>
    /// <remarks>
    /// Section 5.2: adding a child never moves its parent, so this advances the mapping shape-mark
    /// through <see cref="NodeMarks.WithDescendant"/> and leaves the position mark alone.
    /// </remarks>
    public OverlayNode WithChild(NamePart name, OverlayNode child)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(child);

        return new OverlayNode(
            // Section 4.4: "any later deep descendant refreshes the mapping shape-mark of every
            // ancestor required to contain it". The child's own position mark is not that
            // descendant — Section 5.2 pins it to whatever first materialised the child — so the
            // refresh takes the latest contribution anywhere beneath it.
            Marks.WithDescendant(child.Marks.Latest),
            Payload,
            HasExplicitMapping,
            HasExplicitSequence,
            Children.SetItem(name, child),
            Sequence,
            Comments,
            // Section 5.4: a mapping child whose name is a canonical in-range decimal "reserves
            // that ordering value at its own source position during concrete merging, whether or
            // not its containing mapping ultimately qualifies for sequence inference". Reserving
            // here rather than at inference time is what keeps step 11 from retroactively
            // reallocating a native item that was allocated in between.
            OrderingValues.TryRead(name, out var reserved)
                ? Math.Max(SequenceHighWater, reserved)
                : SequenceHighWater);
    }

    /// <summary>
    /// Raises the Section 5.4 high-water mark without creating an item.
    /// </summary>
    /// <param name="orderingValue">The value being reserved.</param>
    /// <remarks>
    /// The mark never falls: Section 5.4 records "the greatest ordering value ever allocated or
    /// explicitly supplied", so a lower reservation is not an error, it simply changes nothing.
    /// </remarks>
    public OverlayNode WithReservedOrderingValue(long orderingValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(orderingValue);

        return orderingValue <= SequenceHighWater
            ? this
            : new OverlayNode(
                Marks, Payload, HasExplicitMapping, HasExplicitSequence, Children, Sequence,
                Comments, orderingValue);
    }

    /// <summary>Replaces or adds a sequence item, refreshing this node's sequence shape-mark.</summary>
    /// <param name="orderingValue">The Section 5.4 ordering value.</param>
    /// <param name="item">The item.</param>
    /// <remarks>
    /// An item is a descendant, so it refreshes shape without moving this node. Section 5.4 also
    /// forbids reallocating around a replacement, so an item that lands on an occupied ordering
    /// value overrides it in place rather than displacing anything.
    /// </remarks>
    public OverlayNode WithSequenceItem(long orderingValue, SequenceItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegative(orderingValue);

        return new OverlayNode(
            Marks.WithSequenceItem(item.Node.Marks.Latest),
            Payload,
            HasExplicitMapping,
            true,
            Children,
            Sequence.SetItem(orderingValue, item),
            Comments,
            Math.Max(SequenceHighWater, orderingValue));
    }

    /// <summary>
    /// Appends an item at the next Section 5.4 implicit ordering value.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="appended">The node with the item appended, when one fits.</param>
    /// <returns>
    /// Whether an ordering value was available. Section 5.4 makes allocating above
    /// <see cref="SequenceOrderingAllocator.MaxOrderingValue"/> "a blocking limit error", which is a
    /// diagnostic and not an exception, so the caller decides how to report it.
    /// </returns>
    public bool TryAppendSequenceItem(SequenceItem item, out OverlayNode appended)
    {
        ArgumentNullException.ThrowIfNull(item);

        var allocator = SequenceOrderingAllocator.From(SequenceHighWater);

        if (!allocator.TryAllocate(out var value))
        {
            appended = this;
            return false;
        }

        appended = WithSequenceItem(value, item);
        return true;
    }

    /// <summary>Binds a comment to this node.</summary>
    /// <param name="comment">The comment.</param>
    /// <remarks>
    /// Binding a comment does not touch any mark. Section 4.5 says comments move with the winning
    /// contribution, not that they are one; a comment that advanced the position mark would let a
    /// trailing <c>#</c> reorder a mapping.
    /// </remarks>
    public OverlayNode WithComment(BoundComment comment)
    {
        ArgumentNullException.ThrowIfNull(comment);

        return new OverlayNode(
            Marks, Payload, HasExplicitMapping, HasExplicitSequence, Children, Sequence,
            Comments.Add(comment), SequenceHighWater);
    }

    /// <summary>Removes a mapping child, as a Section 8.4 permanent exclusion mask does.</summary>
    /// <param name="name">The child's name part.</param>
    /// <remarks>
    /// The mapping shape-mark is not lowered. Section 5.4 states that removal "never shifts,
    /// defragments, or reuses an ordering value", and the same reasoning applies to shape: a mask
    /// suppresses a path, and undoing the shape evidence of a contribution that really occurred
    /// would let a mask change how an unrelated sibling renders.
    /// </remarks>
    public OverlayNode WithoutChild(NamePart name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Children.ContainsKey(name)
            ? new OverlayNode(
                Marks, Payload, HasExplicitMapping, HasExplicitSequence, Children.Remove(name),
                Sequence, Comments, SequenceHighWater)
            : this;
    }

    /// <summary>Removes a sequence item, as a Section 8.6 permanent exclusion mask does.</summary>
    /// <param name="orderingValue">The item's ordering value.</param>
    /// <remarks>
    /// The high-water mark is not lowered. Section 5.4: automatic allocation "never shifts,
    /// defragments, or reuses an ordering value because an item was removed or replaced".
    /// </remarks>
    public OverlayNode WithoutSequenceItem(long orderingValue) =>
        Sequence.ContainsKey(orderingValue)
            ? new OverlayNode(
                Marks, Payload, HasExplicitMapping, HasExplicitSequence, Children,
                Sequence.Remove(orderingValue), Comments, SequenceHighWater)
            : this;

    /// <summary>Removes this node's payload, leaving its container facets intact.</summary>
    public OverlayNode WithoutPayload() =>
        Payload is null
            ? this
            : new OverlayNode(
                Marks, null, HasExplicitMapping, HasExplicitSequence, Children, Sequence,
                Comments, SequenceHighWater);
    /// <inheritdoc/>
    public override string ToString() =>
        $"payload={Payload?.ToString() ?? "-"} children={Children.Count} sequence={Sequence.Count}";

    /// <summary>The Section 5.2 total order over mapping children.</summary>
    private sealed class MappingOrder : IComparer<KeyValuePair<NamePart, OverlayNode>>
    {
        internal static MappingOrder Instance { get; } = new();

        public int Compare(KeyValuePair<NamePart, OverlayNode> x, KeyValuePair<NamePart, OverlayNode> y)
        {
            var byPosition = x.Value.Marks.Position.CompareTo(y.Value.Marks.Position);
            return byPosition != 0 ? byPosition : NamePartOrder.Instance.Compare(x.Key, y.Key);
        }
    }
}
