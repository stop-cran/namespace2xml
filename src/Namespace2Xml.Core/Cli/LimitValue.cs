using System.Globalization;

namespace Namespace2Xml.Cli;

/// <summary>
/// The ASCII decimal syntax specification Section 6.2 fixes for limit-option values.
/// </summary>
/// <remarks>
/// The grammar is deliberately narrow, and the rejections carry as much weight as the acceptances.
/// <c>1_000</c>, <c>1.5GiB</c>, <c>2 MiB</c>, <c>+8</c>, <c>0</c> and <c>10MB</c> are all
/// <c>CLI001</c>: a decimal SI suffix is rejected rather than quietly read as its binary neighbour,
/// and a leading zero is rejected rather than read as octal by some future reader. Nothing here
/// consults the ambient culture, so a comma or a period is never a group separator.
/// </remarks>
public static class LimitValue
{
    private const long Kibibyte = 1024;
    private const long Mebibyte = 1024 * 1024;
    private const long Gibibyte = 1024 * 1024 * 1024;

    /// <summary>Parses a count or depth value: <c>[1-9][0-9]*</c>, with no suffix permitted.</summary>
    /// <param name="text">The raw option value, exactly as it appeared on the command line.</param>
    /// <param name="value">The parsed value when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the value is well formed.</returns>
    public static bool TryParseCount(string? text, out long value) =>
        TryParseDigits(text, out value);

    /// <summary>
    /// Parses a byte value: <c>[1-9][0-9]*</c> optionally followed, case-insensitively, by
    /// <c>KiB</c>, <c>MiB</c> or <c>GiB</c>.
    /// </summary>
    /// <param name="text">The raw option value, exactly as it appeared on the command line.</param>
    /// <param name="value">The parsed byte count when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the value is well formed and does not overflow.</returns>
    public static bool TryParseBytes(string? text, out long value)
    {
        value = 0;

        if (text is null)
        {
            return false;
        }

        var multiplier = 1L;
        var digits = text;

        foreach (var (suffix, scale) in new[] { ("KiB", Kibibyte), ("MiB", Mebibyte), ("GiB", Gibibyte) })
        {
            if (text.Length > suffix.Length && text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                multiplier = scale;
                digits = text[..^suffix.Length];
                break;
            }
        }

        if (!TryParseDigits(digits, out var magnitude))
        {
            return false;
        }

        // Section 6.2 makes multiplication overflow CLI001 rather than a saturated or wrapped
        // bound, so the check happens before the multiply rather than after it.
        if (magnitude > long.MaxValue / multiplier)
        {
            return false;
        }

        value = magnitude * multiplier;
        return true;
    }

    private static bool TryParseDigits(string? text, out long value)
    {
        value = 0;

        // [1-9][0-9]*, checked scalar by scalar. long.TryParse would accept a leading sign, a
        // leading zero, surrounding whitespace, and culture-specific group separators, every one
        // of which Section 6.2 names as CLI001.
        if (string.IsNullOrEmpty(text) || text[0] is < '1' or > '9')
        {
            return false;
        }

        foreach (var scalar in text)
        {
            if (!char.IsAsciiDigit(scalar))
            {
                return false;
            }
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
