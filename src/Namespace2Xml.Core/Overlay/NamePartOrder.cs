using System.Collections.Immutable;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

/// <summary>
/// The Section 5.2 tie-breaking order over sibling name components.
/// </summary>
/// <remarks>
/// <para>
/// This exists so that mapping order is total by construction. Two contributions carrying one
/// Section 4.7 key are one contribution, so equal sibling position marks should be unreachable;
/// "should be unreachable" is not a property an output format can rely on, and the cost of being
/// wrong about it is nondeterministic output, which is the one thing Section 24 forbids outright.
/// </para>
/// <para>
/// The order is structural rather than a comparison of encoded spellings, because Section 21
/// encoding is delimiter-dependent and can legitimately fail — a <c>Q{...}</c> URI containing the
/// configured delimiter has no escaped spelling — and an order that can fail is not an order.
/// </para>
/// </remarks>
public sealed class NamePartOrder : IComparer<NamePart>
{
    /// <summary>The single instance.</summary>
    public static NamePartOrder Instance { get; } = new();

    /// <inheritdoc/>
    public int Compare(NamePart? x, NamePart? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var byKind = Kind(x).CompareTo(Kind(y));

        if (byKind != 0)
        {
            return byKind;
        }

        return (x, y) switch
        {
            (OrdinaryPart left, OrdinaryPart right) => CompareTokens(left.Tokens, right.Tokens),
            (QualifiedElementPart left, QualifiedElementPart right) =>
                Utf8Order.Compare(left.Uri, right.Uri) is var byUri and not 0
                    ? byUri
                    : CompareTokens(left.Local, right.Local),
            (AttributePart left, AttributePart right) => Compare(left.Name, right.Name),
            (ContentPart left, ContentPart right) => left.Ordinal.CompareTo(right.Ordinal),
            _ => 0,
        };
    }

    /// <summary>
    /// Section 5.2 lists the kinds in this order: ordinary component, qualified element, typed
    /// attribute, typed content.
    /// </summary>
    private static int Kind(NamePart part) => part switch
    {
        OrdinaryPart => 0,
        QualifiedElementPart => 1,
        AttributePart => 2,
        ContentPart => 3,
        _ => 4,
    };

    /// <summary>
    /// Compares two token sequences by Section 5.2: literal text in UTF-8 order, with a wildcard
    /// after any literal at the same position.
    /// </summary>
    /// <remarks>
    /// "At the same position" is a position within the component's text, not within its token list.
    /// Comparing token against token instead makes <c>a*</c> and <c>ab</c> meet as the literals
    /// <c>"a"</c> and <c>"ab"</c>, which the UTF-8 rule then orders <c>a* &lt; ab</c> — the wildcard
    /// sorting before a literal, exactly backwards — because the wildcard is never reached. The
    /// comparison therefore walks scalars, and a wildcard is one atom that follows every scalar.
    /// </remarks>
    private static int CompareTokens(
        ImmutableArray<NameToken> left,
        ImmutableArray<NameToken> right)
    {
        var leftWalk = new TokenWalk(left);
        var rightWalk = new TokenWalk(right);

        while (true)
        {
            var hasLeft = leftWalk.MoveNext();
            var hasRight = rightWalk.MoveNext();

            if (!hasLeft || !hasRight)
            {
                return hasLeft == hasRight ? 0 : hasLeft ? 1 : -1;
            }

            var byAtom = Compare(leftWalk.Current, rightWalk.Current);

            if (byAtom != 0)
            {
                return byAtom;
            }
        }
    }

    private static int Compare(Atom left, Atom right) => (left.IsWildcard, right.IsWildcard) switch
    {
        (false, false) => left.Scalar.CompareTo(right.Scalar),
        (false, true) => -1,
        (true, false) => 1,

        // Section 5.2: "two wildcard tokens compare by capture identifier with the bare form first".
        // Utf8Order treats an absent string as earlier than any present one, which is that rule.
        (true, true) => Utf8Order.Compare(left.CaptureId, right.CaptureId),
    };

    /// <summary>One comparable position in a component: either a scalar or a whole wildcard.</summary>
    private readonly struct Atom
    {
        private Atom(int scalar, string? captureId, bool isWildcard)
        {
            Scalar = scalar;
            CaptureId = captureId;
            IsWildcard = isWildcard;
        }

        public int Scalar { get; }

        public string? CaptureId { get; }

        public bool IsWildcard { get; }

        public static Atom OfScalar(int scalar) => new(scalar, null, isWildcard: false);

        public static Atom OfWildcard(string? captureId) => new(0, captureId, isWildcard: true);
    }

    /// <summary>Walks a token sequence one comparable position at a time.</summary>
    private struct TokenWalk
    {
        private readonly ImmutableArray<NameToken> tokens;
        private int index;
        private System.Text.StringRuneEnumerator runes;
        private bool inLiteral;

        public TokenWalk(ImmutableArray<NameToken> tokens)
        {
            this.tokens = tokens.IsDefault ? [] : tokens;
            index = 0;
            runes = default;
            inLiteral = false;
            Current = Atom.OfScalar(0);
        }

        public Atom Current { get; private set; }

        public bool MoveNext()
        {
            while (true)
            {
                if (inLiteral)
                {
                    if (runes.MoveNext())
                    {
                        Current = Atom.OfScalar(runes.Current.Value);
                        return true;
                    }

                    inLiteral = false;
                }

                if (index >= tokens.Length)
                {
                    return false;
                }

                switch (tokens[index++])
                {
                    case LiteralToken literal:
                        runes = literal.Text.EnumerateRunes();
                        inLiteral = true;
                        continue;

                    case WildcardToken wildcard:
                        Current = Atom.OfWildcard(wildcard.CaptureId);
                        return true;

                    default:
                        continue;
                }
            }
        }
    }
}
