using System.Numerics;
using Namespace2Xml.Overlay;

namespace Namespace2Xml.Scalars;

/// <summary>
/// Specification Section 18 scalar inference: the locale-independent grammar that settles an
/// untyped namespace value into a kind.
/// </summary>
/// <remarks>
/// <para>
/// The five rules are tried in the order Section 18 writes them, and the order is load-bearing.
/// Rule 3 accepts <c>[+-]?[0-9]+</c>, which is deliberately more permissive than JSON: <c>+5</c>
/// and <c>007</c> are integers here and are not JSON numbers. Rule 4 delegates to JSON's grammar
/// exactly. Trying rule 4 first would reject <c>007</c> and then rule 3 would accept it, which
/// happens to give the same answer — but <c>0.5e1</c> and <c>+0.5</c> do not agree, so the order is
/// preserved rather than reasoned about.
/// </para>
/// <para>
/// Inference is not applied to a payload that already has a kind. Section 18 says typed JSON and
/// YAML scalars "retain their source kind without re-inference", and Section 4.3 makes
/// <see cref="ScalarPayload.IsUntyped"/> the eligibility test, so re-inference is prevented by the
/// data rather than by every caller remembering to check.
/// </para>
/// </remarks>
public static class ScalarInference
{
    /// <summary>Settles one payload.</summary>
    /// <param name="payload">The payload, whatever its kind.</param>
    /// <returns>
    /// The settled payload, or <paramref name="payload"/> unchanged when it already has a kind.
    /// </returns>
    public static ScalarPayload Infer(ScalarPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return payload.IsUntyped ? Infer(payload.Text) : payload;
    }

    /// <summary>Settles one untyped value's text.</summary>
    /// <param name="text">The value text, already unescaped by Section 8.3.</param>
    /// <returns>The payload the Section 18 grammar assigns to it.</returns>
    public static ScalarPayload Infer(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return ScalarPayload.Null;
        }

        if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return ScalarPayload.OfBoolean(true);
        }

        if (text.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return ScalarPayload.OfBoolean(false);
        }

        if (IsRuleThreeInteger(text))
        {
            // BigInteger.Parse accepts a leading '+' and leading zeros, which is what rule 3 asks
            // for, but it also accepts group separators and whitespace under some styles. The
            // shape has already been checked, so the parse only has to convert.
            return ScalarPayload.OfInteger(
                BigInteger.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
        }

        return BigDecimal.TryParse(text, out var value)
            ? ScalarPayload.OfDecimal(value)
            : ScalarPayload.OfString(text);
    }

    /// <summary>Section 18 grammar rule 3: <c>[+-]?[0-9]+</c>, anchored at both ends.</summary>
    private static bool IsRuleThreeInteger(string text)
    {
        var start = text.Length > 0 && text[0] is '+' or '-' ? 1 : 0;

        if (start == text.Length)
        {
            return false;
        }

        for (var i = start; i < text.Length; i++)
        {
            if (text[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
