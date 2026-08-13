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
    private readonly IReadOnlyDictionary<string, EffectiveTransform> types;
    private readonly int wrapper;
    private int discardedComments;

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
    /// <param name="types">
    /// The Section 16.6 transforms bound to this output instance. Explicit scalar types are applied
    /// at serialization time by the format that has them, so the table is read here rather than
    /// having reshaped the view. Required rather than defaulted: an empty table is indistinguishable
    /// from a forgotten one at run time, and forgetting it is how <c>type=string</c> came to bind
    /// and then be ignored.
    /// </param>
    /// <param name="wrapper">
    /// The number of leading path parts a Section 16.3 <c>root</c> already wrapped the view in. The
    /// table is keyed by unwrapped paths, so these are stripped before a lookup.
    /// </param>
    public DocumentProjection(
        DiagnosticBuffer diagnostics,
        string anchor,
        IReadOnlyDictionary<string, EffectiveTransform> types,
        int wrapper,
        DestinationRef? destination = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(types);

        this.diagnostics = diagnostics;
        this.anchor = anchor;
        this.destination = destination;
        this.types = types;
        this.wrapper = wrapper;
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

        CommentNodes.Report(diagnostics, anchor, destination, discardedComments);

        return document;
    }

    private DocumentNode Visit(OverlayNode node, ImmutableArray<NamePart> path)
    {
        var comments = ImmutableArray.CreateRange(node.OrderedComments);
        var marks = node.Marks;

        // Section 4.4 resolves shape at this destination, and this destination discards Section
        // 11.5 comment nodes. A container whose every member is such a comment therefore has
        // nothing left to contest the scalar with, so the scalar wins and only the comment is
        // lost. Deciding before the discard instead would lose both: the node would render as an
        // empty mapping, and the value an XML comment happened to sit beside would be gone.
        if (marks.RendersAsContainer
            && !marks.HasBothContainers
            && node.Payload is { IsValue: true } surviving
            && EveryMemberIsADiscardedComment(node, marks))
        {
            DiscardCommentMembers(node, marks);

            return new DocumentScalar(ForcedString(path) ? AsString(surviving) : surviving, comments);
        }

        Report(marks, path);

        if (marks.RendersAsSequence)
        {
            var items = ImmutableArray.CreateBuilder<DocumentNode>();

            foreach (var (value, item) in node.OrderedSequence)
            {
                if (CommentNodes.Vanishes(item.Node))
                {
                    discardedComments++;
                    continue;
                }

                items.Add(Visit(item.Node, path.Add(OrderingValues.ToNamePart(value))));
            }

            return new DocumentSequence(items.ToImmutable(), comments);
        }

        if (node.Payload is { IsValue: false })
        {
            // A comment node another contribution has given children. Only the comment goes.
            discardedComments++;
        }

        if (marks.RendersAsScalar && node.Payload is { IsValue: true } payload)
        {
            return new DocumentScalar(ForcedString(path) ? AsString(payload) : payload, comments);
        }

        // Section 14.1: an output view with nothing in it emits "an empty mapping". A node with no
        // payload and no container shape is the same case one level down, and a node whose mapping
        // won the Section 4.4 contest renders its children, so both arrive here.
        var members = ImmutableArray.CreateBuilder<DocumentMember>();

        if (marks.RendersAsMapping)
        {
            var claimed = new Dictionary<string, ImmutableArray<NamePart>>(StringComparer.Ordinal);

            foreach (var (name, child) in node.OrderedChildren)
            {
                if (CommentNodes.Vanishes(child))
                {
                    discardedComments++;
                    continue;
                }

                var key = StructuredKey.Of(name);
                var here = path.Add(name);

                if (claimed.TryGetValue(key, out var first))
                {
                    ReportCollision(here, first, path, key);
                    continue;
                }

                claimed.Add(key, here);

                members.Add(new DocumentMember(key, Visit(child, here)));
            }
        }

        return new DocumentMapping(members.ToImmutable(), comments);
    }

    /// <summary>
    /// Whether this node's rendering container has members and every one of them is a comment
    /// this destination discards.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="marks">Its Section 4.4 marks.</param>
    /// <returns>Whether the container is left with nothing once the comments go.</returns>
    /// <remarks>
    /// A container with no members at all is excluded deliberately. Section 4.4 makes an empty
    /// mapping "participate in precedence even though it has no children", so an author who wrote
    /// one after a scalar meant it to win, and nothing about it is discarded here.
    /// </remarks>
    private static bool EveryMemberIsADiscardedComment(OverlayNode node, NodeMarks marks)
    {
        var any = false;

        if (marks.RendersAsSequence)
        {
            foreach (var (_, item) in node.OrderedSequence)
            {
                if (!CommentNodes.Vanishes(item.Node))
                {
                    return false;
                }

                any = true;
            }

            return any;
        }

        foreach (var (_, child) in node.OrderedChildren)
        {
            if (!CommentNodes.Vanishes(child))
            {
                return false;
            }

            any = true;
        }

        return any;
    }

    /// <summary>Counts the comment members this node loses when its scalar wins instead.</summary>
    /// <param name="node">The node.</param>
    /// <param name="marks">Its Section 4.4 marks.</param>
    private void DiscardCommentMembers(OverlayNode node, NodeMarks marks)
    {
        if (marks.RendersAsSequence)
        {
            foreach (var _ in node.OrderedSequence)
            {
                discardedComments++;
            }

            return;
        }

        foreach (var _ in node.OrderedChildren)
        {
            discardedComments++;
        }
    }

    /// <summary>
    /// Reports two distinct logical paths spelling one mapping key, which Section 19.3 forbids.
    /// </summary>
    /// <param name="here">The path of the losing child.</param>
    /// <param name="first">The path of the child that claimed the key.</param>
    /// <param name="parent">The path of the mapping both are members of.</param>
    /// <param name="key">The key text both spell.</param>
    /// <remarks>
    /// The cardinality slot carries the parent mapping's path as well as the key, because the same
    /// key text in two different mappings is two collisions and not one. Naming only the key would
    /// let the second be dropped as a duplicate of the first, which is the reverse of the failure
    /// this diagnostic exists to prevent.
    /// </remarks>
    private void ReportCollision(
        ImmutableArray<NamePart> here,
        ImmutableArray<NamePart> first,
        ImmutableArray<NamePart> parent,
        string key) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Flat001(
                DiagnosticPhase.Planning,
                anchor,
                $"'{FlatIdentity.PathText(here)}' and '{FlatIdentity.PathText(first)}' are distinct "
                + $"logical paths that both spell the mapping key '{key}': Section 19.3 forbids two "
                + "paths silently becoming one key.",
                cardinalityKey: FlatIdentity.Key(
                    destination?.Canonical,
                    $"{FlatIdentity.PathText(parent)}\u0000{key}"),
                path: FlatIdentity.PathText(here),
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));

    /// <summary>Whether Section 16.6 <c>type=string</c> is effective at one path.</summary>
    private bool ForcedString(ImmutableArray<NamePart> path)
    {
        if (path.Length < wrapper)
        {
            return false;
        }

        return ViewTransformer.At(types, CanonicalPath.Of(path[wrapper..]) ?? string.Empty)
            .Types is { IsString: true };
    }

    /// <summary>
    /// Section 16.6 <c>string</c>: "Forces scalar rendering as a string in the selected output
    /// view."
    /// </summary>
    /// <remarks>
    /// The same clause says the directive "does not change input scalar inference or the typed
    /// value forwarded through references", so this converts a payload that inference has already
    /// settled, at the one output view being rendered, and takes the Section 18 canonical text of
    /// that settled value. An author who writes <c>0755</c> and asks for a string gets
    /// <c>"755"</c>: the leading zero was gone before this point, and re-deriving it here would be
    /// the change to inference the clause forbids.
    ///
    /// Null is left alone. It has no canonical text of its own because Section 19 lets each format
    /// spell it differently, so forcing it to a string would be this pass choosing a spelling on
    /// every format's behalf. Section 16.6 does not say what a forced-string null should be, and
    /// inventing an answer here would be a decision disguised as an implementation detail.
    /// </remarks>
    private static ScalarPayload AsString(ScalarPayload payload) =>
        payload.IsNull ? payload : ScalarPayload.OfString(payload.ToCanonicalText());

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
