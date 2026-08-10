using System.Collections.Immutable;

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
    /// <param name="ownMappingShape">
    /// The mapping shape-mark contributed at this node itself, or <see langword="null"/> when no
    /// contribution addressed it with mapping shape.
    /// </param>
    /// <param name="ownSequenceShape">
    /// The sequence shape-mark contributed at this node itself, or <see langword="null"/> when no
    /// contribution addressed it with sequence shape.
    /// </param>
    /// <param name="contentToken">
    /// The Section 11.4 content-token ordering value, or <see langword="null"/> when the node did
    /// not come from an XML parent.
    /// </param>
    /// <param name="nativeMappings">
    /// The native JSON/YAML mapping contributions at this node, ascending by key.
    /// </param>
    private NodeMarks(
        StableOrderingKey position,
        bool addressedDirectly,
        StableOrderingKey? payloadMark,
        StableOrderingKey? mappingShape,
        StableOrderingKey? sequenceShape,
        StableOrderingKey? ownMappingShape,
        StableOrderingKey? ownSequenceShape,
        long? contentToken,
        ImmutableArray<NativeMappingOrigin> nativeMappings)
    {
        Position = position;
        AddressedDirectly = addressedDirectly;
        PayloadMark = payloadMark;
        MappingShape = mappingShape;
        SequenceShape = sequenceShape;
        OwnMappingShape = ownMappingShape;
        OwnSequenceShape = ownSequenceShape;
        ContentToken = contentToken;
        natives = nativeMappings;
    }

    private readonly ImmutableArray<NativeMappingOrigin> natives;

    /// <summary>
    /// The Section 3.2 native JSON/YAML mapping contributions at this node, ascending by key, or
    /// empty when no native mapping supplied it.
    /// </summary>
    /// <remarks>
    /// Empty for every node an XML or namespace source built, and empty for a native sequence.
    /// A node holding one of these is a mapping some JSON or YAML document wrote as an object;
    /// whether that still matters is decided per output instance by <see cref="RendersAsSequence"/>,
    /// which is the only thing Section 3.2 asks about it.
    /// </remarks>
    public ImmutableArray<NativeMappingOrigin> NativeMappings =>
        natives.IsDefault ? [] : natives;

    /// <summary>
    /// The Section 11.4 content-token ordering value this node's XML parent assigned it, or
    /// <see langword="null"/> when no XML parent did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 11.4 assigns these "across all child elements, text, CDATA, and comments, including
    /// element-only parents", and says element-only children "retain ordinary element-name
    /// addressing while also carrying their content-token ordering value for deterministic
    /// placement". It is therefore not an address: <c>a.b</c> stays <c>a.b</c>, and this value only
    /// "determine[s] placement in the parent's serialized stream".
    /// </para>
    /// <para>
    /// Without it, <c>&lt;a&gt;&lt;b&gt;1&lt;/b&gt;&lt;c&gt;2&lt;/c&gt;&lt;b&gt;3&lt;/b&gt;&lt;/a&gt;</c>
    /// is indistinguishable from the same document with both <c>b</c> children written first, since
    /// the repeated pair becomes one sequence at <c>a.b</c> and a sequence has one position among
    /// its siblings. The reordering that follows is silent and changes what the document says.
    /// </para>
    /// </remarks>
    public long? ContentToken { get; }

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
    /// The mapping shape-mark contributed by something addressing this node itself, as opposed to a
    /// descendant that merely requires the node to contain it.
    /// </summary>
    /// <remarks>
    /// Section 4.4 defines the effective mapping shape-mark as the latest <em>surviving</em>
    /// contribution, and Section 8.7 fixes that word: "<em>Surviving</em> means not suppressed by a
    /// permanent mask." A descendant's evidence is therefore withdrawn when a mask removes it, and
    /// <see cref="MappingShape"/> alone cannot say how much of itself came from descendants. This
    /// mark is the part a mask can never take away, because suppressing the node itself removes the
    /// node rather than changing its shape.
    /// </remarks>
    public StableOrderingKey? OwnMappingShape { get; }

    /// <summary>
    /// The sequence shape-mark contributed by something addressing this node itself, as opposed to
    /// an item beneath it.
    /// </summary>
    public StableOrderingKey? OwnSequenceShape { get; }

    /// <summary>
    /// The Section 4.4 container shape-mark: the later of the mapping and sequence shape-marks.
    /// </summary>
    public StableOrderingKey? ContainerShape => Later(MappingShape, SequenceShape);

    /// <summary>
    /// The latest contribution anywhere in this node's subtree, including the node itself.
    /// </summary>
    /// <remarks>
    /// This is what an ancestor's mapping shape-mark must be refreshed with. Section 4.4 says "any
    /// later deep descendant refreshes the mapping shape-mark of every ancestor required to contain
    /// it", and <em>deep</em> is the load-bearing word: a contribution three levels down still
    /// requires every ancestor above it to have mapping shape. <see cref="Position"/> alone cannot
    /// carry that, because Section 5.2 forbids a descendant from moving an ancestor's position mark,
    /// so an intermediate node keeps the position of whatever first materialised it no matter how
    /// much later arrives beneath it. Refreshing an ancestor with a child's position mark therefore
    /// loses every contribution that did not create the child.
    /// </remarks>
    public StableOrderingKey Latest =>
        Later(Later(PayloadMark, ContainerShape), Position)!.Value;

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

    /// <summary>Whether both container projections are present, so choosing one drops the other.</summary>
    /// <remarks>
    /// Section 17.1 keeps both in the overlay and leaves the choice to the destination, so this is
    /// the condition a destination requiring one container shape warns on, not an error here.
    /// </remarks>
    public bool HasBothContainers => MappingShape is not null && SequenceShape is not null;

    /// <summary>
    /// Whether the later container contribution is the mapping, for a destination that requires one
    /// container shape but does not make the payload compete with it.
    /// </summary>
    /// <remarks>
    /// Equal shape-marks are not reachable: two contributions with one Section 4.7 key are one
    /// contribution, so a mapping and a sequence contribution never carry the same mark. The tie
    /// resolves to the mapping only so that this and <see cref="ContainerIsSequence"/> are
    /// exhaustive whenever a container exists, leaving no node whose container silently vanishes.
    /// </remarks>
    public bool ContainerIsMapping =>
        MappingShape is { } mapping && (SequenceShape is not { } sequence || mapping >= sequence);

    /// <summary>Whether the later container contribution is the sequence.</summary>
    public bool ContainerIsSequence =>
        SequenceShape is { } sequence && (MappingShape is not { } mapping || sequence > mapping);

    /// <summary>Whether an exclusive destination renders this node as a mapping.</summary>
    /// <remarks>
    /// A node with neither shape has no container shape and is not a mapping. A node whose payload
    /// is later than every container contribution is not a mapping either, by Section 4.4 step 3.
    /// </remarks>
    public bool RendersAsMapping => RendersAsContainer && ContainerIsMapping;

    /// <summary>Whether an exclusive destination renders this node as a sequence.</summary>
    public bool RendersAsSequence => RendersAsContainer && ContainerIsSequence;

    /// <summary>
    /// Marks for a node that a contribution addresses without giving it a payload or a shape, which
    /// is how an intermediate node on the way to a deeper descendant first comes into existence.
    /// </summary>
    public static NodeMarks At(StableOrderingKey position) =>
        new(position, addressedDirectly: false, payloadMark: null, mappingShape: null,
            sequenceShape: null, ownMappingShape: null, ownSequenceShape: null, contentToken: null,
            nativeMappings: []);

    /// <summary>Marks for a node whose first contribution is a payload.</summary>
    public static NodeMarks ForPayload(StableOrderingKey position) =>
        new(position, addressedDirectly: true, payloadMark: position, mappingShape: null,
            sequenceShape: null, ownMappingShape: null, ownSequenceShape: null, contentToken: null,
            nativeMappings: []);

    /// <summary>Marks for a node whose first contribution requires mapping shape.</summary>
    public static NodeMarks ForMapping(StableOrderingKey position) =>
        new(position, addressedDirectly: true, payloadMark: null, mappingShape: position,
            sequenceShape: null, ownMappingShape: position, ownSequenceShape: null, contentToken: null,
            nativeMappings: []);

    /// <summary>Marks for a node whose first contribution requires sequence shape.</summary>
    public static NodeMarks ForSequence(StableOrderingKey position) =>
        new(position, addressedDirectly: true, payloadMark: null, mappingShape: null,
            sequenceShape: position, ownMappingShape: null, ownSequenceShape: position, contentToken: null,
            nativeMappings: []);

    /// <summary>
    /// Records a contribution that addresses this node itself, advancing the position mark.
    /// </summary>
    public NodeMarks WithPayload(StableOrderingKey position) =>
        new(
            StableOrderingKey.Later(Position, position),
            addressedDirectly: true,
            Later(PayloadMark, position),
            MappingShape,
            SequenceShape,
            OwnMappingShape,
            OwnSequenceShape,
            ContentToken,
            natives);

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
            SequenceShape,
            Later(OwnMappingShape, position),
            OwnSequenceShape,
            ContentToken,
            natives);

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
            Later(SequenceShape, position),
            OwnMappingShape,
            Later(OwnSequenceShape, position),
            ContentToken,
            natives);

    /// <summary>
    /// Records a strictly deeper descendant, which refreshes the mapping shape-mark and leaves the
    /// position mark alone.
    /// </summary>
    /// <remarks>
    /// Section 5.2: "Adding a new child therefore never moves its parent."
    /// </remarks>
    public NodeMarks WithDescendant(StableOrderingKey position) =>
        new(Position, AddressedDirectly, PayloadMark, Later(MappingShape, position), SequenceShape,
            OwnMappingShape, OwnSequenceShape, ContentToken, natives);

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
        new(Position, AddressedDirectly, PayloadMark, MappingShape, Later(SequenceShape, position),
            OwnMappingShape, OwnSequenceShape, ContentToken, natives);

    /// <summary>
    /// The marks after Section 8.7 inference, which "replaces that contribution's mapping
    /// projection" with a sequence one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mapping shape-mark moves rather than being dropped, because the contributions that set
    /// it are the very ones the inferred sequence now carries. Dropping it would let a payload win
    /// the Section 4.4 exclusive-shape contest against a container that still exists, and clearing
    /// nothing would leave the node claiming both shapes, so a flat destination would warn about a
    /// conflict between a mapping facet and the sequence that replaced it.
    /// </para>
    /// <para>
    /// <see cref="ContainerShape"/> is therefore unchanged by this transformation, which is the
    /// point: inference decides <em>which</em> container a node projects, never whether the
    /// container beats the payload.
    /// </para>
    /// </remarks>
    public NodeMarks AsInferredSequence() =>
        new(Position, AddressedDirectly, PayloadMark, mappingShape: null, sequenceShape: ContainerShape,
            ownMappingShape: null, ownSequenceShape: Later(OwnMappingShape, OwnSequenceShape),
            contentToken: ContentToken, nativeMappings: natives);

    /// <summary>
    /// The marks after Section 16.6 <c>type=mapping</c> converts a winning sequence projection, the
    /// mirror of <see cref="AsInferredSequence"/>.
    /// </summary>
    /// <remarks>
    /// The conversion consumes the sequence rather than leaving it as a Section 4.4 loser, so the
    /// sequence shape-mark becomes the mapping's and the node projects one container. Keeping the
    /// sequence mark would leave the node claiming both shapes when it has one, and Section 17.1
    /// would then warn about a shape conflict this directive exists to settle.
    /// </remarks>
    public NodeMarks AsForcedMapping() =>
        new(Position, AddressedDirectly, PayloadMark, mappingShape: ContainerShape, sequenceShape: null,
            ownMappingShape: Later(OwnMappingShape, OwnSequenceShape), ownSequenceShape: null,
            contentToken: ContentToken, nativeMappings: natives);

    /// <summary>
    /// The marks of a node's independent payload and sequence facets, with its mapping projection
    /// removed, for the Section 16.5 <c>value</c> field.
    /// </summary>
    /// <remarks>
    /// Section 16.5 splits one child into a record and a <c>value</c> field. The two halves must
    /// not both claim the mapping shape-mark: the record holds the mapping fields, so the
    /// <c>value</c> field holds what is left, which by construction has no children.
    /// <para>
    /// The native-mapping provenance goes with the mapping shape-mark for the same reason. The
    /// <c>value</c> field is a scalar that no document ever wrote as an object, so Section 3.2 has
    /// nothing to warn about there, and leaving the origins on both halves would warn twice for one
    /// mapping if the record half later became a sequence.
    /// </para>
    /// </remarks>
    public NodeMarks WithoutMapping() =>
        new(Position, AddressedDirectly, PayloadMark, mappingShape: null, SequenceShape,
            ownMappingShape: null, OwnSequenceShape, ContentToken, nativeMappings: []);

    /// <summary>
    /// The marks after Section 8.6 permanent masking, recomputed from the contributions that
    /// survived it.
    /// </summary>
    /// <param name="mappingFromDescendants">
    /// The latest mark among surviving mapping children, or <see langword="null"/> when none
    /// remain.
    /// </param>
    /// <param name="sequenceFromItems">
    /// The latest mark among surviving sequence items, or <see langword="null"/> when none remain.
    /// </param>
    /// <remarks>
    /// Section 4.4 makes each shape-mark "the latest <em>surviving</em>" contribution of its kind,
    /// and Section 8.7 defines surviving as "not suppressed by a permanent mask". A mask that
    /// removes the only descendant requiring mapping shape therefore removes the mapping shape-mark
    /// with it, and the Section 4.4 exclusive-shape contest is settled without it. Carrying the mark
    /// across the prune instead leaves a contribution that no longer exists winning the contest
    /// against one that does, which loses the surviving data rather than merely mislabelling it.
    /// <para>
    /// The payload and position marks are not recomputed. Both record contributions that address
    /// the node itself, and a mask reaching the node removes the node rather than editing its marks.
    /// </para>
    /// </remarks>
    public NodeMarks AfterMasking(
        StableOrderingKey? mappingFromDescendants,
        StableOrderingKey? sequenceFromItems) =>
        new(
            Position,
            AddressedDirectly,
            PayloadMark,
            Later(OwnMappingShape, mappingFromDescendants),
            Later(OwnSequenceShape, sequenceFromItems),
            OwnMappingShape,
            OwnSequenceShape,
            ContentToken,
            natives);

    /// <summary>
    /// The marks of a node whose complete value one later contribution has replaced.
    /// </summary>
    /// <param name="replacement">The marks of the value that replaced this one.</param>
    /// <remarks>
    /// Section 16.10 <c>replace</c> says "the later complete value replaces the earlier value", and
    /// names what "value" covers: "payload, container presence, children, and sequence projection".
    /// Every mark that describes one of those describes something that is no longer there, so the
    /// replacement supplies all of them. Keeping the earlier shape marks leaves the Section 4.4
    /// contest to be settled between a projection that exists and one that was replaced, and the
    /// path is then reported as supplying both shapes when it supplies one.
    /// <para>
    /// The position mark is not replaced, for the reason <see cref="Combine"/> gives: Section 5.2
    /// governs where a key sits in mapping order and is not a merge strategy, so an intermediate
    /// node keeps the earliest contribution that required it. <see cref="AddressedDirectly"/>
    /// travels with it, because it exists only to say which of the two Section 5.2 rules the
    /// position mark follows and is not part of the value either.
    /// </para>
    /// </remarks>
    public NodeMarks AfterReplacement(NodeMarks replacement) =>
        new(
            CombinePosition(this, replacement),
            AddressedDirectly || replacement.AddressedDirectly,
            replacement.PayloadMark,
            replacement.MappingShape,
            replacement.SequenceShape,
            replacement.OwnMappingShape,
            replacement.OwnSequenceShape,
            replacement.ContentToken ?? ContentToken,
            replacement.natives);

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
            Later(SequenceShape, other.SequenceShape),
            Later(OwnMappingShape, other.OwnMappingShape),
            Later(OwnSequenceShape, other.OwnSequenceShape),
            CombineToken(this, other),
            UnionNatives(natives, other.natives));

    /// <summary>These marks with a Section 11.4 content-token ordering value recorded.</summary>
    /// <param name="contentToken">The value the node's XML parent assigned it.</param>
    public NodeMarks WithContentToken(long contentToken) =>
        new(
            Position,
            AddressedDirectly,
            PayloadMark,
            MappingShape,
            SequenceShape,
            OwnMappingShape,
            OwnSequenceShape,
            contentToken,
            natives);

    /// <summary>
    /// These marks with one Section 3.2 native JSON/YAML mapping contribution recorded.
    /// </summary>
    /// <param name="key">The contribution's Section 4.7 ordering key.</param>
    /// <param name="source">How diagnostics name the document that wrote it.</param>
    /// <remarks>
    /// Recorded for every nonempty native mapping and not only for a numeric-keyed one, because
    /// Section 8.7 infers "over the merged model": <c>{"a":{"0":"x"}}</c> in one document and
    /// <c>{"a":{"b":"y"}}</c> in another produce a node no single document could classify, and a
    /// mask may later remove the child that made it unclassifiable. Deciding at read time would
    /// ask a question the reader cannot answer yet, and would answer it wrongly in exactly the
    /// cases the warning exists for.
    /// </remarks>
    public NodeMarks WithNativeMapping(StableOrderingKey key, string source) =>
        new(
            Position,
            AddressedDirectly,
            PayloadMark,
            MappingShape,
            SequenceShape,
            OwnMappingShape,
            OwnSequenceShape,
            ContentToken,
            UnionNatives(natives, [new NativeMappingOrigin(key, source)]));

    /// <summary>
    /// The union of two native-mapping origin sets, ascending by key and free of duplicates.
    /// </summary>
    /// <param name="left">One set.</param>
    /// <param name="right">The other set.</param>
    /// <remarks>
    /// Sorted because Section 24 orders the resulting warnings by their source ordering key, and a
    /// set assembled in merge order would present them in the order the tree happened to be walked.
    /// Deduplicated by key because a merge may combine a node with contributions it already holds,
    /// and Section 22 owes one warning per source contribution rather than one per merge that
    /// carried it.
    /// </remarks>
    private static ImmutableArray<NativeMappingOrigin> UnionNatives(
        ImmutableArray<NativeMappingOrigin> left,
        ImmutableArray<NativeMappingOrigin> right)
    {
        if (right.IsDefaultOrEmpty)
        {
            return left.IsDefault ? [] : left;
        }

        if (left.IsDefaultOrEmpty)
        {
            return right;
        }

        var merged = new SortedDictionary<StableOrderingKey, NativeMappingOrigin>();

        foreach (var origin in left)
        {
            merged[origin.Key] = origin;
        }

        foreach (var origin in right)
        {
            merged[origin.Key] = origin;
        }

        return [.. merged.Values];
    }

    private static StableOrderingKey CombinePosition(NodeMarks left, NodeMarks right) =>
        (left.AddressedDirectly, right.AddressedDirectly) switch
        {
            (true, true) => StableOrderingKey.Later(left.Position, right.Position),
            (true, false) => left.Position,
            (false, true) => right.Position,
            (false, false) =>
                left.Position < right.Position ? left.Position : right.Position,
        };

    /// <summary>
    /// The content-token ordering value of a node carrying two contributions.
    /// </summary>
    /// <param name="left">One node's marks.</param>
    /// <param name="right">The other node's marks.</param>
    /// <remarks>
    /// Section 11.4 makes the value the parent's statement about where this child sits in that
    /// parent's serialized stream, so it follows the same rule as the position mark: the
    /// contribution that owns the position owns the placement. Taking the later value instead would
    /// let a second document that merely adds a grandchild move an element past a sibling the first
    /// document wrote before it. Where the owning side has none, the other side's is kept rather
    /// than discarded, because a value the merged element has is worth more than none.
    /// </remarks>
    private static long? CombineToken(NodeMarks left, NodeMarks right) =>
        (left.AddressedDirectly, right.AddressedDirectly) switch
        {
            (true, false) => left.ContentToken ?? right.ContentToken,
            (false, true) => right.ContentToken ?? left.ContentToken,
            _ => left.Position <= right.Position
                ? left.ContentToken ?? right.ContentToken
                : right.ContentToken ?? left.ContentToken,
        };

    private static StableOrderingKey? Later(StableOrderingKey? left, StableOrderingKey? right) => (left, right) switch
    {
        (null, null) => null,
        (null, { } only) => only,
        ({ } only, null) => only,
        ({ } a, { } b) => StableOrderingKey.Later(a, b),
    };
}
