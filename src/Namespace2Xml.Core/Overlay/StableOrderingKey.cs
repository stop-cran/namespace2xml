namespace Namespace2Xml.Overlay;

/// <summary>
/// The Section 4.7 stable ordering key: the five-component tuple that decides which of two
/// contributions is later, and therefore which one wins.
/// </summary>
/// <param name="SourceOrdinal">The Section 5.1 CLI source ordinal.</param>
/// <param name="ItemOrdinal">The item traversal ordinal within that source.</param>
/// <param name="TransformationOrdinal">The transformation declaration ordinal.</param>
/// <param name="MatchOrdinal">The wildcard match ordinal.</param>
/// <param name="GenerationOrdinal">The deterministic local generation ordinal.</param>
/// <remarks>
/// <para>
/// Comparison is lexicographic in declaration order, and a component that does not apply to an item
/// is zero. A plain source entry therefore precedes every item its own transformations generate from
/// the same source position, which is what Section 5.3 requires of generated entries.
/// </para>
/// <para>
/// Every component is a <see cref="long"/> because every bound that could cap one is a
/// <see cref="long"/> in Section 6.2 and may be raised to the signed 64-bit range. An
/// <see cref="int"/> ordinal would wrap silently at a raised <c>--max-nodes</c> or
/// <c>--max-generated</c>, and a wrapped ordinal reorders output rather than failing.
/// </para>
/// <para>
/// Section 4.7 also requires that concurrent parsing not alter this key. Nothing here reads a
/// counter shared between workers: each component is assigned from state its own source owns, and
/// the parse-phase join supplies the CLI source ordinal.
/// </para>
/// </remarks>
public readonly record struct StableOrderingKey(
    long SourceOrdinal,
    long ItemOrdinal,
    long TransformationOrdinal,
    long MatchOrdinal,
    long GenerationOrdinal) : IComparable<StableOrderingKey>
{
    /// <summary>The key that precedes every other, for a first contribution at the first source.</summary>
    public static StableOrderingKey First => default;

    /// <summary>
    /// The key that follows every other, for a carrier that requires no key of its own.
    /// </summary>
    /// <remarks>
    /// Section 17.5's per-destination high-water marks are not contributions: they survive a
    /// replacement precisely because the contributions that raised them did not. A carrier that
    /// holds one must therefore lose every Section 5.2 position comparison, so that a path the
    /// replacement removed and a later file recreates takes the recreating contribution's position
    /// rather than the discarded one's. Every real component is an ordinal counted from zero over
    /// CLI sources, items, declarations, matches or generations, so no reachable key equals this
    /// one; the ordering it expresses is nonetheless total rather than special-cased.
    /// </remarks>
    public static StableOrderingKey Last =>
        new(long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue);

    /// <summary>A key for a plain source item, whose last three components do not apply.</summary>
    public static StableOrderingKey FromSource(long sourceOrdinal, long itemOrdinal) =>
        new(sourceOrdinal, itemOrdinal, 0, 0, 0);

    /// <inheritdoc/>
    public int CompareTo(StableOrderingKey other)
    {
        var result = SourceOrdinal.CompareTo(other.SourceOrdinal);

        if (result != 0)
        {
            return result;
        }

        result = ItemOrdinal.CompareTo(other.ItemOrdinal);

        if (result != 0)
        {
            return result;
        }

        result = TransformationOrdinal.CompareTo(other.TransformationOrdinal);

        if (result != 0)
        {
            return result;
        }

        result = MatchOrdinal.CompareTo(other.MatchOrdinal);

        return result != 0 ? result : GenerationOrdinal.CompareTo(other.GenerationOrdinal);
    }

    /// <summary>Whether the left key is earlier in Section 4.7 order.</summary>
    public static bool operator <(StableOrderingKey left, StableOrderingKey right) =>
        left.CompareTo(right) < 0;

    /// <summary>Whether the left key is later in Section 4.7 order.</summary>
    public static bool operator >(StableOrderingKey left, StableOrderingKey right) =>
        left.CompareTo(right) > 0;

    /// <summary>Whether the left key is not later in Section 4.7 order.</summary>
    public static bool operator <=(StableOrderingKey left, StableOrderingKey right) =>
        left.CompareTo(right) <= 0;

    /// <summary>Whether the left key is not earlier in Section 4.7 order.</summary>
    public static bool operator >=(StableOrderingKey left, StableOrderingKey right) =>
        left.CompareTo(right) >= 0;

    /// <summary>The later of two keys, which Section 5 makes the winner.</summary>
    public static StableOrderingKey Later(StableOrderingKey left, StableOrderingKey right) =>
        left >= right ? left : right;
}
