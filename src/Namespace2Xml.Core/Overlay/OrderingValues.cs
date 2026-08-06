using System.Globalization;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

/// <summary>
/// The Section 5.4 ordering-value range and the Section 8.7 canonical decimal spelling that
/// addresses it.
/// </summary>
/// <remarks>
/// Section 8.7 is deliberately strict about spelling. <c>01</c> and <c>1</c> denote the same
/// number, but a configuration key is not a number, and treating them as one ordering value would
/// silently merge two mapping children a user wrote as distinct. Anything that is not the canonical
/// spelling of an in-range value stays an ordinary mapping key and prevents sequence
/// interpretation, so this predicate decides both high-water reservation at step 8 and sequence
/// eligibility at step 11.
/// </remarks>
public static class OrderingValues
{
    /// <summary>
    /// Reads a name component as a Section 8.7 canonically spelled ordering value.
    /// </summary>
    /// <param name="part">The component.</param>
    /// <param name="value">The ordering value, when the component is one.</param>
    /// <returns>Whether the component is a canonical in-range decimal.</returns>
    public static bool TryRead(NamePart part, out long value)
    {
        value = default;

        return part is OrdinaryPart ordinary
            && ordinary.LiteralText is { } text
            && TryRead(text, out value);
    }

    /// <summary>
    /// Reads text as a Section 8.7 canonically spelled ordering value.
    /// </summary>
    /// <param name="text">The candidate spelling.</param>
    /// <param name="value">The ordering value, when the text is one.</param>
    /// <returns>Whether the text is a canonical in-range decimal.</returns>
    public static bool TryRead(string text, out long value)
    {
        value = default;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // Section 8.7: "leading-zero spellings such as 00 and 01 are ordinary mapping keys".
        // TryParse would accept them, and accept them as the same value as the spelling without the
        // zeros, so this check has to come first.
        if (text[0] == '0' && text.Length > 1)
        {
            return false;
        }

        // NumberStyles.None accepts ASCII digits and nothing else: no sign, no separator, no
        // surrounding whitespace, and no non-ASCII digit (verified). Section 8.7: "a canonically
        // spelled decimal above the supported maximum is an ordinary mapping key", so overflow is
        // not an error here either — it is a different kind of name.
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>The canonical spelling of an ordering value.</summary>
    /// <param name="value">The ordering value.</param>
    public static string ToCanonicalText(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The name component that addresses an ordering value, per Section 5.4.</summary>
    /// <param name="value">The ordering value.</param>
    public static NamePart ToNamePart(long value) =>
        new OrdinaryPart([new LiteralToken(ToCanonicalText(value))]);
}
