using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Output;

/// <summary>
/// Projects an output view into the ordered document Section 19.3 and Section 19.4 render.
/// </summary>
/// <remarks>
/// <para>
/// JSON and YAML are the Section 4.4 destinations that "require one exclusive shape", so unlike the
/// flat formats the payload competes with the container here: Section 4.4's own example says JSON
/// and YAML "render <c>x</c> as an object containing <c>z</c>, omit scalar <c>1</c>, and warn".
/// </para>
/// <para>
/// Section 4.4 step 4 allows exactly one warning per node, and <c>TYPE002</c> is counted "once per
/// path and output instance", so a node whose payload loses to a mapping that also lost a sequence
/// produces one diagnostic naming both omissions rather than two the buffer would collapse into
/// one.
/// </para>
/// </remarks>
public sealed class DocumentProjection
{
    private readonly DiagnosticBuffer diagnostics;
    private readonly string anchor;
    private readonly DestinationRef? destination;

    /// <summary>Creates a projection.</summary>
    /// <param name="diagnostics">The buffer shape-conflict warnings accumulate in.</param>
    /// <param name="anchor">
    /// The Section 22 <c>spec</c> anchor of the format being rendered, which is the clause a reader
    /// must consult to see which facet this destination keeps.
    /// </param>
    /// <param name="destination">
    /// The Section 6.4.3 <c>destination</c> this output instance writes to, which is half of the
    /// "once per path and output instance" cardinality of <c>TYPE002</c>.
    /// </param>
    public DocumentProjection(DiagnosticBuffer diagnostics, string anchor, DestinationRef? destination = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.diagnostics = diagnostics;
        this.anchor = anchor;
        this.destination = destination;
    }

    /// <summary>Projects a view.</summary>
    /// <param name="view">The selected output view.</param>
    /// <param name="root">
    /// The Section 16.3 root parts. Section 16.3 says <c>root=x.y</c> makes JSON emit
    /// <c>{"x":{"y":...}}</c>, so these wrap the document in nested single-member mappings rather
    /// than prefixing a key.
    /// </param>
    /// <remarks>
    /// The Section 22 destination fold applies the root to the view before publication, so a view
    /// arriving from the pipeline is already wrapped and carries no root parts. Wrapping here keeps
    /// the projection correct for a view that has not been through that fold.
    /// </remarks>
    /// <returns>The document root.</returns>
    public DocumentNode Project(OverlayNode view, ImmutableArray<NamePart> root)
    {
        ArgumentNullException.ThrowIfNull(view);

        var document = Visit(view, []);

        for (var index = root.Length - 1; index >= 0; index--)
        {
            document = new DocumentMapping(
                [new DocumentMember(StructuredKey.Of(root[index]), document)],
                []);
        }

        return document;
    }

    private DocumentNode Visit(OverlayNode node, ImmutableArray<NamePart> path)
    {
        var comments = ImmutableArray.CreateRange(node.OrderedComments);
        var marks = node.Marks;

        Report(marks, path);

        if (marks.RendersAsSequence)
        {
            var items = ImmutableArray.CreateBuilder<DocumentNode>();

            foreach (var (value, item) in node.OrderedSequence)
            {
                items.Add(Visit(item.Node, path.Add(OrderingValues.ToNamePart(value))));
            }

            return new DocumentSequence(items.ToImmutable(), comments);
        }

        if (marks.RendersAsScalar && node.Payload is { } payload)
        {
            return new DocumentScalar(payload, comments);
        }

        // Section 14.1: an output view with nothing in it emits "an empty mapping". A node with no
        // payload and no container shape is the same case one level down, and a node whose mapping
        // won the Section 4.4 contest renders its children, so both arrive here.
        var members = ImmutableArray.CreateBuilder<DocumentMember>();

        if (marks.RendersAsMapping)
        {
            foreach (var (name, child) in node.OrderedChildren)
            {
                members.Add(new DocumentMember(
                    StructuredKey.Of(name),
                    Visit(child, path.Add(name))));
            }
        }

        return new DocumentMapping(members.ToImmutable(), comments);
    }

    private void Report(NodeMarks marks, ImmutableArray<NamePart> path)
    {
        var omitted = new List<string>();

        if (marks.RendersAsContainer && marks.PayloadMark is not null)
        {
            omitted.Add("the scalar");
        }

        if (marks.HasBothContainers || marks.RendersAsScalar)
        {
            if (marks.MappingShape is not null && !marks.RendersAsMapping)
            {
                omitted.Add("the mapping children");
            }

            if (marks.SequenceShape is not null && !marks.RendersAsSequence)
            {
                omitted.Add("the sequence items");
            }
        }

        if (omitted.Count == 0)
        {
            return;
        }

        var text = FlatIdentity.PathText(path);

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Type002(
                DiagnosticPhase.Planning,
                anchor,
                "this path supplies more shapes than a JSON or YAML node can hold, and Section 4.4 "
                + "renders only the latest contribution, so this output omits "
                + string.Join(" and ", omitted)
                + " here.",
                cardinalityKey: FlatIdentity.Key(destination?.Canonical, text),
                path: text,
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));
    }
}
