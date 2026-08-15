using System.Numerics;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scalars;

namespace Namespace2Xml.Overlay;

/// <summary>
/// A Section 4.3 scalar payload: a value together with the kind it is known to have.
/// </summary>
/// <remarks>
/// <para>
/// The kind travels with the value rather than being recomputed from its text, because Section 18
/// inference is not idempotent over its own output. The string <c>"01"</c> infers to nothing
/// numeric and stays a string, but an integer <c>1</c> renders as <c>1</c>, and re-inferring a
/// rendered value would silently convert a JSON string <c>"1"</c> into a number on its way through
/// the tool. Section 18 says typed JSON and YAML scalars "retain their source kind without
/// re-inference"; keeping the kind is how that is enforced rather than remembered.
/// </para>
/// <para>
/// Numeric spelling is deliberately not retained. Section 18 ends with "numeric source spelling is
/// never retained", so a decimal is held as a <see cref="BigDecimal"/> and rendered through its
/// canonical text on demand. A payload that kept the source text would let two inputs that are the
/// same number produce different bytes.
/// </para>
/// </remarks>
public sealed class ScalarPayload : IEquatable<ScalarPayload>
{
    private readonly string? text;
    private readonly bool boolean;
    private readonly BigInteger integer;
    private readonly BigDecimal decimalValue;
    private readonly InterpretedValue? unresolved;
    private readonly ValueOrigin origin;

    private ScalarPayload(
        ScalarKind kind,
        string? text = null,
        bool boolean = false,
        BigInteger integer = default,
        BigDecimal decimalValue = default,
        InterpretedValue? unresolved = null,
        ValueOrigin origin = default,
        XmlContentSpelling spelling = XmlContentSpelling.Text,
        long? contentToken = null)
    {
        Kind = kind;
        this.text = text;
        this.boolean = boolean;
        this.integer = integer;
        this.decimalValue = decimalValue;
        this.unresolved = unresolved;
        this.origin = origin;
        Spelling = spelling;
        ContentToken = contentToken;
    }

    /// <summary>The Section 4.3 kind of this payload.</summary>
    public ScalarKind Kind { get; }

    /// <summary>
    /// The Section 11.4 content-token ordering value of the XML text or CDATA run this payload was
    /// read from, or <see langword="null"/> for a payload no such run supplied.
    /// </summary>
    /// <remarks>
    /// Section 11.4 exposes an element's lone non-comment run as the scalar at the element path
    /// rather than as a content node, which leaves the run's ordering value with nowhere to live on
    /// the node — <see cref="NodeMarks.ContentToken"/> already holds the element's own position
    /// among <em>its</em> siblings, a different parent's allocator. Section 19.5 needs the run's
    /// value to place the element's comments on either side of it, so it rides here instead.
    ///
    /// Carrying it on the payload rather than the node is what makes Section 11.4's replacement
    /// rule fall out by construction: a contribution that replaces the value replaces the position
    /// with it, and a contribution from a namespace profile, JSON, YAML, or a reference expresses no
    /// position and so acquires none. Section 19.5 writes a payload with no ordering value first.
    /// </remarks>
    public long? ContentToken { get; }

    /// <summary>
    /// The Section 11.6 spelling this payload carries when XML holds it, which is
    /// <see cref="XmlContentSpelling.Text"/> for everything the XML reader did not read as CDATA.
    /// </summary>
    public XmlContentSpelling Spelling { get; }

    /// <summary>This payload spelled as Section 11.6 CDATA.</summary>
    /// <remarks>
    /// Only a textual or still-unresolved payload can be spelled as CDATA. A CDATA section holds
    /// characters, so the XML reader produces a native string or a reference-bearing value from
    /// one and Section 18 inference never sees it as a candidate number — asking for CDATA
    /// anywhere else is a defect rather than an input error.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The payload is not textual.</exception>
    public ScalarPayload AsCdata() => Kind switch
    {
        ScalarKind.String or ScalarKind.UntypedString =>
            new ScalarPayload(Kind, text, spelling: XmlContentSpelling.Cdata, contentToken: ContentToken),
        ScalarKind.Unresolved => new ScalarPayload(
            Kind, unresolved: unresolved, origin: origin, spelling: XmlContentSpelling.Cdata,
            contentToken: ContentToken),
        _ => throw new InvalidOperationException($"a {Kind} payload cannot be spelled as CDATA."),
    };

    /// <summary>This payload spelled as a Section 11.5 comment node.</summary>
    /// <remarks>
    /// A comment holds characters and nothing else, so like <see cref="AsCdata"/> this accepts only
    /// a textual or still-unresolved payload. Nothing but the XML reader calls it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The payload is not textual.</exception>
    public ScalarPayload AsComment() => Kind switch
    {
        ScalarKind.String or ScalarKind.UntypedString =>
            new ScalarPayload(Kind, text, spelling: XmlContentSpelling.Comment, contentToken: ContentToken),
        ScalarKind.Unresolved => new ScalarPayload(
            Kind, unresolved: unresolved, origin: origin, spelling: XmlContentSpelling.Comment,
            contentToken: ContentToken),
        _ => throw new InvalidOperationException($"a {Kind} payload cannot be spelled as a comment."),
    };

    /// <summary>
    /// Whether this payload is a value, which every payload except a Section 11.5 comment is.
    /// </summary>
    /// <remarks>
    /// Section 13.1: comments "have no scalar payload and are invisible to format-agnostic
    /// reference resolution", and Section 13.3 makes a canonical reference to one fail as a
    /// non-scalar reference. A comment node still carries its characters here, because Section 19.5
    /// has to render them and Section 11.4 lets <c>type=text</c> convert them into a value.
    /// </remarks>
    public bool IsValue => Spelling != XmlContentSpelling.Comment;

    /// <summary>This payload under the Section 11.6 spelling of the value that produced it.</summary>
    /// <param name="spelling">The spelling to carry.</param>
    /// <remarks>
    /// Section 13.2 says a sole reference "inherits the referenced scalar kind and value", and
    /// stops there. Spelling is neither: Section 11.6 preserves <em>imported</em> CDATA, and what
    /// was imported is the node the value was written at, not the node the value was read from. A
    /// namespace-profile entry <c>c=${a}</c> is ordinary text however <c>a</c> was spelled, and a
    /// CDATA section holding a reference stays CDATA however its referent was spelled.
    /// </remarks>
    public ScalarPayload As(XmlContentSpelling spelling) => spelling switch
    {
        _ when spelling == Spelling => this,
        XmlContentSpelling.Cdata => AsCdata(),
        _ => Kind switch
        {
            ScalarKind.String or ScalarKind.UntypedString =>
                new ScalarPayload(Kind, text, contentToken: ContentToken),
            ScalarKind.Unresolved => new ScalarPayload(
                Kind, unresolved: unresolved, origin: origin, contentToken: ContentToken),
            _ => this,
        },
    };

    /// <summary>
    /// This payload carrying the Section 11.4 content-token ordering value of the XML run it was
    /// read from.
    /// </summary>
    /// <param name="contentToken">The ordering value the run held among its siblings.</param>
    /// <remarks>Only the XML reader calls this; every other source leaves the value absent.</remarks>
    public ScalarPayload AtContentToken(long contentToken) => Kind switch
    {
        ScalarKind.String or ScalarKind.UntypedString => new ScalarPayload(
            Kind, text, spelling: Spelling, contentToken: contentToken),
        ScalarKind.Unresolved => new ScalarPayload(
            Kind, unresolved: unresolved, origin: origin, spelling: Spelling,
            contentToken: contentToken),
        ScalarKind.Boolean => new ScalarPayload(
            Kind, boolean: boolean, spelling: Spelling, contentToken: contentToken),
        ScalarKind.Integer => new ScalarPayload(
            Kind, integer: integer, spelling: Spelling, contentToken: contentToken),
        ScalarKind.Decimal => new ScalarPayload(
            Kind, decimalValue: decimalValue, spelling: Spelling, contentToken: contentToken),
        _ => new ScalarPayload(Kind, spelling: Spelling, contentToken: contentToken),
    };

    /// <summary>The null payload of Section 4.3.</summary>
    public static ScalarPayload Null { get; } = new(ScalarKind.Null);

    /// <summary>Whether this payload is the null value.</summary>
    public bool IsNull => Kind == ScalarKind.Null;

    /// <summary>
    /// Whether Section 18 inference still applies to this payload. Only a namespace-profile
    /// payload that has not been classified is eligible.
    /// </summary>
    public bool IsUntyped => Kind == ScalarKind.UntypedString;

    /// <summary>
    /// Whether this payload still carries a Section 13 reference. Section 15.1 step 15 replaces
    /// every such payload that any output instance can reach.
    /// </summary>
    public bool IsUnresolved => Kind == ScalarKind.Unresolved;

    /// <summary>
    /// A payload carrying an unresolved Section 13 reference.
    /// </summary>
    /// <param name="value">The interpreted value, which contains at least one reference token.</param>
    /// <param name="origin">Where the value was written.</param>
    /// <remarks>
    /// This is a payload rather than an entry held aside, because Section 13.1 resolves references
    /// "after wildcard generation and ordinary data merging". A reference-bearing entry is an
    /// ordinary scalar contribution until then: it wins and loses Section 17.1 merges by its
    /// position, it settles the Section 4.4 scalar/container contest at its node, a Section 8.6
    /// mask can suppress it, and a wildcard template can match it. Keeping it outside the model
    /// would make every one of those decisions come out differently.
    /// </remarks>
    public static ScalarPayload Unresolved(InterpretedValue value, ValueOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ScalarPayload(ScalarKind.Unresolved, unresolved: value, origin: origin);
    }

    /// <summary>Where an unresolved payload was written.</summary>
    /// <exception cref="InvalidOperationException">The payload is already resolved.</exception>
    public ValueOrigin Origin => Kind == ScalarKind.Unresolved
        ? origin
        : throw new InvalidOperationException($"a {Kind} payload has no origin.");

    /// <summary>An untyped namespace-profile payload, the Section 4.3 initial kind.</summary>
    /// <param name="text">The payload text, already unescaped by Section 8.3.</param>
    public static ScalarPayload Untyped(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new ScalarPayload(ScalarKind.UntypedString, text);
    }

    /// <summary>A settled string payload.</summary>
    /// <param name="text">The string value.</param>
    public static ScalarPayload OfString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new ScalarPayload(ScalarKind.String, text);
    }

    /// <summary>A Boolean payload.</summary>
    /// <param name="value">The Boolean value.</param>
    public static ScalarPayload OfBoolean(bool value) => new(ScalarKind.Boolean, boolean: value);

    /// <summary>An arbitrary-precision integer payload.</summary>
    /// <param name="value">The integer value.</param>
    public static ScalarPayload OfInteger(BigInteger value) => new(ScalarKind.Integer, integer: value);

    /// <summary>An arbitrary-precision decimal payload.</summary>
    /// <param name="value">The decimal value.</param>
    public static ScalarPayload OfDecimal(BigDecimal value) => new(ScalarKind.Decimal, decimalValue: value);

    /// <summary>The text of a string or untyped payload.</summary>
    /// <exception cref="InvalidOperationException">The payload is not textual.</exception>
    public string Text => Kind is ScalarKind.String or ScalarKind.UntypedString
        ? text!
        : throw new InvalidOperationException($"a {Kind} payload has no text.");

    /// <summary>The value of a Boolean payload.</summary>
    /// <exception cref="InvalidOperationException">The payload is not Boolean.</exception>
    public bool Boolean => Kind == ScalarKind.Boolean
        ? boolean
        : throw new InvalidOperationException($"a {Kind} payload has no Boolean value.");

    /// <summary>The value of an integer payload.</summary>
    /// <exception cref="InvalidOperationException">The payload is not an integer.</exception>
    public BigInteger IntegerValue => Kind == ScalarKind.Integer
        ? integer
        : throw new InvalidOperationException($"a {Kind} payload has no integer value.");

    /// <summary>The value of a decimal payload.</summary>
    /// <exception cref="InvalidOperationException">The payload is not a decimal.</exception>
    public BigDecimal DecimalValue => Kind == ScalarKind.Decimal
        ? decimalValue
        : throw new InvalidOperationException($"a {Kind} payload has no decimal value.");

    /// <summary>The interpreted value of an unresolved payload.</summary>
    /// <exception cref="InvalidOperationException">The payload is already resolved.</exception>
    public InterpretedValue UnresolvedValue => Kind == ScalarKind.Unresolved
        ? unresolved!
        : throw new InvalidOperationException($"a {Kind} payload has no unresolved value.");

    /// <summary>
    /// The Section 18 canonical text of this payload, which every output format and interpolation
    /// uses.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The payload is null, which has no canonical text of its own: Section 19 lets each format
    /// spell null differently, so rendering it here would impose one format's spelling on all. Or
    /// the payload is unresolved, which no output may reach — Section 14.4 resolves the closure of
    /// every selected entry strictly, so an unresolved payload arriving at a renderer is a defect
    /// in step 15 rather than anything the input could cause.
    /// </exception>
    public string ToCanonicalText() => Kind switch
    {
        ScalarKind.UntypedString or ScalarKind.String => text!,
        ScalarKind.Boolean => boolean ? "true" : "false",
        ScalarKind.Integer => integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ScalarKind.Decimal => decimalValue.ToCanonicalText(),
        ScalarKind.Unresolved => throw new InvalidOperationException(
            "an unresolved payload has no text until Section 15.1 step 15 resolves it."),
        _ => throw new InvalidOperationException("the null payload has no format-independent text."),
    };

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="ContentToken"/> deliberately takes no part. <see cref="Spelling"/> does, because
    /// Section 11.6 makes CDATA a property of the value itself — it changes what is written. Where
    /// the value was found is not a property of the value: two payloads holding the same characters
    /// are the same value whichever slot of an element supplied them. Equality decides whether two
    /// contributions agree, and a position-sensitive answer would make that decision depend on
    /// something no section asks it to depend on.
    /// </remarks>
    public bool Equals(ScalarPayload? other) =>
        other is not null &&
        Kind == other.Kind &&
        Kind switch
        {
            ScalarKind.UntypedString or ScalarKind.String =>
                string.Equals(text, other.text, StringComparison.Ordinal)
                && Spelling == other.Spelling,
            ScalarKind.Boolean => boolean == other.boolean,
            ScalarKind.Integer => integer == other.integer,
            ScalarKind.Decimal => decimalValue == other.decimalValue,
            ScalarKind.Unresolved =>
                unresolved!.Equals(other.unresolved)
                && origin == other.origin
                && Spelling == other.Spelling,
            _ => true,
        };

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ScalarPayload);

    /// <inheritdoc/>
    public override int GetHashCode() => Kind switch
    {
        ScalarKind.UntypedString or ScalarKind.String =>
            HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode(text!), Spelling),
        ScalarKind.Boolean => HashCode.Combine(Kind, boolean),
        ScalarKind.Integer => HashCode.Combine(Kind, integer),
        ScalarKind.Decimal => HashCode.Combine(Kind, decimalValue),
        ScalarKind.Unresolved => HashCode.Combine(Kind, unresolved, origin, Spelling),
        _ => Kind.GetHashCode(),
    };

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        ScalarKind.Null => "null",
        ScalarKind.Unresolved => $"{Kind}:{unresolved!.Tokens.Length} token(s)",
        _ => $"{Kind}:{ToCanonicalText()}",
    };
}
