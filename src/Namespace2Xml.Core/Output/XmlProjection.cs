using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scheme;

namespace Namespace2Xml.Output;

/// <summary>
/// Projects one output view into the Section 19.5 XML document.
/// </summary>
/// <remarks>
/// <para>
/// XML is the one output format Section 4.4's exclusive shape does not describe: an element holds a
/// payload and children at once, which is mixed content rather than a contest. The projection is
/// therefore its own pass rather than a rendering of <see cref="DocumentNode"/>.
/// </para>
/// <para>
/// The tree is built as LINQ to XML because <see cref="XmlSerializer"/> writes it through
/// <c>XmlWriter</c>, whose settings are what Section 16.9's option names describe.
/// </para>
/// </remarks>
public sealed class XmlProjection
{
    private readonly DiagnosticBuffer diagnostics;
    private readonly DestinationRef? destination;
    private readonly IReadOnlyDictionary<string, EffectiveTransform> types;
    private readonly int wrapper;
    private readonly bool preservesCData;

    /// <summary>Creates a projection.</summary>
    /// <param name="diagnostics">The buffer a refusal accumulates in.</param>
    /// <param name="destination">The destination being rendered, for diagnostic attribution.</param>
    /// <param name="types">
    /// The Section 15.2 bound transform table, keyed by canonical path relative to the selector
    /// prefix, from which Section 16.6's XML node kinds are read.
    /// </param>
    /// <param name="wrapper">
    /// How many leading components of the view are Section 16.3 <c>root</c> wrapping rather than
    /// selected content. No scheme path addresses them, so they are removed before a lookup.
    /// </param>
    /// <param name="options">The Section 16.9 options this destination selected.</param>
    public XmlProjection(
        DiagnosticBuffer diagnostics,
        DestinationRef? destination = null,
        IReadOnlyDictionary<string, EffectiveTransform>? types = null,
        int wrapper = 0,
        XmlOutputOptions options = XmlOutput.Default)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.diagnostics = diagnostics;
        this.destination = destination;
        this.types = types ?? ImmutableDictionary<string, EffectiveTransform>.Empty;
        this.wrapper = wrapper;
        preservesCData = options.PreservesCData();
    }

    /// <summary>Projects one view, or null when Section 14.1 refuses it.</summary>
    /// <param name="view">The selected output view.</param>
    /// <param name="root">The Section 16.3 root parts, empty when undeclared.</param>
    /// <returns>The document element, or null when a blocking type error was raised.</returns>
    public XElement? Project(OverlayNode view, ImmutableArray<NamePart> root)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (view.Marks.RendersAsSequence)
        {
            return ProjectRootSequence(view, root);
        }

        if (root.IsDefaultOrEmpty)
        {
            return ProjectImplicitRoot(view);
        }

        // Section 16.3: "root=x.y ... XML emits <x><y>...</y></x>". The innermost root part owns
        // the selected content and every outer part wraps it.
        var element = NewElement(root[^1]);

        if (element is null)
        {
            return null;
        }

        if (!TryFill(element, view, []))
        {
            return null;
        }

        for (var index = root.Length - 2; index >= 0; index--)
        {
            var outer = NewElement(root[index]);

            if (outer is null)
            {
                return null;
            }

            outer.Add(element);
            element = outer;
        }

        return element;
    }

    private XElement? ProjectImplicitRoot(OverlayNode view)
    {
        // Section 19.5 "emits one document element", and Section 14.1 requires 'root' whenever the
        // view cannot supply one: an empty selection and a bare scalar are both named there.
        if (view.Payload is not null && !view.Marks.RendersAsMapping)
        {
            return RootRequired("the selected value is a bare scalar, so no element names it");
        }

        var children = view.Marks.RendersAsMapping
            ? view.OrderedChildren.ToList()
            : [];

        if (children.Count == 0)
        {
            return RootRequired("the selected view is empty, so no element names it");
        }

        if (children.Count > 1)
        {
            return RootRequired(
                $"the selected view has {children.Count} top-level members and XML has one "
                + "document element");
        }

        var (name, child) = children[0];
        var element = NewElement(name);

        if (element is null)
        {
            return null;
        }

        return TryFill(element, child, [name]) ? element : null;
    }

    private XElement? ProjectRootSequence(OverlayNode view, ImmutableArray<NamePart> root)
    {
        // Section 19.5: "An output view whose document root is itself a sequence requires 'root'
        // with at least two element components ... Without such a root, or with only one root
        // component, rendering is TYPE001."
        if (root.Length < 2)
        {
            return RootRequired(
                "the selected value is a sequence, and Section 19.5 needs one root component to "
                + "wrap it and another to name each repeated item");
        }

        var wrapper = NewElement(root[^2]);

        if (wrapper is null)
        {
            return null;
        }

        if (!TryAddSequence(wrapper, root[^1], view, []))
        {
            return null;
        }

        for (var index = root.Length - 3; index >= 0; index--)
        {
            var outer = NewElement(root[index]);

            if (outer is null)
            {
                return null;
            }

            outer.Add(wrapper);
            wrapper = outer;
        }

        return wrapper;
    }

    /// <summary>Fills one element from the node the caller has already named.</summary>
    private bool TryFill(XElement element, OverlayNode node, ImmutableArray<NamePart> path)
    {
        foreach (var comment in node.OrderedComments)
        {
            element.Add(new XComment(comment.Text));
        }

        var kind = Kind(path);

        if (kind is TypeValue.Attribute)
        {
            // Section 16.6: attribute "is invalid where no containing element exists". Every other
            // path reaches the typed branch through TryAddChild, which has a parent to attach to;
            // a node whose own element is the one being filled is the document element or a
            // sequence item, and neither has one.
            return Refuse(path, "'type=attribute' is invalid where no containing element exists");
        }

        if (node.Marks.RendersAsSequence)
        {
            // A sequence reached here is a sequence-valued mapping child whose repeated siblings
            // the caller could not emit, because only the parent can hold repeated names.
            return Refuse(
                path,
                "a sequence is projected as repeated sibling elements, and this one has no parent "
                + "element to repeat within");
        }

        if (node.Payload is not { } payload)
        {
            if (kind is TypeValue.Text or TypeValue.Cdata)
            {
                return Refuse(
                    path,
                    $"'type={TypeSet.Spell(kind.Value)}' renders a scalar, and this path supplies "
                    + "none");
            }
        }
        else if (SpelledAsComment(kind, payload))
        {
            element.Add(new XComment(Text(payload)));
        }
        else if (SpelledAsCdata(kind, payload))
        {
            element.Add(new XCData(Text(payload)));
        }
        else
        {
            element.Add(new XText(Text(payload)));
        }

        if (!node.Marks.RendersAsMapping)
        {
            return true;
        }

        foreach (var unit in Placed(node, path))
        {
            var placed = unit.IsItem
                ? TryAddItem(element, unit.Name, unit.Node, unit.Path)
                : TryAddChild(element, unit.Name, unit.Node, unit.Path);

            if (!placed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// One element's children in the Section 11.4 order their content tokens fix, with a
    /// sequence expanded into the repeated siblings it renders as.
    /// </summary>
    /// <param name="node">The element's node.</param>
    /// <param name="path">The path naming the element.</param>
    /// <remarks>
    /// <para>
    /// Section 11.4: content-token values "determine placement in the parent's serialized stream".
    /// Mapping order alone cannot: repeated same-name children "form a sequence at
    /// <c>parent.child</c>", and a sequence occupies one place among its siblings while its items
    /// occupy several, so
    /// <c>&lt;a&gt;&lt;b&gt;1&lt;/b&gt;&lt;c&gt;2&lt;/c&gt;&lt;b&gt;3&lt;/b&gt;&lt;/a&gt;</c> would
    /// otherwise emit both <c>b</c> children before <c>c</c>. The sequence is therefore expanded
    /// here, so that each item is placed by the token it carries rather than by the token its
    /// sequence would have.
    /// </para>
    /// <para>
    /// A sequence carrying a Section 16.6 <c>type</c> directive is left whole, because that
    /// directive is written at the sequence's own path and decides what the sequence renders as
    /// before its items are placed anywhere. Only a sequence the XML reader built out of repeated
    /// children has tokens on its items at all, so nothing from another format is expanded.
    /// </para>
    /// <para>
    /// A child with no token keeps its Section 5.2 place after every child that has one. Only the
    /// XML reader assigns tokens, so this is the order in which a contribution from another format
    /// reaches an XML destination, and the sort is stable to leave those relative positions exactly
    /// as mapping order had them. Attributes are never tokened and are unaffected: an
    /// <see cref="XAttribute"/> joins a different chain of the element than an
    /// <see cref="XElement"/> does, so their order among themselves is all that is observable.
    /// </para>
    /// </remarks>
    private IEnumerable<Unit> Placed(OverlayNode node, ImmutableArray<NamePart> path)
    {
        var units = new List<(long Key, Unit Unit)>();

        foreach (var (name, child) in node.OrderedChildren)
        {
            var childPath = path.Add(name);

            if (child.Marks.RendersAsSequence
                && Kind(childPath) is null
                && child.OrderedSequence.Any(item => item.Value.Node.Marks.ContentToken is not null))
            {
                foreach (var (value, item) in child.OrderedSequence)
                {
                    units.Add((
                        item.Node.Marks.ContentToken ?? long.MaxValue,
                        new Unit(
                            name,
                            item.Node,
                            childPath.Add(OrderingValues.ToNamePart(value)),
                            IsItem: true)));
                }

                continue;
            }

            units.Add((
                child.Marks.ContentToken ?? long.MaxValue,
                new Unit(name, child, childPath, IsItem: false)));
        }

        return units.OrderBy(unit => unit.Key).Select(unit => unit.Unit);
    }

    /// <summary>One node to place in an element's content, and how to place it.</summary>
    /// <param name="Name">The name it is emitted under.</param>
    /// <param name="Node">Its overlay node.</param>
    /// <param name="Path">The path naming it.</param>
    /// <param name="IsItem">Whether it is one item of a sequence rendered as repeated siblings.</param>
    private readonly record struct Unit(
        NamePart Name, OverlayNode Node, ImmutableArray<NamePart> Path, bool IsItem);

    private bool TryAddChild(
        XElement parent,
        NamePart name,
        OverlayNode child,
        ImmutableArray<NamePart> path)
    {
        // Section 16.6's four XML kinds "render a scalar" as one node kind, so they decide this
        // child's spelling before its address does. Section 15.1 step 16 places them after
        // `type=ignore` and before the container types, which is where the passes already ran;
        // nothing here changes the shape of the view.
        if (Kind(path) is { } kind)
        {
            return TryAddTyped(parent, name, child, path, kind);
        }

        switch (name)
        {
            case AttributePart attribute:
                return TryAddAttribute(parent, attribute, child, path);

            case ContentPart:
                // Section 11.4 gives every text run, CDATA run, comment, and mixed-content child
                // element its own content token, so the token is a position rather than a name and
                // contributes its contents directly to the owning element.
                return TryFill(parent, child, path);

            default:
                if (child.Marks.RendersAsSequence)
                {
                    return TryAddSequence(parent, name, child, path);
                }

                var element = NewElement(name);

                if (element is null)
                {
                    return false;
                }

                parent.Add(element);

                return TryFill(element, child, path);
        }
    }

    /// <summary>Whether one payload is written as a CDATA section.</summary>
    /// <param name="kind">The Section 16.6 kind effective at its path, if any.</param>
    /// <param name="payload">The payload.</param>
    /// <remarks>
    /// Section 11.6: "XML output must preserve imported CDATA as CDATA unless an output option
    /// requests conversion to ordinary text". An explicit <c>type</c> directive is the stronger
    /// statement of the two, so it decides first in both directions — <c>type=cdata</c> spells a
    /// payload that arrived as text, and <c>type=text</c> spells one that arrived as CDATA.
    /// </remarks>
    private bool SpelledAsCdata(TypeValue? kind, ScalarPayload payload) => kind switch
    {
        TypeValue.Cdata => true,
        TypeValue.Text => false,
        _ => preservesCData && payload.Spelling is XmlContentSpelling.Cdata,
    };

    /// <summary>Whether Section 11.5 renders this payload as a comment node.</summary>
    /// <param name="kind">The Section 16.6 kind effective at its path, if any.</param>
    /// <param name="payload">The payload.</param>
    /// <remarks>
    /// Section 19.5 "emits retained XML comments", and Section 11.4 lets a comment "be selected for
    /// ignore and conversion through <c>#n</c>". Conversion is the <c>type</c> directive, so naming
    /// any Section 16.6 kind at a comment's path renders it as that kind instead — <c>type=text</c>
    /// turns the comment into the text node it sits beside. Only an unqualified comment stays one.
    /// </remarks>
    private static bool SpelledAsComment(TypeValue? kind, ScalarPayload payload) =>
        kind is null && payload.Spelling is XmlContentSpelling.Comment;

    /// <summary>The Section 16.6 XML node kind effective at one path, or null when none is.</summary>
    private TypeValue? Kind(ImmutableArray<NamePart> path)
    {
        if (path.Length < wrapper)
        {
            return null;
        }

        var key = CanonicalPath.Of(path[wrapper..]) ?? string.Empty;

        return ViewTransformer.At(types, key).Types?.XmlKind;
    }

    /// <summary>Renders one child under an explicit Section 16.6 XML node kind.</summary>
    private bool TryAddTyped(
        XElement parent,
        NamePart name,
        OverlayNode child,
        ImmutableArray<NamePart> path,
        TypeValue kind)
    {
        if (kind is TypeValue.Element)
        {
            // Section 16.6: "renders a scalar as an element", which is already the default for a
            // named child. Written on an attribute it converts one, so the attribute marker is
            // dropped and its own name becomes the element name.
            var promoted = name is AttributePart attribute ? attribute.Name : name;

            if (name is ContentPart)
            {
                return Refuse(
                    path,
                    "'type=element' needs a name for the element, and a content token is a "
                    + "position rather than a name");
            }

            if (child.Marks.RendersAsSequence)
            {
                return TryAddSequence(parent, promoted, child, path);
            }

            var element = NewElement(promoted);

            if (element is null)
            {
                return false;
            }

            parent.Add(element);

            return TryFill(element, child, path);
        }

        if (child.Marks.RendersAsSequence)
        {
            return Refuse(
                path,
                $"'type={TypeSet.Spell(kind)}' renders one scalar, and this path is a sequence");
        }

        if (child.Payload is not { } payload)
        {
            return Refuse(
                path,
                $"'type={TypeSet.Spell(kind)}' renders a scalar, and this path supplies none");
        }

        switch (kind)
        {
            case TypeValue.Attribute:
                if (name is ContentPart)
                {
                    return Refuse(
                        path,
                        "'type=attribute' needs a name for the attribute, and a content token is a "
                        + "position rather than a name");
                }

                var attributeName = name is AttributePart marked ? marked.Name : name;

                if (!TryName(attributeName, out var qualified))
                {
                    RefuseName(attributeName, "attribute");
                    return false;
                }

                parent.Add(new XAttribute(qualified!, Text(payload)));
                return true;

            case TypeValue.Cdata:
                // Section 16.6: "renders a scalar as CDATA". The name is not emitted, because a
                // CDATA section is content of the containing element rather than a node with one.
                parent.Add(new XCData(Text(payload)));
                return true;

            default:
                parent.Add(new XText(Text(payload)));
                return true;
        }
    }

    private bool TryAddAttribute(
        XElement parent,
        AttributePart name,
        OverlayNode child,
        ImmutableArray<NamePart> path)
    {
        if (child.Marks.RendersAsSequence)
        {
            // Section 19.5: "attribute is TYPE001 because one XML attribute cannot represent
            // repeated values."
            return Refuse(path, "one XML attribute cannot represent a sequence of values");
        }

        if (child.Payload is not { } payload)
        {
            return Refuse(path, "an XML attribute holds a scalar, and this path supplies none");
        }

        if (!TryName(name.Name, out var qualified))
        {
            RefuseName(name.Name, "attribute");
            return false;
        }

        parent.Add(new XAttribute(qualified!, Text(payload)));

        return true;
    }

    private bool TryAddSequence(
        XElement parent,
        NamePart name,
        OverlayNode sequence,
        ImmutableArray<NamePart> path)
    {
        // Section 19.5: "A sequence-valued mapping child therefore renders as repeated sibling
        // elements whose expanded name is the sequence path's final element component."
        foreach (var (value, item) in sequence.OrderedSequence)
        {
            if (!TryAddItem(
                parent, name, item.Node, path.Add(OrderingValues.ToNamePart(value))))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Emits one item of a sequence as a sibling element.</summary>
    /// <param name="parent">The element the sibling joins.</param>
    /// <param name="name">The sequence path's final element component.</param>
    /// <param name="item">The item's node.</param>
    /// <param name="path">The path naming the item, ordering value included.</param>
    private bool TryAddItem(
        XElement parent, NamePart name, OverlayNode item, ImmutableArray<NamePart> path)
    {
        var element = NewElement(name);

        if (element is null)
        {
            return false;
        }

        parent.Add(element);

        return TryFill(element, item, path);
    }

    private XElement? NewElement(NamePart part)
    {
        if (TryName(part, out var name))
        {
            return new XElement(name!);
        }

        RefuseName(part, "element");

        return null;
    }

    /// <summary>
    /// The expanded name of one component, or false when it has no XML name spelling.
    /// </summary>
    /// <remarks>
    /// A JSON or YAML key is "one ordinary literal component" under Section 8.2 and acquires no XML
    /// node kind, so its text is whatever the source document wrote — including text that is not an
    /// NCName. Section 11.2 admits only names XML admits, so such a key has no element or attribute
    /// spelling and Section 22's <c>XML002</c> says exactly that. Deciding it by construction
    /// rather than by catching the writer's exception keeps Section 6.3's rule that a user-caused
    /// error never escapes as an unhandled exception.
    /// </remarks>
    private static bool TryName(NamePart part, out XName? name)
    {
        switch (part)
        {
            case QualifiedElementPart qualified
                when IsName(StructuredKey.Of(new OrdinaryPart(qualified.Local)), out var local):
                name = XNamespace.Get(qualified.Uri) + local;
                return true;

            case OrdinaryPart ordinary when IsName(StructuredKey.Of(ordinary), out var local):
                name = XName.Get(local);
                return true;

            default:
                name = null;
                return false;
        }
    }

    private static bool IsName(string text, out string verified)
    {
        verified = text;

        if (text.Length == 0)
        {
            return false;
        }

        try
        {
            XmlConvert.VerifyNCName(text);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static string Text(ScalarPayload payload) =>
        payload.IsNull ? "null" : payload.ToCanonicalText();

    private void RefuseName(NamePart part, string role)
    {
        var text = FlatIdentity.PathText([part]);

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Xml002(
                DiagnosticPhase.Planning,
                "\u00A711.2",
                $"'{StructuredKey.Of(part)}' is not usable as an XML {role} name: Section 11.2 "
                + "admits the names XML admits, and a JSON or YAML key is an ordinary literal "
                + "component that need not be one.",
                cardinalityKey: FlatIdentity.Key(destination?.Canonical, text),
                path: text),
            DestinationOrder: destination?.Order));
    }

    private XElement? RootRequired(string because)
    {
        // Section 14.1: "XML requires 'root' to provide a document element and otherwise raises
        // TYPE001."
        Report($"{because}, so Section 14.1 requires an explicit 'root'.", []);

        return null;
    }

    private bool Refuse(ImmutableArray<NamePart> path, string because)
    {
        Report($"{because}.", path);

        return false;
    }

    private void Report(string message, ImmutableArray<NamePart> path)
    {
        // Section 16.3 wraps the selected content beneath names no scheme path can address, so the
        // wrapper is removed here too: a report naming it would invite a directive that binds
        // nowhere.
        var addressable = path.Length < wrapper ? [] : path[wrapper..];
        var text = FlatIdentity.PathText(addressable);

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Type001(
                DiagnosticPhase.Planning,
                "\u00A719.5",
                message,
                cardinalityKey: FlatIdentity.Key(destination?.Canonical, text),
                path: text,
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));
    }
}
