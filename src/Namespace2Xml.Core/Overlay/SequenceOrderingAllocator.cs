namespace Namespace2Xml.Overlay;

/// <summary>
/// The Section 5.4 high-water mark for one sequence path, and the allocator that derives implicit
/// ordering values from it.
/// </summary>
/// <remarks>
/// <para>
/// One instance is one sequence path: Section 5.4 gives each path its own mark. The dictionary that
/// maps a path to its allocator belongs to the merge engine, which is the only component that knows
/// when a path comes into existence.
/// </para>
/// <para>
/// The mark records the greatest value ever allocated or supplied, <b>including values later removed
/// or replaced</b>. It therefore only ever rises. Lowering it on removal would let a later native
/// item reuse the vacated value and land before items that were already there, which is exactly the
/// defragmentation Section 5.4 forbids; and because the reused value is a real logical address for
/// wildcard, ignore and reference matching, a rule written against the earlier document would
/// silently start matching a different item.
/// </para>
/// </remarks>
public sealed class SequenceOrderingAllocator
{
    /// <summary>The least ordering value Section 5.4 admits.</summary>
    public const long MinOrderingValue = 0;

    /// <summary>The greatest ordering value Section 5.4 admits.</summary>
    public const long MaxOrderingValue = long.MaxValue;

    /// <summary>The value of an untouched mark, one below the least ordering value.</summary>
    public const long InitialHighWaterMark = MinOrderingValue - 1;

    /// <summary>
    /// The greatest ordering value ever allocated or explicitly supplied at this path, or
    /// <see cref="InitialHighWaterMark"/> when there has been neither.
    /// </summary>
    public long HighWaterMark { get; private set; } = InitialHighWaterMark;

    /// <summary>Resumes allocation at a path that already has a mark.</summary>
    /// <param name="highWaterMark">The mark carried by the path so far.</param>
    /// <remarks>
    /// The mark is state that belongs to the path and travels with it across pipeline phases, so
    /// the overlay stores it and this type supplies the rule that reads and advances it. Keeping
    /// the mark in a side table owned by one phase instead would lose it at every step that
    /// re-addresses a path — <c>root</c>, <c>key</c>, and destination planning all do — and
    /// Section 5.4 requires the mark to survive exactly those.
    /// </remarks>
    public static SequenceOrderingAllocator From(long highWaterMark)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(highWaterMark, InitialHighWaterMark);

        return new SequenceOrderingAllocator { HighWaterMark = highWaterMark };
    }

    /// <summary>Whether a value is a Section 5.4 ordering value.</summary>
    public static bool IsOrderingValue(long value) =>
        value is >= MinOrderingValue and <= MaxOrderingValue;

    /// <summary>
    /// Allocates the next implicit ordering value as <c>high-water + 1</c>, for a native JSON, YAML
    /// or XML sequence item or an item created by a structural transformation.
    /// </summary>
    /// <param name="value">The allocated value, when this returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> when the mark already sits at <see cref="MaxOrderingValue"/>, which
    /// Section 5.4 makes a blocking limit error. The caller reports <c>LIMIT001</c>; wrapping to a
    /// negative value would reorder the sequence instead of failing.
    /// </returns>
    public bool TryAllocate(out long value)
    {
        if (HighWaterMark == MaxOrderingValue)
        {
            value = default;

            return false;
        }

        HighWaterMark++;
        value = HighWaterMark;

        return true;
    }

    /// <summary>
    /// Records an explicit ordering value supplied by a numeric namespace part or a canonical
    /// decimal JSON, YAML or mapping key, raising the mark only when the value exceeds it.
    /// </summary>
    /// <remarks>
    /// Section 5.4 requires this for any mapping child whose name is a canonical decimal in range,
    /// "whether or not its containing mapping ultimately qualifies for sequence inference".
    /// Supplying a value at or below the mark addresses an existing position and must not raise it.
    /// </remarks>
    public void Supply(long value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, MinOrderingValue);

        if (value > HighWaterMark)
        {
            HighWaterMark = value;
        }
    }

    /// <summary>
    /// Rebases one explicit item under <c>merge=append</c>: raises the mark to at least the supplied
    /// value, then allocates the item's new value as <c>high-water + 1</c>.
    /// </summary>
    /// <param name="suppliedValue">The item's original explicit ordering value.</param>
    /// <param name="value">The item's new value, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> on Section 5.4 allocation overflow, as for
    /// <see cref="TryAllocate"/>.</returns>
    /// <remarks>
    /// The caller must present a contribution's items in ascending original ordering value. Doing so
    /// in any other order changes which values the items receive, because each raise affects every
    /// allocation after it. The original value is no longer addressable for a rebased item.
    /// </remarks>
    public bool TryRebase(long suppliedValue, out long value)
    {
        Supply(suppliedValue);

        return TryAllocate(out value);
    }
}
