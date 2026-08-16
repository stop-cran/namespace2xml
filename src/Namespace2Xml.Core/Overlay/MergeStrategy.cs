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
    private readonly MergeStrategy root;
    private readonly bool rootDeclared;

    private MergeStrategyMap(
        ImmutableDictionary<QualifiedName, MergeStrategy> strategies,
        MergeStrategy root,
        bool rootDeclared)
    {
        this.strategies = strategies;
        this.root = root;
        this.rootDeclared = rootDeclared;
    }

    /// <summary>The map in which every path uses the Section 16.10 default.</summary>
    public static MergeStrategyMap Default { get; } =
        new(ImmutableDictionary<QualifiedName, MergeStrategy>.Empty, MergeStrategy.Deep, false);

    /// <summary>Builds a map from compiled literal-path <c>merge</c> directives.</summary>
    /// <param name="strategies">The strategy declared at each literal path.</param>
    /// <param name="root">
    /// The strategy declared by a directive with no path at all. Section 16.10 spells the directive
    /// <c>[path.]merge=…</c>, so the path is optional and a bare <c>merge=replace</c> governs the
    /// overlay root.
    /// </param>
    /// <param name="rootDeclared">
    /// Whether <paramref name="root"/> came from a directive. Section 8.7 asks whether "no explicit
    /// <c>merge</c> directive applies", which the strategy alone cannot answer: a declared
    /// <c>merge=deep</c> and no directive at all produce the same effective strategy.
    /// </param>
    /// <remarks>
    /// The root is a separate parameter because Appendix A.2 spells a qname as "one or more
    /// components": the root has no name, so it cannot be a key of the dictionary. Modelling it as
    /// an absent key instead would make a declared root strategy indistinguishable from no
    /// declaration, which is exactly the silence this parameter exists to break.
    /// </remarks>
    public static MergeStrategyMap Create(
        IEnumerable<KeyValuePair<QualifiedName, MergeStrategy>> strategies,
        MergeStrategy root = MergeStrategy.Deep,
        bool rootDeclared = false)
    {
        ArgumentNullException.ThrowIfNull(strategies);

        // Last wins, per Section 15.2: "A later matching directive overrides an earlier matching
        // directive for the same effective setting." ImmutableDictionary.CreateRange throws on a
        // repeated key, so building the map that way would turn two 'merge' directives at one path
        // into an unhandled exception rather than an override.
        var builder = ImmutableDictionary.CreateBuilder<QualifiedName, MergeStrategy>();

        foreach (var (path, strategy) in strategies)
        {
            builder[path] = strategy;
        }

        return new MergeStrategyMap(builder.ToImmutable(), root, rootDeclared);
    }

    /// <summary>The effective strategy at one path.</summary>
    /// <param name="path">The literal path, from the overlay root.</param>
    public MergeStrategy For(ImmutableArray<NamePart> path) =>
        path.IsDefaultOrEmpty
            ? root
            : strategies.TryGetValue(new QualifiedName(path), out var strategy)
                ? strategy
                : MergeStrategy.Deep;

    /// <summary>Whether a <c>merge</c> directive was declared at exactly this path.</summary>
    /// <param name="path">The literal path, from the overlay root.</param>
    /// <remarks>
    /// Section 8.7 warns only when "no explicit <c>merge</c> directive applies", so the question is
    /// about the directive and not about the resulting strategy. A directive at an ancestor does
    /// not answer it either: Section 16.10 looks a strategy up at the exact path.
    /// </remarks>
    public bool Declares(ImmutableArray<NamePart> path) =>
        path.IsDefaultOrEmpty ? rootDeclared : strategies.ContainsKey(new QualifiedName(path));
}
