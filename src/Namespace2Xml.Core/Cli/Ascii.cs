namespace Namespace2Xml.Cli;

/// <summary>
/// ASCII-only case comparison. The specification requires ASCII case-insensitive matching for
/// option values; culture-aware or Unicode-aware folding would make results depend on the
/// current locale, which specification Section 24 forbids.
/// </summary>
internal static class Ascii
{
    internal static bool EqualsIgnoreCase(string? left, string right)
    {
        if (left is null || left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < right.Length; i++)
        {
            if (Fold(left[i]) != Fold(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static char Fold(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
}
