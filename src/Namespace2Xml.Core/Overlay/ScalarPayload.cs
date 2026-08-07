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
        ValueOrigin origin = default)
    {
        Kind = kind;
        this.text = text;
        this.boolean = boolean;
        this.integer = integer;
        this.decimalValue = decimalValue;
        this.unresolved = unresolved;
        this.origin = origin;
    }

    /// <summary>The Section 4.3 kind of this payload.</summary>
    public ScalarKind Kind { get; }

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
    public bool Equals(ScalarPayload? other) =>
        other is not null &&
        Kind == other.Kind &&
        Kind switch
        {
            ScalarKind.UntypedString or ScalarKind.String =>
                string.Equals(text, other.text, StringComparison.Ordinal),
            ScalarKind.Boolean => boolean == other.boolean,
            ScalarKind.Integer => integer == other.integer,
            ScalarKind.Decimal => decimalValue == other.decimalValue,
            ScalarKind.Unresolved =>
                unresolved!.Equals(other.unresolved) && origin == other.origin,
            _ => true,
        };

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ScalarPayload);

    /// <inheritdoc/>
    public override int GetHashCode() => Kind switch
    {
        ScalarKind.UntypedString or ScalarKind.String =>
            HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode(text!)),
        ScalarKind.Boolean => HashCode.Combine(Kind, boolean),
        ScalarKind.Integer => HashCode.Combine(Kind, integer),
        ScalarKind.Decimal => HashCode.Combine(Kind, decimalValue),
        ScalarKind.Unresolved => HashCode.Combine(Kind, unresolved, origin),
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
