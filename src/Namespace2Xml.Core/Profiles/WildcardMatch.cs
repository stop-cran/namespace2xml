using System.Collections.Immutable;
using System.Text;

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
/// Each capture takes "the shortest text that still permits the remaining pattern to match". When
/// no explicit identifier repeats, that is a single greedy walk: for a literal between two
/// wildcards the shortest capture ends at the literal's <em>earliest</em> occurrence at or after the
/// current position, and taking it never costs a match, because the remaining pattern begins with a
/// wildcard and so matches a longer suffix whenever it matches a shorter one. The earliest choice
/// therefore both minimizes the capture and maximizes the text left for the rest of the pattern.
/// </para>
/// <para>
/// A <em>repeated</em> explicit identifier breaks that argument, because Section 12.2 makes the
/// second occurrence a constraint rather than a free capture, and the shortest binding may be the
/// one the constraint rejects. The qualifying clause in Section 12.1 is "still permits the remaining
/// pattern to match", so the shortest <em>viable</em> partition has to be found rather than assumed,
/// and a pattern that repeats an identifier is matched by a shortest-first search with backtracking.
/// The greedy walk is kept for every other pattern: it is provably sufficient there, and the search
/// is not.
/// </para>
/// <para>
/// The search branches only on the first binding of each distinct identifier, since every later
/// occurrence is then a comparison that fails immediately, so its cost stays polynomial in the part
/// length rather than exponential in the number of wildcards.
/// </para>
/// <para>
/// Component matching is typed and strict: an ordinary component matches an ordinary component, a
/// qualified element matches a qualified element with an equal URI, and so on. Section 15.2 grants
/// unmarked components an alias index that also selects XML-typed components, but it grants it to
/// <em>scheme paths</em>; Sections 8.6 and 12 describe patterns over input data and say nothing of
/// the kind, so the Appendix A.2 typed model applies literally here. A scheme path reaches the
/// alias by folding the <em>concrete</em> path before calling in — see
/// <c>Namespace2Xml.Overlay.SchemeAlias</c> — which keeps that grant out of this class entirely.
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
        var constrained = ConstrainedIds(pattern, count);

        if (!constrained.IsEmpty)
        {
            // Section 12.2 makes a repeated identifier a constraint on the partition, so the
            // components cannot be matched one at a time: the binding an earlier component chose is
            // what a later one rejects, and only the earlier component can put it right.
            if (!ConstrainedMatch.TryMatch(pattern, count, concrete, constrained, accumulator))
            {
                return false;
            }

            captures = accumulator.Freeze();
            return true;
        }

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
            if (HasWildcard(pattern.Parts[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Whether one component contains a wildcard token.</summary>
    /// <param name="part">The component.</param>
    /// <returns><see langword="true"/> when the component is a pattern rather than literal text.</returns>
    /// <remarks>
    /// Section 12.4 charges a candidate check only when "every literal name part before that point
    /// equals the corresponding item part", so a caller deciding eligibility has to be able to ask
    /// which parts are literal.
    /// </remarks>
    public static bool HasWildcard(NamePart part) => part switch
    {
        OrdinaryPart ordinary => HasWildcard(ordinary.Tokens),
        QualifiedElementPart qualified => HasWildcard(qualified.Local),
        AttributePart attribute => HasWildcard(attribute.Name),
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
    /// The explicit capture identifiers the pattern uses more than once, which is what Section 12.2
    /// turns from free captures into constraints.
    /// </summary>
    /// <param name="pattern">The pattern components.</param>
    /// <param name="count">How many leading components take part in the match.</param>
    /// <returns>The repeated identifiers in ordinal order, or empty when none repeats.</returns>
    private static ImmutableArray<string> ConstrainedIds(ImmutableArray<NamePart> pattern, int count)
    {
        var explicitCaptures = 0;

        for (var i = 0; i < count; i++)
        {
            explicitCaptures += CountCaptures(pattern[i]);
        }

        if (explicitCaptures < 2)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        SortedSet<string>? repeated = null;

        for (var i = 0; i < count; i++)
        {
            if (TokensOf(pattern[i]) is not { } tokens)
            {
                continue;
            }

            foreach (var token in tokens)
            {
                if (token is WildcardToken { CaptureId: { } id } && !seen.Add(id))
                {
                    repeated ??= new SortedSet<string>(StringComparer.Ordinal);
                    repeated.Add(id);
                }
            }
        }

        return repeated is null ? [] : [.. repeated];
    }

    private static int CountCaptures(NamePart part) =>
        TokensOf(part) is { } tokens ? CountCaptures(tokens) : 0;

    private static int CountCaptures(ImmutableArray<NameToken> tokens)
    {
        var total = 0;

        foreach (var token in tokens)
        {
            if (token is WildcardToken { CaptureId: not null })
            {
                total++;
            }
        }

        return total;
    }

    /// <summary>
    /// The tokens a component matches with, or <see langword="null"/> for a component that carries
    /// no pattern text.
    /// </summary>
    private static ImmutableArray<NameToken>? TokensOf(NamePart part) =>
        part switch
        {
            OrdinaryPart ordinary => ordinary.Tokens,
            QualifiedElementPart qualified => qualified.Local,
            AttributePart attribute => TokensOf(attribute.Name),
            _ => null,
        };

    /// <summary>
    /// The Section 12.2 search for a partition every repeated capture agrees with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 12.1 assigns captures left to right, "each taking the shortest text that still
    /// permits the remaining pattern to match". Where no identifier repeats, "the remaining
    /// pattern" is satisfied by a single forward walk, and <see cref="TryMatchTokens"/> does that.
    /// Where an identifier repeats, its later occurrence is already-decided text, so a binding that
    /// looked shortest can turn out not to permit the remainder at all, and the only way to honour
    /// the sentence is to reconsider it. Section 12.2 scopes a capture to "one profile or scheme
    /// entry", so the reconsideration reaches across the namespace delimiter even though
    /// Section 12.3 confines an individual wildcard to one name part.
    /// </para>
    /// <para>
    /// Trying each length in ascending order and keeping the first that lets everything after it
    /// match yields exactly the specified partition, and yields the same one on every platform, so
    /// the result is a function of the inputs rather than of a search strategy.
    /// </para>
    /// <para>
    /// The search records the states it has exhausted, without which it is exponential: a pattern
    /// of eight captures against a sixty-character part took over three minutes before this was
    /// added, which is a denial of service in a tool that reads untrusted configuration. A state is
    /// the position reached plus the text bound to each repeated identifier; free captures are
    /// excluded from the key because they constrain nothing that follows, so two states differing
    /// only in them succeed or fail together.
    /// </para>
    /// </remarks>
    private sealed class ConstrainedMatch
    {
        private readonly ImmutableArray<NameToken>[] patterns;
        private readonly string[] texts;
        private readonly ImmutableArray<string> constrained;
        private readonly CaptureAccumulator captures;
        private readonly HashSet<string> exhausted = new(StringComparer.Ordinal);
        private readonly StringBuilder state = new();

        private ConstrainedMatch(
            ImmutableArray<NameToken>[] patterns,
            string[] texts,
            ImmutableArray<string> constrained,
            CaptureAccumulator captures)
        {
            this.patterns = patterns;
            this.texts = texts;
            this.constrained = constrained;
            this.captures = captures;
        }

        /// <summary>
        /// Matches the pattern's leading components, reconsidering a binding when a later component
        /// rejects it.
        /// </summary>
        /// <param name="pattern">The pattern components.</param>
        /// <param name="count">How many leading components take part in the match.</param>
        /// <param name="concrete">The concrete name's components.</param>
        /// <param name="constrained">The identifiers the pattern repeats.</param>
        /// <param name="captures">Receives the bindings the match made.</param>
        /// <returns><see langword="true"/> when some partition satisfies every constraint.</returns>
        internal static bool TryMatch(
            ImmutableArray<NamePart> pattern,
            int count,
            ImmutableArray<NamePart> concrete,
            ImmutableArray<string> constrained,
            CaptureAccumulator captures)
        {
            var patterns = new ImmutableArray<NameToken>[count];
            var texts = new string[count];

            for (var index = 0; index < count; index++)
            {
                if (!TryFlatten(pattern[index], concrete[index], out patterns[index], out var text))
                {
                    return false;
                }

                texts[index] = text;
            }

            return new ConstrainedMatch(patterns, texts, constrained, captures).TryMatchComponent(0);
        }

        /// <summary>
        /// Reduces a component pair to the tokens and the text a match compares, having settled
        /// everything about the pair that no partition can change.
        /// </summary>
        /// <param name="pattern">The pattern component.</param>
        /// <param name="concrete">The concrete component.</param>
        /// <param name="tokens">The pattern tokens to match with.</param>
        /// <param name="text">The concrete text to match against.</param>
        /// <returns><see langword="false"/> when the two components cannot match at all.</returns>
        private static bool TryFlatten(
            NamePart pattern,
            NamePart concrete,
            out ImmutableArray<NameToken> tokens,
            out string text)
        {
            switch (pattern)
            {
                case OrdinaryPart ordinary
                    when concrete is OrdinaryPart target
                        && LiteralTextOf(target.Tokens) is { } literal:
                    tokens = ordinary.Tokens;
                    text = literal;
                    return true;

                case QualifiedElementPart qualified
                    when concrete is QualifiedElementPart element
                        && string.Equals(qualified.Uri, element.Uri, StringComparison.Ordinal)
                        && LiteralTextOf(element.Local) is { } local:
                    tokens = qualified.Local;
                    text = local;
                    return true;

                case AttributePart attribute when concrete is AttributePart other:
                    return TryFlatten(attribute.Name, other.Name, out tokens, out text);

                case ContentPart content
                    when concrete is ContentPart ordinal && content.Ordinal == ordinal.Ordinal:
                    tokens = [];
                    text = string.Empty;
                    return true;

                default:
                    tokens = [];
                    text = string.Empty;
                    return false;
            }
        }

        private bool TryMatchComponent(int component) =>
            component == patterns.Length || TryMatchTokens(component, 0, 0);

        private bool TryMatchTokens(int component, int index, int position)
        {
            var tokens = patterns[component];
            var text = texts[component];

            if (index == tokens.Length)
            {
                // Section 12.1 anchors matching "to the complete part", so a partition that leaves
                // text over is not a match however well its captures agree.
                return position == text.Length && TryMatchComponent(component + 1);
            }

            if (tokens[index] is LiteralToken literal)
            {
                return position + literal.Text.Length <= text.Length
                    && string.CompareOrdinal(text, position, literal.Text, 0, literal.Text.Length) == 0
                    && TryMatchTokens(component, index + 1, position + literal.Text.Length);
            }

            if (!exhausted.Add(StateKey(component, index, position)))
            {
                return false;
            }

            var wildcard = (WildcardToken)tokens[index];

            for (var take = 0; position + take <= text.Length; take++)
            {
                var mark = captures.Save();

                if (captures.Bind(wildcard.CaptureId, text.Substring(position, take))
                    && TryMatchTokens(component, index + 1, position + take))
                {
                    return true;
                }

                captures.Rewind(mark);
            }

            return false;
        }

        /// <summary>
        /// Identifies a search state by where it has reached and by the text every repeated
        /// identifier is already committed to.
        /// </summary>
        /// <param name="component">The component being matched.</param>
        /// <param name="index">The token being matched.</param>
        /// <param name="position">How much of the component's text has been consumed.</param>
        /// <remarks>
        /// Each bound text is written with its length in front of it, so no capture text can be
        /// spelled the same way as a different one followed by a separator. A key that was merely
        /// delimited would let a name containing the delimiter collide with an unrelated state, and
        /// a collision here is a silently missed match rather than a visible failure.
        /// </remarks>
        private string StateKey(int component, int index, int position)
        {
            state.Clear();
            state.Append(component).Append(':').Append(index).Append(':').Append(position);

            foreach (var id in constrained)
            {
                var bound = captures.Lookup(id);
                state.Append(':').Append(bound is null ? -1 : bound.Length).Append(':').Append(bound);
            }

            return state.ToString();
        }
    }

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
        private readonly List<string> namedOrder = [];

        /// <summary>
        /// The point a backtracking search returns to when a partition turns out not to be viable.
        /// </summary>
        /// <param name="Positional">How many legacy captures had been bound.</param>
        /// <param name="Named">How many distinct explicit identifiers had been bound.</param>
        internal readonly record struct Mark(int Positional, int Named);

        /// <summary>Records the bindings made so far, so they can be undone.</summary>
        internal Mark Save() => new(positional.Count, namedOrder.Count);

        /// <summary>Discards every binding made since a mark.</summary>
        /// <param name="mark">The mark to return to.</param>
        internal void Rewind(Mark mark)
        {
            positional.RemoveRange(mark.Positional, positional.Count - mark.Positional);

            for (var index = namedOrder.Count - 1; index >= mark.Named; index--)
            {
                named.Remove(namedOrder[index]);
                namedOrder.RemoveAt(index);
            }
        }

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
            namedOrder.Add(id);
            return true;
        }

        /// <summary>
        /// The text an explicit identifier is currently bound to, or null when it is still free.
        /// </summary>
        /// <param name="id">The explicit capture identifier.</param>
        internal string? Lookup(string id) => named.GetValueOrDefault(id);

        internal WildcardCaptures Freeze() =>
            new([.. positional], named.ToImmutableDictionary(StringComparer.Ordinal));
    }
}
