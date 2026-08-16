using Namespace2Xml.Cli;

namespace Namespace2Xml.Budgets;

/// <summary>
/// What one source contributed to the global input bounds: the only thing a parse worker reports to
/// the Section 7.3 join.
/// </summary>
/// <param name="InputBytes">Bytes read, or the UTF-8 length of a command-line variable.</param>
/// <param name="Nodes">Parsed nodes, before any generated entry.</param>
/// <param name="Comments">Retained comments.</param>
/// <param name="CommentBytes">Decoded comment bytes.</param>
public readonly record struct SourceTally(
    long InputBytes,
    long Nodes,
    long Comments,
    long CommentBytes)
{
    /// <summary>A source that contributed nothing.</summary>
    public static SourceTally Empty => default;
}

/// <summary>
/// The per-source half of the Section 7.3 two-tier budget: a parse worker's own counters, and the
/// bounds that are enforced within one source and are "never cumulative across sources".
/// </summary>
/// <remarks>
/// <para>
/// This type deliberately cannot see a global total. Section 7.3 requires that a parser accumulate
/// only its own source's contribution, because a worker that could read a running total would
/// produce a different outcome depending on which worker got there first, and Section 24 forbids
/// results that depend on thread scheduling. The separation is structural: there is no field, no
/// constructor parameter, and no method here through which another source's contribution could
/// arrive, and <c>SourceBudgetIsIsolatedFromGlobalTotals</c> in the test suite fails if one appears.
/// </para>
/// <para>
/// Knowing a configured <i>bound</i> is not the same as knowing a running <i>total</i>, so holding
/// <see cref="ResourceLimits"/> is sound: the values in it come from the command line and are the
/// same for every worker.
/// </para>
/// <para>
/// Global counts are tallied here and checked nowhere here. That is safe because
/// <c>--max-input-bytes</c> bounds every source individually, so no single worker can produce an
/// unbounded tally before the join gets a chance to reject it.
/// </para>
/// </remarks>
public sealed class SourceBudget
{
    private readonly ResourceLimits limits;
    private long inputBytes;
    private long nodes;
    private long comments;
    private long commentBytes;

    /// <summary>Creates a budget for one source.</summary>
    /// <param name="limits">The configured bounds, identical for every source.</param>
    /// <param name="sourceOrdinal">The source's position in the Section 7.3 ordered input stream.</param>
    public SourceBudget(ResourceLimits limits, long sourceOrdinal)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOrdinal);

        this.limits = limits;
        SourceOrdinal = sourceOrdinal;
    }

    /// <summary>The source's position in the Section 7.3 ordered input stream.</summary>
    public long SourceOrdinal { get; }

    /// <summary>
    /// The first per-source bound this source crossed, or <see langword="null"/> if it crossed none.
    /// </summary>
    /// <remarks>
    /// The first is kept rather than the earliest by Section 11.1 order, because a parser reaches
    /// positions in document order: the first crossing it sees is already the earliest one.
    /// </remarks>
    public BudgetFault? Fault { get; private set; }

    /// <summary>What this source contributed, for the Section 7.3 join.</summary>
    public SourceTally Tally => new(inputBytes, nodes, comments, commentBytes);

    /// <summary>
    /// Accounts for bytes read from this source against the per-file bound of Section 23.
    /// </summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="documentOrder">Position within the source, for Section 11.1 attribution.</param>
    /// <returns><see langword="false"/> when this source crosses <c>--max-input-bytes</c>.</returns>
    public bool TryAddInputBytes(long count, long documentOrder = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count > limits.MaxInputBytes - inputBytes)
        {
            return Cross(ResourceBound.MaxInputBytes, limits.MaxInputBytes, documentOrder);
        }

        inputBytes += count;

        return true;
    }

    /// <summary>
    /// Checks a nesting depth against the per-document bound of Section 23.
    /// </summary>
    /// <param name="depth">The depth being entered, counted from zero at the document root.</param>
    /// <param name="documentOrder">Position within the source, for Section 11.1 attribution.</param>
    /// <returns><see langword="false"/> when this source crosses <c>--max-depth</c>.</returns>
    /// <remarks>
    /// Depth is a level, not a running total, so it is checked rather than accumulated. Section 7.3
    /// makes it explicitly non-cumulative across sources.
    /// </remarks>
    public bool TryEnterDepth(long depth, long documentOrder = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);

        return depth <= limits.MaxDepth
            || Cross(ResourceBound.MaxDepth, limits.MaxDepth, documentOrder);
    }

    /// <summary>
    /// Checks an element's attribute count against the per-element bound of Sections 11.1 and 16.2.
    /// </summary>
    /// <param name="count">The number of attributes on one element.</param>
    /// <param name="documentOrder">Position within the source, for Section 11.1 attribution.</param>
    /// <param name="elementOrder">Position within the document, for Section 11.1 attribution.</param>
    /// <returns><see langword="false"/> when this source crosses <c>--max-xml-attributes</c>.</returns>
    public bool TryAddXmlAttributes(long count, long documentOrder = 0, long elementOrder = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        return count <= limits.MaxXmlAttributes
            || Cross(ResourceBound.MaxXmlAttributes, limits.MaxXmlAttributes, documentOrder, elementOrder);
    }

    /// <summary>Tallies parsed nodes, which only the Section 7.3 join can judge.</summary>
    /// <param name="count">The number of nodes.</param>
    public void AddNodes(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        nodes += count;
    }

    /// <summary>Tallies retained comments and their decoded bytes, for the Section 7.3 join.</summary>
    /// <param name="count">The number of comments.</param>
    /// <param name="bytes">Their total decoded byte length.</param>
    public void AddComments(long count, long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        comments += count;
        commentBytes += bytes;
    }

    private bool Cross(ResourceBound bound, long limit, long documentOrder, long elementOrder = 0)
    {
        Fault ??= new BudgetFault(bound, limit, SourceOrdinal, documentOrder, elementOrder);

        return false;
    }
}
