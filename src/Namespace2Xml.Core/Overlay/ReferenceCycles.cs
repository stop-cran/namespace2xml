namespace Namespace2Xml.Overlay;

/// <summary>
/// Section 22's canonical form for the identity of a reference cycle.
/// </summary>
/// <remarks>
/// <para>
/// Section 22 counts <c>REFERENCE003</c> "once per canonically distinct reachable cycle", and fixes
/// the canonical form: "Rotate the ring so its lexicographically smallest canonical path under
/// unsigned UTF-8 byte order is first; when the same smallest path appears more than once, choose
/// the lexicographically smallest resulting rotated sequence."
/// </para>
/// <para>
/// Two resolvers detect cycles — one over the overlay at step 15 and one over scheme entries at
/// step 1 — and a ring is the same ring whichever found it. The rule lives here so that it cannot
/// be stated twice and drift, which would make the same defect report under two identities
/// depending on where it was written.
/// </para>
/// </remarks>
internal static class ReferenceCycles
{
    /// <summary>Chooses the Section 22 rotation of a ring.</summary>
    /// <param name="ring">The ring's canonical member paths, in ring order. Never empty.</param>
    /// <returns>The offset the canonical rotation starts at.</returns>
    internal static int LeastRotation(string[] ring)
    {
        var least = 0;

        for (var i = 1; i < ring.Length; i++)
        {
            // CompareRotations weighs first members first, so this one comparison is the whole
            // Section 22 rule: least first member, and the whole sequence where those tie.
            if (CompareRotations(ring, i, least) < 0)
            {
                least = i;
            }
        }

        return least;
    }

    /// <summary>Spells a rotated ring's identity.</summary>
    /// <param name="order">The ring's members, already rotated into canonical order.</param>
    /// <returns>An injective spelling of the ring.</returns>
    /// <remarks>
    /// The identity of a ring is the ring, not its printed form. A canonical path escapes the
    /// delimiter, <c>=</c>, <c>}</c>, <c>*</c> and the line terminators, but not a space, a hyphen
    /// or a greater-than sign, so a member may legitimately contain the text a display form joins
    /// on: <c>["a -&gt; b", "c"]</c> and <c>["a", "b -&gt; c"]</c> print identically. Deduplicating
    /// on the printed chain would silently discard one of two genuinely distinct cycles, which is
    /// the same failure <c>CanonicalPath</c> documents for a non-injective path spelling. Each
    /// member is therefore length-prefixed and the prose is built separately.
    /// </remarks>
    internal static string Identity(IEnumerable<string> order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return string.Concat(order.Select(member => $"{member.Length}\u0000{member}"));
    }

    /// <summary>
    /// Compares the two rotations of <paramref name="ring"/> beginning at the given offsets, under
    /// the Section 22 unsigned UTF-8 byte order.
    /// </summary>
    /// <param name="ring">The ring's canonical member paths, in ring order.</param>
    /// <param name="left">The offset the left rotation starts at.</param>
    /// <param name="right">The offset the right rotation starts at.</param>
    /// <returns>A negative value, zero, or a positive value in the usual comparison convention.</returns>
    private static int CompareRotations(string[] ring, int left, int right)
    {
        for (var i = 0; i < ring.Length; i++)
        {
            var comparison = Utf8Order.Compare(
                ring[(left + i) % ring.Length], ring[(right + i) % ring.Length]);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}
