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
    /// <param name="mappingShape">The mapping shape-mark, or <see langword="null"/> when absent.</param>
    /// <param name="sequenceShape">The sequence shape-mark, or <see langword="null"/> when absent.</param>
    private NodeMarks(
        StableOrderingKey position,
        StableOrderingKey? mappingShape,
        StableOrderingKey? sequenceShape)
    {
        Position = position;
        MappingShape = mappingShape;
        SequenceShape = sequenceShape;
    }

    /// <summary>
    /// The Section 4.4 position mark: the latest contribution that addresses this node itself. It
    /// fixes the node's place in Section 5.2 mapping order.
    /// </summary>
    public StableOrderingKey Position { get; }

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

    /// <summary>Whether a mapping contribution would win an exclusive-shape contest.</summary>
    /// <remarks>
    /// A node with neither shape has no container shape and is not a mapping.
    /// </remarks>
    public bool RendersAsMapping =>
        MappingShape is { } mapping && (SequenceShape is not { } sequence || mapping > sequence);

    /// <summary>Whether a sequence contribution would win an exclusive-shape contest.</summary>
    /// <remarks>
    /// A sequence contribution at the same key as a mapping contribution cannot happen: two
    /// contributions with one Section 4.7 key are one contribution.
    /// </remarks>
    public bool RendersAsSequence =>
        SequenceShape is { } sequence && (MappingShape is not { } mapping || sequence > mapping);

    /// <summary>Marks for a node whose first contribution is a payload or a bare presence.</summary>
    public static NodeMarks ForPayload(StableOrderingKey position) =>
        new(position, mappingShape: null, sequenceShape: null);

    /// <summary>Marks for a node whose first contribution requires mapping shape.</summary>
    public static NodeMarks ForMapping(StableOrderingKey position) =>
        new(position, mappingShape: position, sequenceShape: null);

    /// <summary>Marks for a node whose first contribution requires sequence shape.</summary>
    public static NodeMarks ForSequence(StableOrderingKey position) =>
        new(position, mappingShape: null, sequenceShape: position);

    /// <summary>
    /// Records a contribution that addresses this node itself, advancing the position mark.
    /// </summary>
    public NodeMarks WithPayload(StableOrderingKey position) =>
        new(StableOrderingKey.Later(Position, position), MappingShape, SequenceShape);

    /// <summary>
    /// Records a contribution that requires mapping shape at this node itself, advancing both the
    /// position mark and the mapping shape-mark.
    /// </summary>
    public NodeMarks WithMapping(StableOrderingKey position) =>
        new(
            StableOrderingKey.Later(Position, position),
            Later(MappingShape, position),
            SequenceShape);

    /// <summary>
    /// Records a contribution that requires sequence shape at this node itself, advancing both the
    /// position mark and the sequence shape-mark.
    /// </summary>
    public NodeMarks WithSequence(StableOrderingKey position) =>
        new(
            StableOrderingKey.Later(Position, position),
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
        new(Position, Later(MappingShape, position), SequenceShape);

    private static StableOrderingKey? Later(StableOrderingKey? left, StableOrderingKey? right) =>
        (left, right) switch
        {
            (null, null) => null,
            (null, { } only) => only,
            ({ } only, null) => only,
            ({ } a, { } b) => StableOrderingKey.Later(a, b),
        };
}
