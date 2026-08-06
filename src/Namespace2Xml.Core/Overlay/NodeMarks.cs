namespace Namespace2Xml.Overlay;

/// <summary>
/// The Section 4.4 marks a node carries: one position mark, and the two shape marks whose later
/// value decides which shape an exclusive destination renders.
/// </summary>
/// <remarks>
/// <para>
/// The distinction between the two kinds is the whole point. A descendant contribution refreshes an
/// ancestor's mapping shape-mark, because the ancestor must now be a mapping to contain it, but it
/// must <b>not</b> move the ancestor: Section 5.2 states that adding a new child never moves its
/// parent. Folding both into one mark would reorder a mapping every time something was added deep
/// inside it, and nothing downstream would notice, because the output would still be internally
/// consistent -- just different from the output the same inputs produced yesterday.
/// </para>
/// <para>
/// A shape mark is absent until something contributes that shape. Absent is not the same as
/// <see cref="StableOrderingKey.First"/>: a node with no sequence contribution at all must lose the
/// shape contest to a mapping contribution at the very first source position, which it would win on
/// a tie if absence were spelled as the smallest key.
/// </para>
/// </remarks>
public readonly record struct NodeMarks
{
    /// <summary>Creates the marks for a node's first contribution.</summary>
    /// <param name="position">The contribution's position mark.</param>
    /// <param name="addressedDirectly">Whether a contribution addresses the node itself.</param>
    /// <param name="payloadMark">The payload mark, or <see langword="null"/> when absent.</param>
    /// <param name="mappingShape">The mapping shape-mark, or <see langword="null"/> when absent.</param>
    /// <param name="sequenceShape">The sequence shape-mark, or <see langword="null"/> when absent.</param>
    private NodeMarks(
        StableOrderingKey position,
        bool addressedDirectly,
        StableOrderingKey? payloadMark,
        StableOrderingKey? mappingShape,
        StableOrderingKey? sequenceShape)
    {
        Position = position;
        AddressedDirectly = addressedDirectly;
        PayloadMark = payloadMark;
        MappingShape = mappingShape;
        SequenceShape = sequenceShape;
    }

    /// <summary>
    /// The Section 4.4 position mark: the latest contribution that addresses this node itself. It
    /// fixes the node's place in Section 5.2 mapping order.
    /// </summary>
    public StableOrderingKey Position { get; }

    /// <summary>
    /// Whether any contribution addresses this node itself, as opposed to the node existing only
    /// because something deeper needed a container.
    /// </summary>
    /// <remarks>
    /// Section 5.2 gives an intermediate node "the position mark of the earliest contribution that
    /// required it", while a directly addressed node takes the latest such contribution. The two
    /// rules point in opposite directions, so merging two nodes cannot pick between their position
    /// marks without knowing which kind each one is. The position mark alone does not say: a node
    /// materialised at key K and a node overridden at key K carry the same mark and must merge
    /// differently.
    /// </remarks>
    public bool AddressedDirectly { get; }

    /// <summary>
    /// The latest scalar or null contribution at this node, or <see langword="null"/> when there is
    /// none. This is step 1 of the Section 4.4 exclusive-shape rule, and it is not the same as
    /// <see cref="Position"/>: an explicit mapping-presence contribution advances the position mark
    /// without being a scalar contribution, so a node whose payload, mapping and payload arrive in
    /// that order would otherwise judge the second payload to be earlier than itself.
    /// </summary>
    public StableOrderingKey? PayloadMark { get; }

    /// <summary>
    /// The Section 4.4 mapping shape-mark: the latest explicit mapping-presence or descendant
    /// contribution requiring mapping shape, or <see langword="null"/> when there is none.
    /// </summary>
    public StableOrderingKey? MappingShape { get; }

    /// <summary>
    /// The Section 4.4 sequence shape-mark: the latest sequence contribution, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    public StableOrderingKey? SequenceShape { get; }

    /// <summary>
    /// The Section 4.4 container shape-mark: the later of the mapping and sequence shape-marks.
    /// </summary>
    public StableOrderingKey? ContainerShape => Later(MappingShape, SequenceShape);

    /// <summary>
    /// Whether the scalar or null payload wins the Section 4.4 exclusive-shape contest, so an
    /// exclusive destination renders this node as a scalar and omits its container facets.
    /// </summary>
    public bool RendersAsScalar =>
        PayloadMark is { } payload && (ContainerShape is not { } container || payload > container);

    /// <summary>
    /// Whether a container wins the Section 4.4 exclusive-shape contest against the payload.
    /// </summary>
    public bool RendersAsContainer =>
        ContainerShape is { } container && (PayloadMark is not { } payload || container > payload);

    /// <summary>Whether an exclusive destination renders this node as a mapping.</summary>
    /// <remarks>
    /// A node with neither shape has no container shape and is not a mapping. A node whose payload
    /// is later than every container contribution is not a mapping either, by Section 4.4 step 3.
    /// </remarks>
    public bool RendersAsMapping =>
        RendersAsContainer
        && MappingShape is { } mapping
        && (SequenceShape is not { } sequence || mapping > sequence);

    /// <summary>Whether an exclusive destination renders this node as a sequence.</summary>
    /// <remarks>
    /// A sequence contribution at the same key as a mapping contribution cannot happen: two
    /// contributions with one Section 4.7 key are one contribution.
    /// </remarks>
    public bool RendersAsSequence =>
        RendersAsContainer
        && SequenceShape is { } sequence
        && (MappingShape is not { } mapping || sequence > mapping);

    /// <summary>
    /// Marks for a node that a contribution addresses without giving it a payload or a shape, which
    /// is how an intermediate node on the way to a deeper descendant first comes into existence.
    /// </summary>
    public static NodeMarks At(StableOrderingKey position) =>
        new(position, addressedDirectly: false, payloadMark: null, mappingShape: null, sequenceShape: null);

    /// <summary>Marks for a node whose first contribution is a payload.</summary>
    public static NodeMarks ForPayload(StableOrderingKey position) =>
        new(position, addressedDirectly: true, payloadMark: position, mappingShape: null, sequenceShape: null);

    /// <summary>Marks for a node whose first contribution requires mapping shape.</summary>
    public static NodeMarks ForMapping(StableOrderingKey position) =>
        new(position, addressedDirectly: true, payloadMark: null, mappingShape: position, sequenceShape: null);

    /// <summary>Marks for a node whose first contribution requires sequence shape.</summary>
    public static NodeMarks ForSequence(StableOrderingKey position) =>
        new(position, addressedDirectly: true, payloadMark: null, mappingShape: null, sequenceShape: position);

    /// <summary>
    /// Records a contribution that addresses this node itself, advancing the position mark.
    /// </summary>
    public NodeMarks WithPayload(StableOrderingKey position) =>
        new(
            StableOrderingKey.Later(Position, position),
            addressedDirectly: true,
            Later(PayloadMark, position),
            MappingShape,
            SequenceShape);

    /// <summary>
    /// Records a contribution that requires mapping shape at this node itself, advancing both the
    /// position mark and the mapping shape-mark.
    /// </summary>
    public NodeMarks WithMapping(StableOrderingKey position) =>
        new(
            StableOrderingKey.Later(Position, position),
            addressedDirectly: true,
            PayloadMark,
            Later(MappingShape, position),
            SequenceShape);

    /// <summary>
    /// Records a contribution that requires sequence shape at this node itself, advancing both the
    /// position mark and the sequence shape-mark.
    /// </summary>
    public NodeMarks WithSequence(StableOrderingKey position) =>
        new(
            StableOrderingKey.Later(Position, position),
            addressedDirectly: true,
            PayloadMark,
            MappingShape,
            Later(SequenceShape, position));

    /// <summary>
    /// Records a strictly deeper descendant, which refreshes the mapping shape-mark and leaves the
    /// position mark alone.
    /// </summary>
    /// <remarks>
    /// Section 5.2: "Adding a new child therefore never moves its parent."
    /// </remarks>
    public NodeMarks WithDescendant(StableOrderingKey position) =>
        new(Position, AddressedDirectly, PayloadMark, Later(MappingShape, position), SequenceShape);

    /// <summary>
    /// Records a sequence item, which refreshes the sequence shape-mark and leaves the position
    /// mark alone.
    /// </summary>
    /// <remarks>
    /// An item is a descendant, so Section 4.4's rule that a deeper contribution refreshes shape
    /// "without changing that ancestor's position mark" applies to it exactly as Section 5.2
    /// applies to a mapping child. Appending to a list must not move the list within its own
    /// parent.
    /// </remarks>
    public NodeMarks WithSequenceItem(StableOrderingKey position) =>
        new(Position, AddressedDirectly, PayloadMark, MappingShape, Later(SequenceShape, position));

    /// <summary>
    /// The marks of a node that carries both of two nodes' contributions, taking the later of each
    /// shape mark independently.
    /// </summary>
    /// <param name="other">The other node's marks.</param>
    /// <remarks>
    /// <para>
    /// Section 17.1 merges each facet on its own evidence: a payload plus a container "retain both
    /// in the overlay with independent source marks". Collapsing to a single later-wins mark would
    /// discard the losing facet's evidence and change how the merged node renders to an exclusive
    /// destination, which Section 4.4 decides from the marks and not from what merged last.
    /// </para>
    /// <para>
    /// The position mark is the exception, and it does not take the later value. Section 5.2 says a
    /// directly addressed node moves to its latest override, while an intermediate node keeps the
    /// earliest contribution that required it, because every contribution that could materialise it
    /// is a descendant and "adding a new child therefore never moves its parent". Taking the later
    /// mark unconditionally would let a second source that merely adds a child to <c>a</c> move
    /// <c>a</c> past a sibling declared after it in the first source.
    /// </para>
    /// </remarks>
    public NodeMarks Combine(NodeMarks other) =>
        new(
            CombinePosition(this, other),
            AddressedDirectly || other.AddressedDirectly,
            Later(PayloadMark, other.PayloadMark),
            Later(MappingShape, other.MappingShape),
            Later(SequenceShape, other.SequenceShape));

    private static StableOrderingKey CombinePosition(NodeMarks left, NodeMarks right) =>
        (left.AddressedDirectly, right.AddressedDirectly) switch
        {
            (true, true) => StableOrderingKey.Later(left.Position, right.Position),
            (true, false) => left.Position,
            (false, true) => right.Position,
            (false, false) =>
                left.Position < right.Position ? left.Position : right.Position,
        };

    private static StableOrderingKey? Later(StableOrderingKey? left, StableOrderingKey? right) =>
        (left, right) switch
        {
            (null, null) => null,
            (null, { } only) => only,
            ({ } only, null) => only,
            ({ } a, { } b) => StableOrderingKey.Later(a, b),
        };
}
