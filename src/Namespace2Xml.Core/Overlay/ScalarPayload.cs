using System.Numerics;
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

    private ScalarPayload(
        ScalarKind kind,
        string? text = null,
        bool boolean = false,
        BigInteger integer = default,
        BigDecimal decimalValue = default)
    {
        Kind = kind;
        this.text = text;
        this.boolean = boolean;
        this.integer = integer;
        this.decimalValue = decimalValue;
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

    /// <summary>
    /// The Section 18 canonical text of this payload, which every output format and interpolation
    /// uses.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The payload is null, which has no canonical text of its own: Section 19 lets each format
    /// spell null differently, so rendering it here would impose one format's spelling on all.
    /// </exception>
    public string ToCanonicalText() => Kind switch
    {
        ScalarKind.UntypedString or ScalarKind.String => text!,
        ScalarKind.Boolean => boolean ? "true" : "false",
        ScalarKind.Integer => integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ScalarKind.Decimal => decimalValue.ToCanonicalText(),
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
        _ => Kind.GetHashCode(),
    };

    /// <inheritdoc/>
    public override string ToString() => IsNull ? "null" : $"{Kind}:{ToCanonicalText()}";
}
