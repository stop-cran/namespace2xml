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
        ImmutableDictionary<NamePart, OverlayNode> children,
        ImmutableDictionary<long, SequenceItem> sequence,
        ImmutableList<BoundComment> comments)
    {
        Marks = marks;
        Payload = payload;
        HasExplicitMapping = hasExplicitMapping;
        Children = children;
        Sequence = sequence;
        Comments = comments;
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

    /// <summary>Mapping children by name, unordered. Use <see cref="OrderedChildren"/> to render.</summary>
    public ImmutableDictionary<NamePart, OverlayNode> Children { get; }

    /// <summary>
    /// Sequence items by Section 5.4 ordering value, unordered. Use <see cref="OrderedSequence"/>
    /// to render.
    /// </summary>
    public ImmutableDictionary<long, SequenceItem> Sequence { get; }

    /// <summary>Comments bound to this node, unordered. Use <see cref="OrderedComments"/> to render.</summary>
    public ImmutableList<BoundComment> Comments { get; }

    /// <summary>A node with no payload, no children, no sequence and no comments.</summary>
    /// <param name="marks">The marks of the contribution that created it.</param>
    public static OverlayNode Empty(NodeMarks marks) =>
        new(marks, null, false, ImmutableDictionary<NamePart, OverlayNode>.Empty,
            ImmutableDictionary<long, SequenceItem>.Empty, ImmutableList<BoundComment>.Empty);

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
            ImmutableDictionary<NamePart, OverlayNode>.Empty,
            ImmutableDictionary<long, SequenceItem>.Empty,
            ImmutableList<BoundComment>.Empty);
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
        Payload is null && !HasExplicitMapping && Children.IsEmpty && Sequence.IsEmpty;

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
            Marks.WithPayload(position), winner, HasExplicitMapping, Children, Sequence, Comments);
    }

    /// <summary>Records an explicit mapping-presence contribution at this node.</summary>
    /// <param name="position">The contribution's position mark.</param>
    public OverlayNode WithExplicitMapping(StableOrderingKey position) =>
        new(Marks.WithMapping(position), Payload, true, Children, Sequence, Comments);

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
            Marks.WithDescendant(child.Marks.Position),
            Payload,
            HasExplicitMapping,
            Children.SetItem(name, child),
            Sequence,
            Comments);
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
            Marks.WithSequenceItem(item.Node.Marks.Position),
            Payload,
            HasExplicitMapping,
            Children,
            Sequence.SetItem(orderingValue, item),
            Comments);
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
            Marks, Payload, HasExplicitMapping, Children, Sequence, Comments.Add(comment));
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
                Marks, Payload, HasExplicitMapping, Children.Remove(name), Sequence, Comments)
            : this;
    }

    /// <summary>Removes this node's payload, leaving its container facets intact.</summary>
    public OverlayNode WithoutPayload() =>
        Payload is null
            ? this
            : new OverlayNode(Marks, null, HasExplicitMapping, Children, Sequence, Comments);

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
