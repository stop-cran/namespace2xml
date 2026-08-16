using Namespace2Xml.Cli;

namespace Namespace2Xml.Budgets;

/// <summary>
/// The global half of the Section 7.3 two-tier budget: the running totals no parse worker may see,
/// admitted one source at a time in the order Section 7.3 fixes, plus the pipeline-phase bounds of
/// Section 23.
/// </summary>
/// <remarks>
/// <para>
/// Everything this type does happens after the parse-phase join, on one thread, in one order. That
/// is the whole reason it exists separately from <see cref="SourceBudget"/>: Section 23 requires
/// that "concurrent work never races to consume a shared budget", and the way to guarantee that is
/// for the shared budget to be reachable only from single-threaded code.
/// </para>
/// <para>
/// Sources are admitted in the Section 7.3 ordered input stream -- scheme files in <c>-s</c> order,
/// then input files in <c>-i</c> order, then command-line variables in <c>-v</c> token order. The
/// first source that would cross a global bound is refused and reported; every later source in that
/// stream is refused silently, because Section 22 makes <c>LIMIT001</c> once per invocation.
/// </para>
/// </remarks>
public sealed class GlobalBudget
{
    private readonly ResourceLimits limits;
    private readonly Dictionary<ResourceBound, long> consumed = [];
    private long totalInputBytes;
    private long nodes;
    private long comments;
    private long commentBytes;

    /// <summary>Creates a budget for one invocation.</summary>
    /// <param name="limits">The configured bounds.</param>
    public GlobalBudget(ResourceLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        this.limits = limits;
    }

    /// <summary>
    /// Whether a source has already crossed a global input bound, closing the Section 7.3 stream.
    /// </summary>
    public bool InputStreamClosed { get; private set; }

    /// <summary>
    /// Admits one source's contribution to the global input bounds, in Section 7.3 stream order.
    /// </summary>
    /// <param name="tally">What the source's parse worker reported.</param>
    /// <param name="sourceOrdinal">The source's position in the Section 7.3 ordered input stream.</param>
    /// <param name="fault">
    /// The crossing to report, set only for the first source that crosses. It is
    /// <see langword="null"/> both when the source is admitted and when the stream was already
    /// closed, so a caller that reports every non-null fault reports exactly one.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the source contributes its parsed model. Once this returns
    /// <see langword="false"/>, it returns <see langword="false"/> for every later source, "including
    /// later sources of a different kind".
    /// </returns>
    public bool TryAdmit(SourceTally tally, long sourceOrdinal, out BudgetFault? fault)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOrdinal);

        fault = null;

        if (InputStreamClosed)
        {
            return false;
        }

        // Section 11.1 breaks a tie between bounds crossed at one position by bound name, and a
        // source's contribution is one position, so the candidates are gathered before one is chosen.
        BudgetFault? crossing = null;

        Consider(tally.InputBytes, totalInputBytes, limits.MaxTotalInputBytes, ResourceBound.MaxTotalInputBytes);
        Consider(tally.Nodes, nodes, limits.MaxNodes, ResourceBound.MaxNodes);
        Consider(tally.Comments, comments, limits.MaxComments, ResourceBound.MaxComments);
        Consider(tally.CommentBytes, commentBytes, limits.MaxCommentBytes, ResourceBound.MaxCommentBytes);

        if (crossing is { } found)
        {
            InputStreamClosed = true;
            fault = found;

            return false;
        }

        totalInputBytes += tally.InputBytes;
        nodes += tally.Nodes;
        comments += tally.Comments;
        commentBytes += tally.CommentBytes;

        return true;

        void Consider(long contribution, long running, long limit, ResourceBound bound)
        {
            if (contribution > limit - running)
            {
                crossing = crossing is { } existing
                    ? BudgetFaultOrder.Earlier(existing, new BudgetFault(bound, limit, sourceOrdinal))
                    : new BudgetFault(bound, limit, sourceOrdinal);
            }
        }
    }

    /// <summary>
    /// Consumes a pipeline-phase bound, in the normative pipeline order of Section 23.
    /// </summary>
    /// <param name="bound">The bound to consume.</param>
    /// <param name="amount">How much to consume.</param>
    /// <param name="fault">The crossing, when this returns <see langword="false"/>.</param>
    /// <returns><see langword="false"/> when the amount would cross the bound.</returns>
    /// <remarks>
    /// Section 23: "Accounting occurs before allocation or expansion whenever possible." Nothing is
    /// consumed when the bound would be crossed, so a refused caller may report and stop without
    /// having to unwind a partial charge.
    /// </remarks>
    public bool TryConsume(ResourceBound bound, long amount, out BudgetFault fault)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        var limit = LimitOf(bound);
        var running = consumed.GetValueOrDefault(bound);

        if (amount > limit - running)
        {
            fault = new BudgetFault(bound, limit);

            return false;
        }

        consumed[bound] = running + amount;
        fault = default;

        return true;
    }

    /// <summary>
    /// Checks a depth against a pipeline-phase bound without consuming it, for
    /// <c>--max-reference-depth</c> and <c>--max-wildcard-iterations</c>, which are levels rather
    /// than totals.
    /// </summary>
    /// <param name="bound">The bound to check.</param>
    /// <param name="level">The level being entered.</param>
    /// <param name="fault">The crossing, when this returns <see langword="false"/>.</param>
    /// <returns><see langword="false"/> when the level exceeds the bound.</returns>
    public bool TryEnter(ResourceBound bound, long level, out BudgetFault fault)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);

        var limit = LimitOf(bound);

        if (level > limit)
        {
            fault = new BudgetFault(bound, limit);

            return false;
        }

        fault = default;

        return true;
    }

    /// <summary>How much of a pipeline-phase bound has been consumed.</summary>
    /// <param name="bound">The bound to report.</param>
    /// <returns>The running total.</returns>
    public long Consumed(ResourceBound bound) => consumed.GetValueOrDefault(bound);

    /// <summary>The configured value of a bound.</summary>
    /// <param name="bound">The bound to report.</param>
    /// <returns>The configured limit.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The bound is not a declared member.</exception>
    public long LimitOf(ResourceBound bound) => bound switch
    {
        ResourceBound.MaxInputBytes => limits.MaxInputBytes,
        ResourceBound.MaxDepth => limits.MaxDepth,
        ResourceBound.MaxXmlAttributes => limits.MaxXmlAttributes,
        ResourceBound.MaxTotalInputBytes => limits.MaxTotalInputBytes,
        ResourceBound.MaxNodes => limits.MaxNodes,
        ResourceBound.MaxComments => limits.MaxComments,
        ResourceBound.MaxCommentBytes => limits.MaxCommentBytes,
        ResourceBound.MaxWildcardRules => limits.MaxWildcardRules,
        ResourceBound.MaxWildcardCandidates => limits.MaxWildcardCandidates,
        ResourceBound.MaxGenerated => limits.MaxGenerated,
        ResourceBound.MaxWildcardIterations => limits.MaxWildcardIterations,
        ResourceBound.MaxReferenceDepth => limits.MaxReferenceDepth,
        ResourceBound.MaxOutputs => limits.MaxOutputs,
        ResourceBound.MaxTotalOutputBytes => limits.MaxTotalOutputBytes,
        _ => throw new ArgumentOutOfRangeException(nameof(bound)),
    };
}
