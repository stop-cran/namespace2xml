using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;

namespace Namespace2Xml.Output;

/// <summary>
/// Aggregates explicit empty-container facets a final destination projection cannot represent.
/// </summary>
internal sealed class EmptyContainerLosses
{
    private int emptyMappings;
    private int emptySequences;

    /// <summary>
    /// Counts the node's winning authored container facet when it is explicit and empty.
    /// </summary>
    /// <remarks>
    /// Own shape marks exclude carrier and projection-created ancestors. The winning-shape checks
    /// exclude the facet Section 17.1 already reports through <c>TYPE002</c>.
    /// </remarks>
    public void Observe(OverlayNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (IsEmptyMapping(node))
        {
            emptyMappings++;
        }
        else if (IsEmptySequence(node))
        {
            emptySequences++;
        }
    }

    /// <summary>
    /// Counts an empty sequence only after a destination has selected its lossy sequence path.
    /// </summary>
    public void ObserveEmptySequence(OverlayNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (IsEmptySequence(node))
        {
            emptySequences++;
        }
    }

    /// <summary>Emits the one destination-scoped warning, if this projection lost any facets.</summary>
    public void Report(
        DiagnosticBuffer diagnostics, string specification, DestinationRef? destination)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentException.ThrowIfNullOrEmpty(specification);

        if (emptyMappings == 0 && emptySequences == 0)
        {
            return;
        }

        var categories = new List<string>(2);

        if (emptyMappings != 0)
        {
            categories.Add($"empty-mapping={emptyMappings}");
        }

        if (emptySequences != 0)
        {
            categories.Add($"empty-sequence={emptySequences}");
        }

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Warn015(
                DiagnosticPhase.Planning,
                specification,
                "destination projection discarded explicit empty containers it cannot represent: "
                + $"{string.Join("; ", categories)}.",
                FlatIdentity.Key(destination?.Canonical, "empty-containers"),
                destination?.Canonical),
            DestinationOrder: destination?.Order));
    }

    private static bool IsEmptyMapping(OverlayNode node) =>
        node.Marks.ContainerIsMapping
        && node.Marks.OwnMappingShape is not null
        && node.Children.IsEmpty;

    private static bool IsEmptySequence(OverlayNode node) =>
        node.Marks.ContainerIsSequence
        && node.Marks.OwnSequenceShape is not null
        && !node.OrderedSequence.Any();
}
