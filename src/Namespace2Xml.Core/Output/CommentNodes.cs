using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;

namespace Namespace2Xml.Output;

/// <summary>
/// What an output that is not XML does with the Section 11.5 comment nodes selected into it.
/// </summary>
/// <remarks>
/// <para>
/// Section 19.5 is the only renderer that "emits retained XML comments". Every other format
/// discards them, and Section 3 fixes how: "Unsupported source concepts are discarded during
/// rendering and must produce one summarized warning per output file and feature category."
/// </para>
/// <para>
/// They are discarded rather than translated because Section 11.5 keeps a comment out of a
/// "'leading comment for the next value' representation", and Section 4.5 says "standalone XML
/// comments remain ordered content nodes and are not reassigned to adjacent values". Section 19.4
/// emits YAML comments "in normalized positions" and Section 20 emits namespace comments "where
/// their association can be represented"; a comment holding a content-token slot has no
/// association with any value, and giving it one would be exactly the reassignment both clauses
/// forbid.
/// </para>
/// <para>
/// The feature category is comment <em>nodes</em>, which is not the category the Section 4.5 bound
/// comments of <c>JsonSerializer</c> and <c>IniSerializer</c> report under. The two are different
/// source concepts — one is ordered content, the other an annotation on a value — and sharing a
/// cardinality key would let whichever renderer ran first silence the other's count.
/// </para>
/// </remarks>
public static class CommentNodes
{
    /// <summary>Whether this node is a comment and nothing else, so an output may omit it.</summary>
    /// <param name="node">The node.</param>
    /// <returns>Whether the node disappears when its comment is discarded.</returns>
    /// <remarks>
    /// A comment node that another contribution has given children or sequence items is not
    /// omitted: Section 17.1 keeps those, and only the comment text is unsupported here.
    /// </remarks>
    public static bool Vanishes(OverlayNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Payload is { IsValue: false }
            && !node.Marks.RendersAsMapping
            && !node.Marks.RendersAsSequence;
    }

    /// <summary>Reports the summarized Section 3 discard, if anything was discarded.</summary>
    /// <param name="diagnostics">The buffer the warning accumulates in.</param>
    /// <param name="anchor">The Section 22 <c>spec</c> anchor of the format being rendered.</param>
    /// <param name="destination">The Section 6.4.3 destination, which is the "output file" half.</param>
    /// <param name="discarded">How many comment nodes were discarded.</param>
    public static void Report(
        DiagnosticBuffer diagnostics,
        string anchor,
        DestinationRef? destination,
        int discarded)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (discarded == 0)
        {
            return;
        }

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Warn003(
                DiagnosticPhase.Planning,
                anchor,
                $"only XML renders comment nodes, so {discarded} XML comment(s) selected into this "
                + "output were discarded.",
                cardinalityKey: FlatIdentity.Key(destination?.Canonical, "comment-nodes"),
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));
    }
}
