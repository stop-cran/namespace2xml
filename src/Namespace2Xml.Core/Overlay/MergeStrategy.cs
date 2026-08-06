using System.Collections.Immutable;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

/// <summary>
/// The Section 16.10 literal-path input merge strategies.
/// </summary>
public enum MergeStrategy
{
    /// <summary>
    /// Section 16.10 <c>deep</c>, the default: mappings merge recursively, implicit sequence items
    /// concatenate, explicit ordering values patch, later payloads override earlier payloads, and
    /// scalar and container contributions coexist until output projection.
    /// </summary>
    Deep,

    /// <summary>Section 16.10 <c>replace</c>: the later complete value replaces the earlier one.</summary>
    Replace,

    /// <summary>
    /// Section 16.10 <c>append</c>: every item in the later sequence contribution is rebased in
    /// ascending original ordering value onto fresh implicit values above the high-water mark.
    /// </summary>
    Append,

    /// <summary>
    /// Section 16.10 <c>error</c>: after entries inside each source contribution have been folded,
    /// any distinct second source or generated contribution at the path is an error.
    /// </summary>
    Error,
}

/// <summary>
/// The effective Section 16.10 strategy at each path.
/// </summary>
/// <remarks>
/// Section 16.10: "A <c>merge</c> directive governs only the node it matches; descendants use their
/// independently effective strategy, defaulting to <c>deep</c>." The map is therefore an exact
/// lookup and never inherits down the tree, which is why it is a dictionary rather than a prefix
/// search.
/// </remarks>
public sealed class MergeStrategyMap
{
    private readonly ImmutableDictionary<QualifiedName, MergeStrategy> strategies;

    private MergeStrategyMap(ImmutableDictionary<QualifiedName, MergeStrategy> strategies) =>
        this.strategies = strategies;

    /// <summary>The map in which every path uses the Section 16.10 default.</summary>
    public static MergeStrategyMap Default { get; } =
        new(ImmutableDictionary<QualifiedName, MergeStrategy>.Empty);

    /// <summary>Builds a map from compiled literal-path <c>merge</c> directives.</summary>
    /// <param name="strategies">The strategy declared at each literal path.</param>
    public static MergeStrategyMap Create(
        IEnumerable<KeyValuePair<QualifiedName, MergeStrategy>> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);

        return new MergeStrategyMap(ImmutableDictionary.CreateRange(strategies));
    }

    /// <summary>The effective strategy at one path.</summary>
    /// <param name="path">The literal path, from the overlay root.</param>
    /// <remarks>
    /// The empty path addresses the overlay root, which has no name. Section 16.10 requires a
    /// <c>merge</c> directive to name a literal path, and Appendix A.2 spells a name as "one or more
    /// components", so no directive can reach the root and it always uses the default.
    /// </remarks>
    public MergeStrategy For(ImmutableArray<NamePart> path) =>
        !strategies.IsEmpty
        && !path.IsEmpty
        && strategies.TryGetValue(new QualifiedName(path), out var strategy)
            ? strategy
            : MergeStrategy.Deep;
}
