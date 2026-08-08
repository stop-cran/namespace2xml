using System.Collections.Immutable;
using System.Globalization;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Inputs;

/// <summary>
/// Section 11.4's cross-contribution classification: reconciles the shapes several XML documents
/// give one element before they merge.
/// </summary>
/// <remarks>
/// <para>
/// "Mixedness and repeated-child classification are properties of the merged common-model element
/// and are evaluated at concrete merge time across all input contributions to that element."
/// <see cref="XmlInputReader"/> can only see one document, so it classifies each element from the
/// document that contains it. That is right whenever an element has one contributor and wrong the
/// moment it has two: an element-only <c>&lt;a&gt;&lt;b/&gt;&lt;/a&gt;</c> beside a mixed
/// <c>&lt;a&gt;t&lt;b/&gt;&lt;/a&gt;</c> addressed its child as <c>a.b</c> while the other
/// addressed the same child as <c>a.#1.b</c>, and the merged model then held one element at two
/// addresses.
/// </para>
/// <para>
/// Section 11.4 states the principle and Section 17.4 states the tests, so the tests are taken from
/// there: "the presence of any text or CDATA node makes the parent mixed-content"; "comments alone
/// do not make a parent mixed-content"; "for one expanded child name, if every source contribution
/// contains at most one occurrence, those singleton children deep-merge in source order"; "if any
/// source contribution contains more than one occurrence, every occurrence of that expanded name
/// forms one sequence".
/// </para>
/// <para>
/// The last two are why two documents that each supply one <c>&lt;b/&gt;</c> still overlay rather
/// than becoming a two-item sequence. Reading Section 11.4's "after the merged model contains
/// repeated <c>&lt;b&gt;</c> children" as counting across documents would make overlaying a base
/// document with an override — the thing this tool exists to do — append instead of override, and
/// Section 17.4 settles it by making the occurrence count a property of a single contribution.
/// </para>
/// <para>
/// Section 11.4 requires this before addresses are exposed, because it also requires that
/// "addresses are exposed before wildcard evaluation and never recomputed for an output view". The
/// pass therefore runs on the native shapes, ahead of the projection into overlay paths, and the
/// promotion it performs is exactly the one Section 11.4 describes: "a singleton <c>&lt;b&gt;</c>
/// is addressed as <c>a.b</c>; after the merged model contains repeated <c>&lt;b&gt;</c> children,
/// their canonical paths are <c>a.b.&lt;ordering-value&gt;</c> and the former singleton path no
/// longer names a scalar or element."
/// </para>
/// </remarks>
internal static class XmlClassification
{
    /// <summary>Reconciles the classification of every element several documents share.</summary>
    /// <param name="documents">The XML documents of one run, in source order.</param>
    /// <returns>
    /// The documents, each rewritten into the merged classification. A document already in that
    /// shape is returned unchanged, so a run whose documents agree allocates nothing.
    /// </returns>
    public static ImmutableArray<StructuredNode> Reconcile(ImmutableArray<StructuredNode> documents)
    {
        if (documents.Length < 2)
        {
            return documents;
        }

        var root = new Facts();

        foreach (var document in documents)
        {
            Survey(document, root);
        }

        return [.. documents.Select(document => Rewrite(document, root))];
    }

    /// <summary>What every contribution to one element, taken together, makes that element.</summary>
    private sealed class Facts
    {
        private readonly Dictionary<NamePart, Facts> children = [];

        /// <summary>Whether any contribution gave this element a text or CDATA node.</summary>
        public bool Mixed { get; set; }

        /// <summary>The child names some one contribution supplied more than once.</summary>
        public HashSet<NamePart> Sequences { get; } = [];

        /// <summary>
        /// The next content-token ordering value a converted contribution may allocate here.
        /// </summary>
        /// <remarks>
        /// Section 11.4 assigns content tokens "while concrete XML contributions merge, using
        /// Section 5.4", which is the high-water allocator, and Section 17.4 settles what that has
        /// to mean across contributions: "child elements in mixed content do not deep-merge with
        /// elements from another contribution". Reusing the ordinals an element-only document
        /// assigned for sibling ordering alone would instead land its first child on top of another
        /// document's first text node. Only a converted contribution allocates — a document that
        /// was already mixed keeps the tokens it wrote, so overlaying one mixed document with
        /// another is unaffected.
        /// </remarks>
        public long Next { get; private set; }

        /// <summary>Reserves the ordering values a contribution already occupies here.</summary>
        /// <param name="token">One occupied content-token ordering value.</param>
        public void Occupy(long token)
        {
            if (token >= Next)
            {
                Next = token + 1;
            }
        }

        /// <summary>Allocates the next free content-token ordering value here.</summary>
        public long Allocate() => Next++;

        /// <summary>The facts about one child name, created on first mention.</summary>
        /// <param name="name">The expanded child name.</param>
        public Facts Child(NamePart name)
        {
            if (!children.TryGetValue(name, out var child))
            {
                child = new Facts();
                children[name] = child;
            }

            return child;
        }

        /// <summary>The facts about one child name, or an empty set when it has none.</summary>
        /// <param name="name">The expanded child name.</param>
        public Facts Known(NamePart name) =>
            children.TryGetValue(name, out var child) ? child : Empty;

        private static Facts Empty { get; } = new();
    }

    private static void Survey(StructuredNode node, Facts facts)
    {
        switch (node)
        {
            case StructuredSequence sequence:
                foreach (var item in sequence.Items)
                {
                    Survey(item, facts);
                }

                break;

            case StructuredMapping mapping:
                SurveyMapping(mapping, facts);
                break;

            default:
                break;
        }
    }

    private static void SurveyMapping(StructuredMapping mapping, Facts facts)
    {
        foreach (var property in mapping.Properties)
        {
            switch (property.Name)
            {
                // Section 11.4 addresses an attribute as '@name' rather than as content, so it
                // neither makes a parent mixed nor takes part in child classification.
                case AttributePart:
                    break;

                case ContentPart when Wrapped(property.Value) is { } inner:
                    facts.Mixed = true;
                    facts.Occupy(((ContentPart)property.Name).Ordinal);
                    Survey(inner.Value, facts.Child(inner.Name));
                    break;

                case ContentPart content:
                    if (!IsComment(property.Value))
                    {
                        facts.Mixed = true;
                        facts.Occupy(content.Ordinal);
                    }

                    break;

                default:
                    if (property.Value is StructuredSequence)
                    {
                        facts.Sequences.Add(property.Name);
                    }

                    Survey(property.Value, facts.Child(property.Name));
                    break;
            }
        }
    }

    private static StructuredNode Rewrite(StructuredNode node, Facts facts)
    {
        switch (node)
        {
            case StructuredSequence sequence:
                {
                    var items = ImmutableArray.CreateBuilder<StructuredNode>(sequence.Items.Length);
                    var changed = false;

                    foreach (var item in sequence.Items)
                    {
                        var rewritten = Rewrite(item, facts);
                        changed |= !ReferenceEquals(rewritten, item);
                        items.Add(rewritten);
                    }

                    return changed ? sequence with { Items = items.ToImmutable() } : sequence;
                }

            case StructuredMapping mapping:
                return RewriteMapping(mapping, facts);

            default:
                return node;
        }
    }

    private static StructuredMapping RewriteMapping(StructuredMapping mapping, Facts facts)
    {
        if (facts.Mixed && !IsMixedShape(mapping))
        {
            return Convert(mapping, facts);
        }

        var properties = ImmutableArray.CreateBuilder<StructuredProperty>(mapping.Properties.Length);
        var changed = false;

        foreach (var property in mapping.Properties)
        {
            changed |= RewriteProperty(property, facts, properties);
        }

        return changed
            ? new StructuredMapping(properties.ToImmutable(), mapping.Line, mapping.Column)
            {
                Comments = mapping.Comments,
                ContentToken = mapping.ContentToken,
                Scalar = mapping.Scalar,
            }
            : mapping;
    }

    /// <summary>Re-addresses an element-only element as the mixed content the merge makes it.</summary>
    /// <param name="mapping">The element as its own document wrote it.</param>
    /// <param name="facts">The merged facts about it.</param>
    /// <returns>The same element in the Section 11.3 <c>#n</c> shape.</returns>
    /// <remarks>
    /// Every content node this contribution holds — its comments, its children, and the text it
    /// exposed at its own path — is re-allocated together, in the document order its own tokens
    /// record, so that the relative order it wrote survives the rebase. An element that exposes a
    /// scalar at its own path has no child elements, and nothing records where its text sat
    /// relative to a comment beside it, so the text is placed first.
    /// </remarks>
    private static StructuredMapping Convert(StructuredMapping mapping, Facts facts)
    {
        var attributes = ImmutableArray.CreateBuilder<StructuredProperty>();
        var content = new List<(long Order, Func<long, StructuredProperty> Place)>();

        if (mapping.Scalar is { } scalar)
        {
            content.Add((-1, token => Content(token, scalar with { ContentToken = token })));
        }

        foreach (var property in mapping.Properties)
        {
            switch (property.Name)
            {
                case AttributePart:
                    attributes.Add(property);
                    break;

                case ContentPart comment:
                    content.Add((
                        comment.Ordinal,
                        token => Content(token, property.Value with { ContentToken = token })));
                    break;

                default:
                    {
                        var value = Rewrite(property.Value, facts.Known(property.Name));

                        foreach (var occurrence in Occurrences(value))
                        {
                            content.Add((
                                occurrence.ContentToken ?? 0,
                                token => Content(
                                    token,
                                    new StructuredMapping(
                                        [property with { Value = occurrence }],
                                        occurrence.Line,
                                        occurrence.Column)
                                    {
                                        ContentToken = token,
                                    })));
                        }

                        break;
                    }
            }
        }

        return new StructuredMapping(
            [
                .. attributes,
                .. content.OrderBy(item => item.Order).Select(item => item.Place(facts.Allocate())),
            ],
            mapping.Line,
            mapping.Column)
        {
            Comments = mapping.Comments,
            ContentToken = mapping.ContentToken,
        };
    }

    /// <summary>Rewrites one property of an element whose shape the merge leaves alone.</summary>
    /// <param name="property">The property as its own document wrote it.</param>
    /// <param name="facts">The merged facts about the element that owns it.</param>
    /// <param name="properties">The property list being built.</param>
    /// <returns>Whether the rewrite changed anything.</returns>
    private static bool RewriteProperty(
        StructuredProperty property,
        Facts facts,
        ImmutableArray<StructuredProperty>.Builder properties)
    {
        switch (property.Name)
        {
            case AttributePart:
                properties.Add(property);
                return false;

            case ContentPart when Wrapped(property.Value) is { } inner:
                {
                    var value = Rewrite(inner.Value, facts.Known(inner.Name));

                    if (ReferenceEquals(value, inner.Value))
                    {
                        properties.Add(property);
                        return false;
                    }

                    properties.Add(property with
                    {
                        Value = new StructuredMapping(
                            [inner with { Value = value }],
                            property.Value.Line,
                            property.Value.Column)
                        {
                            ContentToken = property.Value.ContentToken,
                        },
                    });

                    return true;
                }

            // A text, CDATA, or comment node already sits at its own content token and owns no
            // children, so nothing about it can be reclassified.
            case ContentPart:
                properties.Add(property);
                return false;

            default:
                return RewriteChild(property, facts, properties);
        }
    }

    private static bool RewriteChild(
        StructuredProperty property,
        Facts facts,
        ImmutableArray<StructuredProperty>.Builder properties)
    {
        var value = Rewrite(property.Value, facts.Known(property.Name));

        if (facts.Sequences.Contains(property.Name) && value is not StructuredSequence)
        {
            // Section 11.4: "a singleton <b> is addressed as a.b; after the merged model contains
            // repeated <b> children, their canonical paths are a.b.<ordering-value> and the former
            // singleton path no longer names a scalar or element". The singleton becomes an item
            // rather than staying beside the sequence, because Section 11.4 also forbids retargeting
            // 'a.b' to the first repeated child.
            properties.Add(property with
            {
                Value = new StructuredSequence([value], value.Line, value.Column),
            });

            return true;
        }

        if (ReferenceEquals(value, property.Value))
        {
            properties.Add(property);
            return false;
        }

        properties.Add(property with { Value = value });
        return true;
    }

    /// <summary>The child elements one name-keyed property holds, in source order.</summary>
    /// <param name="value">The property's value.</param>
    private static ImmutableArray<StructuredNode> Occurrences(StructuredNode value) =>
        value is StructuredSequence sequence ? sequence.Items : [value];

    private static StructuredProperty Content(long token, StructuredNode value) =>
        new(
            "#" + token.ToString(CultureInfo.InvariantCulture),
            new ContentPart(token),
            value,
            value.Line,
            value.Column);

    /// <summary>The child element a mixed-content <c>#n</c> wraps, or null when it holds a scalar.</summary>
    /// <param name="node">The value at the content token.</param>
    private static StructuredProperty? Wrapped(StructuredNode node) =>
        node is StructuredMapping { Properties: [{ Name: not ContentPart } single] } ? single : null;

    /// <summary>Whether a value at a content token is a Section 11.5 comment node.</summary>
    /// <param name="node">The value at the content token.</param>
    private static bool IsComment(StructuredNode node) =>
        node is StructuredScalar { Payload: { IsValue: false } };

    /// <summary>
    /// Whether this element is already addressed as mixed content, which Section 17.4 makes the
    /// presence of a text or CDATA node rather than of any content token: an element-only element
    /// holding a comment has one too.
    /// </summary>
    /// <param name="mapping">The element.</param>
    private static bool IsMixedShape(StructuredMapping mapping) =>
        mapping.Properties.Any(
            property => property.Name is ContentPart && !IsComment(property.Value));
}
