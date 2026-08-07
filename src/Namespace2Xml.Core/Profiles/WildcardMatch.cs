using System.Collections.Immutable;

namespace Namespace2Xml.Profiles;

/// <summary>
/// The capture texts one Section 12 match bound.
/// </summary>
/// <param name="Positional">
/// Section 12.1 legacy unnamed captures, in the left-to-right order the name defines them.
/// </param>
/// <param name="Named">Section 12.2 explicit captures, by identifier.</param>
/// <remarks>
/// Both collections are present because Section 12.2 forbids one rule from mixing the two forms,
/// not because a match can produce both. A rule populates exactly one of them.
/// </remarks>
public sealed record WildcardCaptures(
    ImmutableArray<string> Positional,
    ImmutableDictionary<string, string> Named)
{
    /// <summary>A match that bound nothing, which is what a wildcard-free pattern produces.</summary>
    public static WildcardCaptures Empty { get; } =
        new([], ImmutableDictionary<string, string>.Empty);
}

/// <summary>
/// Section 12 wildcard matching: whether a pattern name matches a concrete name, and what its
/// captures bound.
/// </summary>
/// <remarks>
/// <para>
/// Section 12.1 requires the capture partition to be produced "independently of regular-expression
/// greediness", so this matcher is written directly rather than by translating a pattern into a
/// regular expression. A translation would inherit whichever of leftmost-longest or leftmost-first
/// the engine implements, which is exactly the property the clause declines to depend on.
/// </para>
/// <para>
/// Each capture takes "the shortest text that still permits the remaining pattern to match". For a
/// literal between two wildcards that is the literal's <em>earliest</em> occurrence at or after the
/// current position, and taking it never costs a match: the remaining pattern begins with a
/// wildcard, so it matches a longer suffix whenever it matches a shorter one. The earliest choice
/// therefore both minimizes the capture and maximizes the text left for the rest of the pattern,
/// and no backtracking is needed.
/// </para>
/// <para>
/// Component matching is typed and strict: an ordinary component matches an ordinary component, a
/// qualified element matches a qualified element with an equal URI, and so on. Section 15.2 grants
/// unmarked components an alias index that also selects XML-typed components, but it grants it to
/// <em>scheme paths</em>; Sections 8.6 and 12 describe patterns over input data and say nothing of
/// the kind, so the Appendix A.2 typed model applies literally here. See KNOWN-LIMITS.
/// </para>
/// </remarks>
public static class WildcardMatch
{
    /// <summary>
    /// Matches the pattern's leading components against a concrete name's leading components.
    /// </summary>
    /// <param name="pattern">The pattern components. Never empty.</param>
    /// <param name="count">
    /// How many of the pattern's components to match. Section 8.6 matches a mask in full; Section
    /// 12.3 matches a generative template "through its last wildcard-containing name part".
    /// </param>
    /// <param name="concrete">The concrete name's components.</param>
    /// <param name="captures">The captures the match bound, or <see cref="WildcardCaptures.Empty"/>.</param>
    /// <returns><see langword="true"/> when the leading components match.</returns>
    public static bool TryMatchPrefix(
        ImmutableArray<NamePart> pattern,
        int count,
        ImmutableArray<NamePart> concrete,
        out WildcardCaptures captures)
    {
        captures = WildcardCaptures.Empty;

        if (count < 0 || count > pattern.Length || concrete.Length < count)
        {
            return false;
        }

        var accumulator = new CaptureAccumulator();

        for (var i = 0; i < count; i++)
        {
            if (!TryMatchPart(pattern[i], concrete[i], accumulator))
            {
                return false;
            }
        }

        captures = accumulator.Freeze();
        return true;
    }

    /// <summary>
    /// Matches a whole pattern name against a whole concrete name, component for component.
    /// </summary>
    /// <param name="pattern">The pattern name.</param>
    /// <param name="concrete">The concrete name.</param>
    /// <param name="captures">The captures the match bound.</param>
    /// <returns><see langword="true"/> when the names match and have equal length.</returns>
    public static bool TryMatch(
        QualifiedName pattern,
        QualifiedName concrete,
        out WildcardCaptures captures)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(concrete);

        captures = WildcardCaptures.Empty;

        return pattern.Parts.Length == concrete.Parts.Length
            && TryMatchPrefix(pattern.Parts, pattern.Parts.Length, concrete.Parts, out captures);
    }

    /// <summary>
    /// The zero-based index of the pattern's last wildcard-containing component, or -1.
    /// </summary>
    /// <param name="pattern">The pattern name.</param>
    /// <returns>The index, or -1 when the name contains no wildcard.</returns>
    /// <remarks>
    /// Section 12.3 makes this the depth a generative template matches through, and Section 12.4
    /// makes it the depth at which candidate items are counted: "if the rule's last
    /// wildcard-containing part is at depth k, eligible items are the distinct depth-k prefixes of
    /// existing paths, not every deeper descendant."
    /// </remarks>
    public static int LastWildcardPart(QualifiedName pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        for (var i = pattern.Parts.Length - 1; i >= 0; i--)
        {
            if (PartHasWildcard(pattern.Parts[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool PartHasWildcard(NamePart part) => part switch
    {
        OrdinaryPart ordinary => HasWildcard(ordinary.Tokens),
        QualifiedElementPart qualified => HasWildcard(qualified.Local),
        AttributePart attribute => PartHasWildcard(attribute.Name),
        _ => false,
    };

    private static bool HasWildcard(ImmutableArray<NameToken> tokens)
    {
        foreach (var token in tokens)
        {
            if (token is WildcardToken)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryMatchPart(NamePart pattern, NamePart concrete, CaptureAccumulator captures)
    {
        switch (pattern)
        {
            case OrdinaryPart ordinary:
                return concrete is OrdinaryPart target
                    && LiteralTextOf(target.Tokens) is { } text
                    && TryMatchTokens(ordinary.Tokens, text, captures);

            case QualifiedElementPart qualified:
                return concrete is QualifiedElementPart element
                    && string.Equals(qualified.Uri, element.Uri, StringComparison.Ordinal)
                    && LiteralTextOf(element.Local) is { } local
                    && TryMatchTokens(qualified.Local, local, captures);

            case AttributePart attribute:
                return concrete is AttributePart other
                    && TryMatchPart(attribute.Name, other.Name, captures);

            case ContentPart content:
                return concrete is ContentPart ordinal && content.Ordinal == ordinal.Ordinal;

            default:
                return false;
        }
    }

    private static string? LiteralTextOf(ImmutableArray<NameToken> tokens) =>
        tokens.Length == 1 && tokens[0] is LiteralToken literal ? literal.Text : null;

    /// <summary>
    /// Matches one component's tokens against one concrete component's text.
    /// </summary>
    /// <remarks>
    /// Section 12.1 anchors matching "to the complete part", so the walk below both starts at the
    /// text's first scalar and must finish at its last. <see cref="TokenSequence.Canonical"/>
    /// guarantees no two literals are adjacent, so a literal is always reachable as the token
    /// immediately following a wildcard, and the loop never has to scan forward for one; for the
    /// same reason the token at the top of the loop is always a wildcard.
    /// </remarks>
    private static bool TryMatchTokens(
        ImmutableArray<NameToken> pattern,
        string text,
        CaptureAccumulator captures)
    {
        if (!HasWildcard(pattern))
        {
            return LiteralTextOf(pattern) is { } literal
                && string.Equals(literal, text, StringComparison.Ordinal);
        }

        var index = 0;
        var position = 0;

        if (pattern[0] is LiteralToken lead)
        {
            if (!text.StartsWith(lead.Text, StringComparison.Ordinal))
            {
                return false;
            }

            position = lead.Text.Length;
            index = 1;
        }

        while (index < pattern.Length)
        {
            var wildcard = (WildcardToken)pattern[index];

            if (index + 1 == pattern.Length)
            {
                return captures.Bind(wildcard.CaptureId, text[position..]);
            }

            if (pattern[index + 1] is not LiteralToken following)
            {
                if (!captures.Bind(wildcard.CaptureId, string.Empty))
                {
                    return false;
                }

                index++;
                continue;
            }

            if (index + 2 == pattern.Length)
            {
                var start = text.Length - following.Text.Length;

                return start >= position
                    && text.EndsWith(following.Text, StringComparison.Ordinal)
                    && captures.Bind(wildcard.CaptureId, text[position..start]);
            }

            var next = text.IndexOf(following.Text, position, StringComparison.Ordinal);

            if (next < 0)
            {
                return false;
            }

            if (!captures.Bind(wildcard.CaptureId, text[position..next]))
            {
                return false;
            }

            position = next + following.Text.Length;
            index += 2;
        }

        throw new InvalidOperationException(
            "Section 12.1 anchors matching to the complete part, and the walk above reaches the end "
            + "of the text on every path that leaves it: a trailing wildcard takes the remaining "
            + "text and a trailing literal is matched against the end. Arriving here means a branch "
            + "was added that returns without consuming the part.");
    }

    /// <summary>Accumulates capture bindings while a match is in progress.</summary>
    private sealed class CaptureAccumulator
    {
        private readonly List<string> positional = [];
        private readonly Dictionary<string, string> named = new(StringComparer.Ordinal);

        /// <summary>
        /// Binds one capture, reporting whether Section 12.2 keeps the match alive.
        /// </summary>
        /// <param name="id">The explicit capture identifier, or null for the legacy bare form.</param>
        /// <param name="text">The text the capture took.</param>
        /// <returns>
        /// <see langword="false"/> when an explicit identifier already bound different text.
        /// Section 12.2 makes "inconsistent repeated captures" nonmatches rather than errors, so
        /// this is an ordinary failed match and not a diagnostic.
        /// </returns>
        /// <remarks>
        /// Appendix A.2 spells <c>identifier</c> without a case-folding rule, and the Section 15
        /// list of ASCII case-insensitive vocabularies does not include capture identifiers, so
        /// comparison is ordinal.
        /// </remarks>
        internal bool Bind(string? id, string text)
        {
            if (id is null)
            {
                positional.Add(text);
                return true;
            }

            if (named.TryGetValue(id, out var existing))
            {
                return string.Equals(existing, text, StringComparison.Ordinal);
            }

            named.Add(id, text);
            return true;
        }

        internal WildcardCaptures Freeze() =>
            new([.. positional], named.ToImmutableDictionary(StringComparer.Ordinal));
    }
}
