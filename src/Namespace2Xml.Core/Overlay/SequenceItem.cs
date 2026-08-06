namespace Namespace2Xml.Overlay;

/// <summary>
/// How a sequence item acquired its Section 5.4 ordering value.
/// </summary>
/// <remarks>
/// Section 5.4 requires this to survive <c>root</c>, <c>key</c>, <c>type</c>, output-instance
/// construction, and destination planning, so it is a property of the item rather than something
/// recomputed at render time.
/// </remarks>
public enum OrderingProvenance
{
    /// <summary>Native sequence items and items created by structural transformations.</summary>
    Implicit,

    /// <summary>Canonical numeric mapping children, which supply their value explicitly.</summary>
    Explicit,
}

/// <summary>
/// One item of a Section 5.4 sequence, together with the provenance of its ordering value.
/// </summary>
/// <param name="Node">The item's own overlay node.</param>
/// <param name="Provenance">Whether the ordering value was allocated or supplied.</param>
/// <remarks>
/// The ordering value itself is the key this item is stored under, not a field here, so that the
/// value and the item cannot disagree.
/// </remarks>
public sealed record SequenceItem(OverlayNode Node, OrderingProvenance Provenance)
{
    /// <summary>An implicitly ordered item.</summary>
    /// <param name="node">The item's node.</param>
    public static SequenceItem Native(OverlayNode node) => new(node, OrderingProvenance.Implicit);

    /// <summary>An item whose ordering value came from a canonical numeric mapping child.</summary>
    /// <param name="node">The item's node.</param>
    public static SequenceItem Numbered(OverlayNode node) => new(node, OrderingProvenance.Explicit);
}
