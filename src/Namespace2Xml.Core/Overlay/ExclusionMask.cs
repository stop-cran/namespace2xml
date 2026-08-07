using System.Collections.Immutable;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

/// <summary>
/// The union of a run's Section 8.6 permanent exclusion masks.
/// </summary>
/// <remarks>
/// <para>
/// Section 8.6 calls a mask "a permanent run-wide subtree exclusion mask" and suppresses matching
/// contributions "regardless of whether it appears before or after the ignore entry". The union is
/// therefore assembled from every source before any of them is pruned, and a mask declared in the
/// last input suppresses a path contributed by the first. This is the one exception the
/// specification grants to universal later-source precedence.
/// </para>
/// <para>
/// A pattern matches a path <em>prefix</em>. Section 8.6 suppresses "suppressed paths and
/// descendants", so the pattern's components are matched against the concrete path's leading
/// components and everything below the matched depth goes with it: <c>!a</c> removes <c>a</c> and
/// <c>a.x.y</c> alike. The corollary is that <c>!a.*</c> does not remove <c>a</c>, because a
/// two-component pattern cannot match a one-component path.
/// </para>
/// </remarks>
public sealed class ExclusionMask
{
    private readonly ImmutableArray<QualifiedName> patterns;

    private ExclusionMask(ImmutableArray<QualifiedName> patterns) => this.patterns = patterns;

    /// <summary>A mask that suppresses nothing.</summary>
    public static ExclusionMask None { get; } = new([]);

    /// <summary>Whether this mask suppresses nothing, so that pruning can be skipped.</summary>
    public bool IsEmpty => patterns.IsEmpty;

    /// <summary>Forms the union of a run's masks.</summary>
    /// <param name="patterns">Every mask pattern declared anywhere in the run.</param>
    /// <returns>The union, or <see cref="None"/> when there are none.</returns>
    public static ExclusionMask Of(IEnumerable<QualifiedName> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var collected = patterns.ToImmutableArray();

        return collected.IsEmpty ? None : new ExclusionMask(collected);
    }

    /// <summary>
    /// Whether a path is suppressed, either in its own right or as the descendant of one.
    /// </summary>
    /// <param name="path">The absolute path, from the overlay root.</param>
    /// <returns><see langword="true"/> when no contribution at the path may survive.</returns>
    public bool Suppresses(ImmutableArray<NamePart> path)
    {
        foreach (var pattern in patterns)
        {
            if (WildcardMatch.TryMatchPrefix(pattern.Parts, pattern.Parts.Length, path, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Prunes every suppressed subtree from one contribution.</summary>
    /// <param name="contribution">The contribution's overlay root.</param>
    /// <returns>The overlay with suppressed paths removed and reservations preserved.</returns>
    /// <remarks>
    /// Section 8.6 requires that masked contributions "still reserve any canonical ordering value
    /// for high-water stability, then are discarded before literal-path merge validation". Pruning
    /// therefore carries each surviving node's high-water mark across untouched. That is sufficient
    /// rather than merely convenient: the mark is stored on the node, and both
    /// <see cref="OverlayNode.WithChild"/> and <see cref="OverlayNode.WithSequenceItem"/> raise it
    /// as the child or item is placed, so it already dominates every value a mask can remove.
    /// Deriving the mark from the survivors instead would let an ignore entry renumber the items
    /// around it, and a later <c>append</c> would reuse a masked item's ordering value.
    /// </remarks>
    public OverlayNode Apply(OverlayNode contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);

        return IsEmpty ? contribution : Prune(contribution, []);
    }

    private OverlayNode Prune(OverlayNode node, ImmutableArray<NamePart> path)
    {
        var children = node.Children;
        var sequence = node.Sequence;
        StableOrderingKey? mappingFromDescendants = null;
        StableOrderingKey? sequenceFromItems = null;

        foreach (var (name, child) in node.Children)
        {
            var childPath = path.Add(name);

            if (Suppresses(childPath))
            {
                children = children.Remove(name);
                continue;
            }

            var pruned = Prune(child, childPath);

            if (!ReferenceEquals(pruned, child))
            {
                children = children.SetItem(name, pruned);
            }

            // Section 8.6 keeps a masked contribution's high-water reservation, so a node emptied by
            // pruning stays in the tree. It is no longer a "descendant contribution that requires
            // mapping shape", though: nothing survives in it to require anything, so it must not go
            // on refreshing this node's Section 4.4 mapping shape-mark.
            if (!pruned.IsEmpty)
            {
                mappingFromDescendants = Later(mappingFromDescendants, pruned.Marks.Latest);
            }
        }

        foreach (var (value, item) in node.Sequence)
        {
            // Section 15.1 makes a sequence item and the mapping child spelled with its ordering
            // value "one structural overlay node", so a mask written against that spelling has to
            // reach the item as well as the child.
            var name = OrderingValues.ToNamePart(value);
            var itemPath = path.Add(name);

            if (Suppresses(itemPath))
            {
                sequence = sequence.Remove(value);
                continue;
            }

            // The two facets share one node object once step 9 has merged them, so pruning the item
            // separately would repeat the whole subtree traversal. Each such level doubles the work,
            // which is exponential in depth rather than merely wasteful.
            var pruned = node.Children.TryGetValue(name, out var twin) && ReferenceEquals(twin, item.Node)
                ? children.TryGetValue(name, out var prunedTwin) ? prunedTwin : Prune(item.Node, itemPath)
                : Prune(item.Node, itemPath);

            if (!ReferenceEquals(pruned, item.Node))
            {
                sequence = sequence.SetItem(value, item with { Node = pruned });
            }

            if (!pruned.IsEmpty)
            {
                sequenceFromItems = Later(sequenceFromItems, pruned.Marks.Latest);
            }
        }
        var marks = node.Marks.AfterMasking(mappingFromDescendants, sequenceFromItems);

        return ReferenceEquals(children, node.Children)
            && ReferenceEquals(sequence, node.Sequence)
            && marks.Equals(node.Marks)
            ? node
            : OverlayNode.Compose(
                marks,
                node.Payload,
                node.HasExplicitMapping,
                node.HasExplicitSequence,
                children,
                sequence,
                node.Comments,
                node.SequenceHighWater);
    }

    private static StableOrderingKey? Later(StableOrderingKey? left, StableOrderingKey? right) =>
        (left, right) switch
        {
            (null, null) => null,
            (null, { } only) => only,
            ({ } only, null) => only,
            ({ } a, { } b) => StableOrderingKey.Later(a, b),
        };
}
