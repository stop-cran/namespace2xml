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
    private static int CompareTokens(
        ImmutableArray<NameToken> left,
        ImmutableArray<NameToken> right)
    {
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var byToken = CompareToken(left[index], right[index]);

            if (byToken != 0)
            {
                return byToken;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int CompareToken(NameToken left, NameToken right) => (left, right) switch
    {
        (LiteralToken a, LiteralToken b) => Utf8Order.Compare(a.Text, b.Text),
        (LiteralToken, WildcardToken) => -1,
        (WildcardToken, LiteralToken) => 1,
        (WildcardToken a, WildcardToken b) => Utf8Order.Compare(a.CaptureId, b.CaptureId),
        _ => 0,
    };
}
