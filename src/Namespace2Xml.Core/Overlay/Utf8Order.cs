namespace Namespace2Xml.Overlay;

/// <summary>
/// Compares strings as unsigned UTF-8 bytes, which is the specification's final deterministic
/// tie-breaker in Sections 5.2, 16.6, 21.3 and 24.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not <see cref="string.CompareOrdinal(string, string)"/>. Ordinal comparison
/// is over UTF-16 code units, which places a surrogate pair before U+E000, while UTF-8 places it
/// after. A qualified path, destination or diagnostic containing an astral character would then be
/// ordered in a way the specification does not permit — and would do so only for inputs nobody
/// writes by accident, which is the worst kind of defect to carry.
/// </para>
/// <para>
/// UTF-8 byte order is code-point order, so comparing runes yields the specified result without
/// allocating or encoding anything.
/// </para>
/// </remarks>
public static class Utf8Order
{
    /// <summary>An <see cref="IComparer{T}"/> over the same order, for sorting.</summary>
    public static IComparer<string?> Comparer { get; } = new Utf8Comparer();

    /// <summary>
    /// Compares two strings as unsigned UTF-8 bytes, with an absent value before any present one.
    /// </summary>
    /// <param name="left">The left string, which may be absent.</param>
    /// <param name="right">The right string, which may be absent.</param>
    /// <returns>A negative value, zero, or a positive value in the usual comparison convention.</returns>
    public static int Compare(string? left, string? right)
    {
        if (left is null)
        {
            return right is null ? 0 : -1;
        }

        if (right is null)
        {
            return 1;
        }

        var leftRunes = left.EnumerateRunes();
        var rightRunes = right.EnumerateRunes();

        while (true)
        {
            var hasLeft = leftRunes.MoveNext();
            var hasRight = rightRunes.MoveNext();

            if (!hasLeft || !hasRight)
            {
                return hasLeft == hasRight ? 0 : hasLeft ? 1 : -1;
            }

            var byRune = leftRunes.Current.Value.CompareTo(rightRunes.Current.Value);

            if (byRune != 0)
            {
                return byRune;
            }
        }
    }

    private sealed class Utf8Comparer : IComparer<string?>
    {
        public int Compare(string? x, string? y) => Utf8Order.Compare(x, y);
    }
}
