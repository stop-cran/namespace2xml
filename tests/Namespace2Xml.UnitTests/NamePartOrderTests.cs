using System.Collections.Immutable;
using Namespace2Xml.Overlay;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Section 5.2 tie-breaking order over sibling name components.
/// </summary>
[TestFixture]
public class NamePartOrderTests
{
    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static OrdinaryPart Wild(string? capture = null) =>
        new([new WildcardToken(capture)]);

    private static readonly ImmutableArray<NamePart> Kinds =
    [
        Ordinary("m"),
        new QualifiedElementPart("urn:x", [new LiteralToken("m")]),
        new AttributePart(Ordinary("m")),
        new ContentPart(0),
    ];

    /// <summary>
    /// Section 5.2 lists the kinds in this order, and compares kind before text precisely because a
    /// typed attribute and an ordinary component may carry the same text while naming different
    /// things.
    /// </summary>
    [Test]
    public void ComponentKindIsComparedBeforeText()
    {
        for (var earlier = 0; earlier < Kinds.Length; earlier++)
        {
            for (var later = earlier + 1; later < Kinds.Length; later++)
            {
                NamePartOrder.Instance.Compare(Kinds[earlier], Kinds[later])
                    .ShouldBeLessThan(0, $"{Kinds[earlier]} before {Kinds[later]}");
            }
        }
    }

    [Test]
    public void AnAttributeAndAnOrdinaryPartWithTheSameTextAreOrdered()
    {
        var attribute = new AttributePart(Ordinary("x"));
        var ordinary = Ordinary("x");

        NamePartOrder.Instance.Compare(ordinary, attribute).ShouldBeLessThan(0);
        NamePartOrder.Instance.Compare(attribute, ordinary).ShouldBeGreaterThan(0);
        NamePartOrder.Instance.Compare(attribute, attribute).ShouldBe(0);
    }

    [Test]
    public void OrdinaryPartsCompareByUtf8Text()
    {
        var astral = char.ConvertFromUtf32(0x1D400);

        NamePartOrder.Instance.Compare(Ordinary("\uE000"), Ordinary(astral)).ShouldBeLessThan(0);
        NamePartOrder.Instance.Compare(Ordinary("a"), Ordinary("b")).ShouldBeLessThan(0);
        NamePartOrder.Instance.Compare(Ordinary("a"), Ordinary("ab")).ShouldBeLessThan(0);
    }

    /// <summary>Section 5.2: "A wildcard token sorts after any literal text at the same position."</summary>
    [Test]
    public void AWildcardSortsAfterLiteralTextAtTheSamePosition()
    {
        NamePartOrder.Instance.Compare(Ordinary("zzz"), Wild()).ShouldBeLessThan(0);
        NamePartOrder.Instance.Compare(Wild(), Ordinary("zzz")).ShouldBeGreaterThan(0);
    }

    /// <summary>Section 5.2: "two wildcard tokens compare by capture identifier with the bare form first".</summary>
    [Test]
    public void WildcardsCompareByCaptureIdentifierWithTheBareFormFirst()
    {
        NamePartOrder.Instance.Compare(Wild(), Wild("a")).ShouldBeLessThan(0);
        NamePartOrder.Instance.Compare(Wild("a"), Wild("b")).ShouldBeLessThan(0);
        NamePartOrder.Instance.Compare(Wild("a"), Wild("a")).ShouldBe(0);
    }

    [Test]
    public void QualifiedElementsCompareByUriThenLocalName()
    {
        var first = new QualifiedElementPart("urn:a", [new LiteralToken("z")]);
        var second = new QualifiedElementPart("urn:b", [new LiteralToken("a")]);
        var third = new QualifiedElementPart("urn:b", [new LiteralToken("b")]);

        NamePartOrder.Instance.Compare(first, second).ShouldBeLessThan(0);
        NamePartOrder.Instance.Compare(second, third).ShouldBeLessThan(0);
    }

    [Test]
    public void ContentPartsCompareByOrderingValue()
    {
        NamePartOrder.Instance.Compare(new ContentPart(2), new ContentPart(10))
            .ShouldBeLessThan(0);
    }

    /// <summary>
    /// A comparer is only asked about pairs, so a sort cannot reveal an intransitive or asymmetric
    /// order. The properties that make it an order are therefore asserted directly.
    /// </summary>
    [Test]
    public void TheOrderIsTotal()
    {
        ImmutableArray<NamePart> parts =
        [
            Ordinary("a"), Ordinary("b"), Ordinary("ab"), Wild(), Wild("c"),
            new OrdinaryPart([new LiteralToken("a"), new WildcardToken(null)]),
            new QualifiedElementPart("", [new LiteralToken("a")]),
            new QualifiedElementPart("urn:x", [new LiteralToken("a")]),
            new AttributePart(Ordinary("a")),
            new AttributePart(new QualifiedElementPart("urn:x", [new LiteralToken("a")])),
            new ContentPart(0), new ContentPart(7),
        ];

        foreach (var left in parts)
        {
            NamePartOrder.Instance.Compare(left, left).ShouldBe(0);

            foreach (var right in parts)
            {
                Math.Sign(NamePartOrder.Instance.Compare(left, right))
                    .ShouldBe(-Math.Sign(NamePartOrder.Instance.Compare(right, left)));

                if (!left.Equals(right))
                {
                    NamePartOrder.Instance.Compare(left, right).ShouldNotBe(0, $"{left} vs {right}");
                }

                foreach (var third in parts)
                {
                    if (NamePartOrder.Instance.Compare(left, right) <= 0
                        && NamePartOrder.Instance.Compare(right, third) <= 0)
                    {
                        NamePartOrder.Instance.Compare(left, third).ShouldBeLessThanOrEqualTo(0);
                    }
                }
            }
        }
    }
}
