using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

/// <summary>
/// Pipeline step 9: exposes native-sequence ordering values and ordinary numeric mapping keys as
/// path parts.
/// </summary>
/// <remarks>
/// <para>
/// Numeric mapping keys need nothing done to them — Section 8.7 says that "before that phase,
/// numeric mapping keys are ordinary addressable path parts". A sequence item is the one addressing
/// gap: Section 11 exposes <c>a[0]</c> as <c>a.0</c>, and until that address exists a template, an
/// ignore mask or a reference cannot name an item at all.
/// </para>
/// <para>
/// The step therefore has one structural job. Section 15.1 makes "a mapping child whose name is an
/// in-range canonical ordering value and the sequence item with that value at the same path" one
/// overlay node, so wherever both exist they are combined into a single node that both facets then
/// hold. Combining is what makes the following rule of the same sentence achievable: a
/// <c>(rule, logical path)</c> pair generates at most once. Two nodes at one address would be
/// matched twice, patched independently, and could disagree.
/// </para>
/// <para>
/// The two facets are both kept, holding the same node, rather than collapsing into whichever one
/// looks canonical. Step 11 has not run yet, and it is step 11 that decides whether the mapping
/// projection survives; a mapping that also has a non-numeric child never becomes a sequence, so
/// discarding the mapping projection here would lose data the destination still has to render.
/// </para>
/// </remarks>
public sealed class OrderingValueExposer
{
    private readonly OverlayMerger merger;

    /// <summary>Creates an exposer.</summary>
    /// <param name="merger">
    /// The step-8 merger, which carries the effective Section 16.10 strategy at each path and the
    /// buffer its diagnostics accumulate in.
    /// </param>
    public OrderingValueExposer(OverlayMerger merger)
    {
        ArgumentNullException.ThrowIfNull(merger);

        this.merger = merger;
    }

    /// <summary>Exposes ordering values throughout a merged overlay.</summary>
    /// <param name="root">The overlay produced by step 8.</param>
    public OverlayNode Expose(OverlayNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Expose(root, []);
    }

    /// <summary>
    /// Resolves one name part against a node, treating a canonical ordering value as an address for
    /// the sequence item carrying it.
    /// </summary>
    /// <param name="parent">The node to look inside.</param>
    /// <param name="name">The name part to resolve.</param>
    /// <param name="child">The node at that address, when there is one.</param>
    /// <returns>Whether the address names anything.</returns>
    /// <remarks>
    /// The mapping facet is consulted first, which matters only for an overlay that has not been
    /// exposed: after <see cref="Expose(OverlayNode)"/> the two facets hold the same node at any
    /// address both can answer, so the order of the two lookups is unobservable.
    /// </remarks>
    public static bool TryResolve(
        OverlayNode parent, NamePart name, [NotNullWhen(true)] out OverlayNode? child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(name);

        if (parent.Children.TryGetValue(name, out var mapped))
        {
            child = mapped;
            return true;
        }

        if (OrderingValues.TryRead(name, out var value)
            && parent.Sequence.TryGetValue(value, out var item))
        {
            child = item.Node;
            return true;
        }

        child = null;
        return false;
    }

    /// <summary>Resolves a whole path against an exposed overlay.</summary>
    /// <param name="root">The node the path starts from.</param>
    /// <param name="path">The path components, in order.</param>
    /// <param name="node">The node the path names, when it names one.</param>
    /// <returns>Whether every component resolved.</returns>
    public static bool TryResolve(
        OverlayNode root, IEnumerable<NamePart> path, [NotNullWhen(true)] out OverlayNode? node)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);

        var current = root;

        foreach (var part in path)
        {
            if (!TryResolve(current, part, out var next))
            {
                node = null;
                return false;
            }

            current = next;
        }

        node = current;
        return true;
    }

    private OverlayNode Expose(OverlayNode node, ImmutableArray<NamePart> path)
    {
        var combined = Combine(node, path);
        var children = node.Children;

        foreach (var (name, child) in node.OrderedChildren)
        {
            children = children.SetItem(
                name,
                OrderingValues.TryRead(name, out var value)
                && combined.TryGetValue(value, out var shared)
                    ? shared
                    : Expose(child, path.Add(name)));
        }

        var sequence = node.Sequence;

        foreach (var (value, item) in node.OrderedSequence)
        {
            sequence = sequence.SetItem(
                value,
                item with
                {
                    // Section 15.1: the combined item "keeps the ordering provenance the sequence
                    // item already had", so only the node is replaced.
                    Node = combined.TryGetValue(value, out var shared)
                        ? shared
                        : Expose(item.Node, path.Add(OrderingValues.ToNamePart(value))),
                });
        }

        return OverlayNode.Compose(
            node.Marks,
            node.Payload,
            node.HasExplicitMapping,
            node.HasExplicitSequence,
            children,
            sequence,
            node.Comments,
            node.SequenceHighWater);
    }

    /// <summary>
    /// Combines every mapping child that names an ordering value the sequence already holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result is exposed here, once per combined pair, rather than by the two loops that place
    /// it. Exposing it in each loop would build two structurally equal subtrees, and the whole point
    /// of the step is that there is one node; a later contribution grafted onto one of them would
    /// be invisible through the other address.
    /// </para>
    /// <para>
    /// Values are visited in ascending order so that any diagnostic the merge reports is emitted in
    /// an order that does not depend on how a hash table happened to enumerate.
    /// </para>
    /// </remarks>
    private Dictionary<long, OverlayNode> Combine(OverlayNode node, ImmutableArray<NamePart> path)
    {
        var combined = new Dictionary<long, OverlayNode>();

        foreach (var (value, item) in node.OrderedSequence)
        {
            var name = OrderingValues.ToNamePart(value);

            if (!node.Children.TryGetValue(name, out var child))
            {
                continue;
            }

            var at = path.Add(name);

            // Section 15.1 merges the pair "in source order", which the two position marks give.
            // A tie is not reachable from input — no single source contributes both a sequence item
            // and a numeric mapping child at one path — so the order on a tie is pinned only for
            // determinism: the item is treated as the earlier one, and Section 17.1's "later
            // payload wins" then has no later payload to prefer, leaving what is already in the
            // sequence in place.
            combined[value] = Expose(
                child.Marks.Position >= item.Node.Marks.Position
                    ? merger.MergeAt(item.Node, child, at)
                    : merger.MergeAt(child, item.Node, at),
                at);
        }

        return combined;
    }
}
