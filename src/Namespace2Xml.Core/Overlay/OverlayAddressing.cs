using System.Collections.Immutable;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

/// <summary>
/// How a merged overlay is walked when something needs to enumerate the paths it contains.
/// </summary>
/// <remarks>
/// Section 12.4 and Section 14.1 both expand a pattern by enumerating the concrete paths at the
/// depth of its last wildcard-containing part, and Section 24 requires the two to agree: a wildcard
/// template and a wildcard output selector written with the same pattern must see the same items,
/// or a selector could name a path no rule could ever generate into. One walk, used by both, is
/// what makes that true by construction rather than by coincidence.
/// </remarks>
internal static class OverlayAddressing
{
    /// <summary>
    /// The distinct logical path nodes at one depth, in a deterministic order.
    /// </summary>
    /// <param name="node">The node to walk from.</param>
    /// <param name="depth">How many components below <paramref name="node"/> to descend.</param>
    /// <returns>One path per distinct node at that depth.</returns>
    /// <remarks>
    /// Section 12.4: "For candidate accounting, an item is a distinct logical path node. If the
    /// rule's last wildcard-containing part is at depth <c>k</c>, eligible items are the distinct
    /// depth-<c>k</c> prefixes of existing paths, not every deeper descendant."
    /// </remarks>
    public static IEnumerable<ImmutableArray<NamePart>> Candidates(OverlayNode node, int depth) =>
        Walk(node, [], depth);

    /// <summary>
    /// The distinct logical path nodes at one depth that one pattern admits as candidates.
    /// </summary>
    /// <param name="node">The node to walk from.</param>
    /// <param name="pattern">The rule or selector parts, of at least <paramref name="depth"/> length.</param>
    /// <param name="depth">How many components below <paramref name="node"/> to descend.</param>
    /// <returns>
    /// The subsequence of <see cref="Candidates(OverlayNode,int)"/> whose leading literal parts
    /// equal the pattern's, in the same order.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is Section 12.4's second candidate condition — "every literal name part before that
    /// point equals the corresponding item part" — expressed as a walk rather than as a filter over
    /// the whole tree. The two are the same set, because a literal part admits at most one address:
    /// Section 15.1 makes a mapping child and the sequence item of that ordering value one node, so
    /// there is no second node the part could also name.
    /// </para>
    /// <para>
    /// Expressing it as a filter is what the evaluator used to do, and it enumerated every path in
    /// the model once per rule per wave — including the subtrees the rule's own first part already
    /// excluded. The cost was quadratic in the wrong pair of quantities, rules times unrelated tree
    /// size, and Section 23 asks for bounded work rather than for bounded output.
    /// </para>
    /// </remarks>
    public static IEnumerable<ImmutableArray<NamePart>> Candidates(
        OverlayNode node, ImmutableArray<NamePart> pattern, int depth)
    {
        ArgumentNullException.ThrowIfNull(node);

        return Walk(node, pattern, [], depth);
    }

    /// <summary>
    /// Every address one node answers, each exactly once.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The addressable children, mapping children first and then sequence items.</returns>
    /// <remarks>
    /// A sequence item is addressable by its Section 5.4 ordering value, and Section 15.1 makes the
    /// item and the mapping child of that name one node, so an address that both facets answer is
    /// yielded once. Yielding it twice would charge one logical item two candidate checks and let
    /// one <c>(rule, path)</c> pair generate twice.
    /// <para>
    /// Removing the check is not observable today, because <see cref="WildcardEvaluator"/>'s
    /// <c>considered</c> set rejects the repeated pair before it is charged and Section 14.1
    /// deduplicates concrete instances by literalized selector. It is kept because it states the
    /// Section 12.4 rule at the point the rule is about: what an item is. Do not write a test for
    /// the duplicate — it would pin the deduplication, which is asserted elsewhere, rather than
    /// this.
    /// </para>
    /// </remarks>
    public static IEnumerable<KeyValuePair<NamePart, OverlayNode>> Addresses(OverlayNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        foreach (var child in node.OrderedChildren)
        {
            yield return child;
        }

        foreach (var (value, item) in node.OrderedSequence)
        {
            var name = OrderingValues.ToNamePart(value);

            if (!node.Children.ContainsKey(name))
            {
                yield return KeyValuePair.Create(name, item.Node);
            }
        }
    }

    private static IEnumerable<ImmutableArray<NamePart>> Walk(
        OverlayNode node, ImmutableArray<NamePart> path, int remaining)
    {
        if (remaining == 0)
        {
            yield return path;
            yield break;
        }

        foreach (var (name, child) in Addresses(node))
        {
            foreach (var found in Walk(child, path.Add(name), remaining - 1))
            {
                yield return found;
            }
        }
    }

    private static IEnumerable<ImmutableArray<NamePart>> Walk(
        OverlayNode node, ImmutableArray<NamePart> pattern, ImmutableArray<NamePart> path, int remaining)
    {
        if (remaining == 0)
        {
            yield return path;
            yield break;
        }

        var part = pattern[path.Length];

        if (!WildcardMatch.HasWildcard(part))
        {
            if (TryAddress(node, part, out var only))
            {
                foreach (var found in Walk(only, pattern, path.Add(part), remaining - 1))
                {
                    yield return found;
                }
            }

            yield break;
        }

        foreach (var (name, child) in Addresses(node))
        {
            foreach (var found in Walk(child, pattern, path.Add(name), remaining - 1))
            {
                yield return found;
            }
        }
    }

    /// <summary>The one node a name addresses, under the same unification <see cref="Addresses"/> uses.</summary>
    /// <param name="node">The node to look inside.</param>
    /// <param name="name">The name part.</param>
    /// <param name="addressed">The node it names, when there is one.</param>
    private static bool TryAddress(
        OverlayNode node, NamePart name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out OverlayNode? addressed)
    {
        if (node.Children.TryGetValue(name, out addressed))
        {
            return true;
        }

        if (OrderingValues.TryRead(name, out var value)
            && node.Sequence.TryGetValue(value, out var item))
        {
            addressed = item.Node;
            return true;
        }

        addressed = null;
        return false;
    }
}
