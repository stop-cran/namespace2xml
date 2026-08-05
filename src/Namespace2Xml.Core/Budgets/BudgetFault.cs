namespace Namespace2Xml.Budgets;

/// <summary>
/// A crossed bound, carrying everything Section 22 needs to decide which of several crossings is
/// the one reported.
/// </summary>
/// <param name="Bound">The bound that was crossed.</param>
/// <param name="Limit">The configured value of that bound.</param>
/// <param name="SourceOrdinal">
/// The source's position in the Section 7.3 ordered input stream, or <c>-1</c> for a crossing that
/// belongs to no source, which every pipeline-phase bound does.
/// </param>
/// <param name="DocumentOrder">Position within the source, zero when the source has no finer position.</param>
/// <param name="ElementOrder">Position within the document, zero when there is no finer position.</param>
/// <remarks>
/// Section 22 makes <c>LIMIT001</c> once per invocation, so when several bounds are crossed the run
/// reports exactly one. The order is source, then position within the source, then position within
/// the document, then the bound's own name -- see <see cref="BudgetFaultOrder"/>.
/// </remarks>
public readonly record struct BudgetFault(
    ResourceBound Bound,
    long Limit,
    long SourceOrdinal = -1,
    long DocumentOrder = 0,
    long ElementOrder = 0)
{
    /// <summary>The Section 6.2 option spelling of the crossed bound.</summary>
    public string Spelling => ResourceBoundNames.Spelling(Bound);
}

/// <summary>
/// The Section 22 attribution order: "the earliest under CLI source order as defined in Section 7.3,
/// then document order within that source, then element order, then the bound name compared as
/// unsigned UTF-8 bytes".
/// </summary>
/// <remarks>
/// <para>
/// A fault with no source ordinal sorts before every fault that has one, because a pipeline-phase
/// bound is only ever compared with another pipeline-phase bound: the parse phase has already
/// succeeded by the time any of them can be consumed. Ordering them consistently anyway keeps the
/// comparer a total order, which <see cref="Comparer{T}"/> callers are entitled to assume.
/// </para>
/// <para>
/// The bound names are ASCII, so ordinal string comparison is the unsigned UTF-8 byte comparison
/// Section 22 specifies. Culture-sensitive comparison is not: it would put <c>--max-comment-bytes</c>
/// after <c>--max-comments</c> under some collations, and which bound a run blames would then depend
/// on the operating system's locale.
/// </para>
/// </remarks>
public sealed class BudgetFaultOrder : IComparer<BudgetFault>
{
    /// <summary>The single instance.</summary>
    public static BudgetFaultOrder Instance { get; } = new();

    /// <summary>The earlier of two faults under Section 22 attribution order.</summary>
    public static BudgetFault Earlier(BudgetFault left, BudgetFault right) =>
        Instance.Compare(left, right) <= 0 ? left : right;

    /// <inheritdoc/>
    public int Compare(BudgetFault x, BudgetFault y)
    {
        var result = x.SourceOrdinal.CompareTo(y.SourceOrdinal);

        if (result != 0)
        {
            return result;
        }

        result = x.DocumentOrder.CompareTo(y.DocumentOrder);

        if (result != 0)
        {
            return result;
        }

        result = x.ElementOrder.CompareTo(y.ElementOrder);

        return result != 0
            ? result
            : string.CompareOrdinal(
                ResourceBoundNames.Spelling(x.Bound),
                ResourceBoundNames.Spelling(y.Bound));
    }
}
