using System.Collections.Immutable;
using Namespace2Xml.Overlay;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Inputs;

/// <summary>
/// One node of a native structured document, in the three shapes Section 4.2 recognizes.
/// </summary>
/// <remarks>
/// <para>
/// JSON, YAML, and XML each have their own syntax and their own errors, and nothing about
/// projecting a mapping into Section 4.2 children or a sequence into Section 4.2 ordering values
/// differs between them. The format readers therefore stop at this shape, and
/// <see cref="StructuredProfileReader"/> is the one implementation of Section 15.1 steps 6 and 7
/// for all three. A rule that exists once cannot disagree with itself in two formats.
/// </para>
/// <para>
/// Names arrive already lexed, as a <see cref="NamePart"/>, because that is the one thing the
/// formats genuinely disagree about: Section 9.1 and Section 10.4 make a mapping key one literal
/// component, while Section 11.4 builds an XML address from a namespace URI, a local name, and an
/// attribute or content marker. Keeping the difference in the readers keeps it out of everything
/// downstream.
/// </para>
/// </remarks>
/// <param name="Line">The one-based line this node begins on.</param>
/// <param name="Column">The one-based Section 22 column this node begins at.</param>
public abstract record StructuredNode(int Line, int Column);

/// <summary>A native scalar.</summary>
public sealed record StructuredScalar : StructuredNode
{
    private StructuredScalar(ScalarPayload? payload, string? nativeString, int line, int column)
        : base(line, column)
    {
        Payload = payload;
        NativeString = nativeString;
    }

    /// <summary>
    /// The payload when the source format typed it, otherwise <see langword="null"/>.
    /// </summary>
    public ScalarPayload? Payload { get; }

    /// <summary>
    /// The decoded text of a native string, which Section 9.1 sends through the value lexer,
    /// otherwise <see langword="null"/>.
    /// </summary>
    public string? NativeString { get; }

    /// <summary>A scalar whose kind its source format already fixed.</summary>
    /// <param name="payload">The typed payload.</param>
    /// <param name="line">The one-based line it begins on.</param>
    /// <param name="column">The one-based Section 22 column it begins at.</param>
    /// <remarks>
    /// Section 18: "Typed JSON and YAML input scalars retain their source kind without
    /// re-inference." A number, a Boolean, and null are settled where they are read.
    /// </remarks>
    public static StructuredScalar OfTyped(ScalarPayload payload, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return new StructuredScalar(payload, null, line, column);
    }

    /// <summary>A native string, which Section 15.1 step 6 still has to lex.</summary>
    /// <param name="text">The string as the native parser decoded it.</param>
    /// <param name="line">The one-based line it begins on.</param>
    /// <param name="column">The one-based Section 22 column it begins at.</param>
    public static StructuredScalar OfNativeString(string text, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new StructuredScalar(null, text, line, column);
    }
}

/// <summary>One entry of a <see cref="StructuredMapping"/>.</summary>
/// <param name="Key">The key as the native parser decoded it, for diagnostics.</param>
/// <param name="Name">The key, already lexed into the one component its format gives it.</param>
/// <param name="Value">The value.</param>
/// <param name="Line">The one-based line the key begins on.</param>
/// <param name="Column">The one-based Section 22 column the key begins at.</param>
public sealed record StructuredProperty(
    string Key,
    NamePart Name,
    StructuredNode Value,
    int Line,
    int Column);

/// <summary>A native mapping, in the property order its source wrote.</summary>
/// <param name="Properties">The properties, in source order.</param>
/// <param name="Line">The one-based line this mapping begins on.</param>
/// <param name="Column">The one-based Section 22 column this mapping begins at.</param>
/// <remarks>
/// An empty mapping is Section 4.2's explicit mapping presence: it contributes a node with no
/// children rather than nothing at all, which is why the property list may be empty and the node
/// still matters.
/// </remarks>
public sealed record StructuredMapping(
    ImmutableArray<StructuredProperty> Properties,
    int Line,
    int Column) : StructuredNode(Line, Column)
{
    /// <summary>
    /// The scalar this node owns at its own path, or <see langword="null"/> when it owns none.
    /// </summary>
    /// <remarks>
    /// JSON and YAML never set this: a mapping is a mapping and a scalar is a scalar. Section 11.4
    /// gives XML a third case — "an element with no child elements and exactly one non-comment text
    /// or CDATA node also exposes that scalar at the element path" — so <c>&lt;b x="1"&gt;two&lt;/b&gt;</c>
    /// owns the string <c>two</c> at <c>b</c> while <c>b.@x</c> remains its child. Section 4.4 already
    /// lets one overlay node carry a payload and children at once; this is how a reader says so.
    /// </remarks>
    public StructuredScalar? Scalar { get; init; }
}

/// <summary>A native sequence.</summary>
/// <param name="Items">The items, in source order.</param>
/// <param name="Line">The one-based line this sequence begins on.</param>
/// <param name="Column">The one-based Section 22 column this sequence begins at.</param>
public sealed record StructuredSequence(
    ImmutableArray<StructuredNode> Items,
    int Line,
    int Column) : StructuredNode(Line, Column);
