using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Output;

/// <summary>One scalar of a flat output, before its key has been spelled.</summary>
/// <param name="Path">
/// The path a flat format spells as the key: Section 19.1's "generated zero-based decimal parts"
/// stand in for ordering values, and the Section 16.3 root is already applied.
/// </param>
/// <param name="LogicalPath">
/// The same path with the stable Section 5.4 ordering values that continue to govern matching and
/// precedence. Diagnostics name this one, because it is the path a user wrote.
/// </param>
/// <param name="Payload">The scalar being emitted.</param>
/// <param name="Comments">The node's comments, in Section 4.5 source order.</param>
public sealed record FlatEntry(
    ImmutableArray<NamePart> Path,
    ImmutableArray<NamePart> LogicalPath,
    ScalarPayload Payload,
    ImmutableArray<BoundComment> Comments);

/// <summary>
/// Flattens an output view into the ordered scalar entries Section 19.1, Section 19.2, and
/// Section 19.6 spell as keys.
/// </summary>
/// <remarks>
/// <para>
/// Section 19.1 fixes the walk: depth first in pre-order, a node's own scalar before anything
/// beneath it, mapping children in Section 5.2 order, sequence items in ascending ordering value.
/// Pre-order is what places <c>a.x=1</c> before <c>a.x.z=3</c>.
/// </para>
/// <para>
/// A node emits only one container facet. Section 16.4 makes every flat output a destination
/// requiring one container shape, so a node holding both projections takes the later one under
/// Section 17.1 and warns. Emitting both would give two keys to the single node Section 15.1
/// step 9 shares between a numeric mapping child and the sequence item at its ordering value.
/// </para>
/// <para>
/// The payload does not compete with the container. Namespace output emits <c>a=1</c> and
/// <c>a.b=2</c> from one node, and Section 19.6 says so outright: "no shape warning is emitted
/// merely because one logical path supplies both projections". Only the two container facets
/// contest each other.
/// </para>
/// </remarks>
public sealed class FlatProjection
{
    private readonly DiagnosticBuffer diagnostics;
    private readonly DestinationRef? destination;

    /// <summary>Creates a projection.</summary>
    /// <param name="diagnostics">The buffer shape-conflict warnings accumulate in.</param>
    /// <param name="destination">
    /// The Section 6.4.3 <c>destination</c> this output instance writes to, which is half of the
    /// "once per path and output instance" cardinality of <c>TYPE002</c>.
    /// </param>
    public FlatProjection(DiagnosticBuffer diagnostics, DestinationRef? destination = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.diagnostics = diagnostics;
        this.destination = destination;
    }

    /// <summary>Flattens a view.</summary>
    /// <param name="view">The selected output view.</param>
    /// <param name="root">
    /// The Section 16.3 root parts, applied before any spelling. For INI these become section-path
    /// parts rather than key text, which is why the root is prefixed here and not in the encoder.
    /// </param>
    /// <returns>The scalar entries, in emission order.</returns>
    public ImmutableArray<FlatEntry> Project(OverlayNode view, ImmutableArray<NamePart> root)
    {
        ArgumentNullException.ThrowIfNull(view);

        var entries = ImmutableArray.CreateBuilder<FlatEntry>();

        Visit(view, root, root, entries);

        return entries.ToImmutable();
    }

    private void Visit(
        OverlayNode node,
        ImmutableArray<NamePart> path,
        ImmutableArray<NamePart> logical,
        ImmutableArray<FlatEntry>.Builder entries)
    {
        if (node.Payload is { } payload)
        {
            entries.Add(new FlatEntry(path, logical, payload, [.. node.OrderedComments]));
        }

        if (node.Marks.HasBothContainers)
        {
            ReportShapeConflict(logical, node.Marks.ContainerIsSequence);
        }

        if (node.Marks.ContainerIsSequence)
        {
            // Section 5.4: flat output "must display fresh dense indices where their projection
            // requires indices, but matching and precedence continue to use stable ordering
            // values". Both paths are therefore carried: one is spelled, the other is named.
            var index = 0L;

            foreach (var (value, item) in node.OrderedSequence)
            {
                Visit(
                    item.Node,
                    path.Add(OrderingValues.ToNamePart(index)),
                    logical.Add(OrderingValues.ToNamePart(value)),
                    entries);
                index++;
            }
        }
        else
        {
            foreach (var (name, child) in node.OrderedChildren)
            {
                Visit(child, path.Add(name), logical.Add(name), entries);
            }
        }
    }

    private void ReportShapeConflict(ImmutableArray<NamePart> path, bool sequenceWins)
    {
        var text = FlatIdentity.PathText(path);

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Type002(
                DiagnosticPhase.Planning,
                "\u00A716.4",
                "this path supplies both a mapping and a sequence projection, and a flat output "
                + "renders one container shape: Section 17.1 keeps the later contribution, so the "
                + (sequenceWins ? "mapping children are" : "sequence items are")
                + " not emitted here.",
                cardinalityKey: FlatIdentity.Key(destination?.Canonical, text),
                path: text,
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));
    }
}
