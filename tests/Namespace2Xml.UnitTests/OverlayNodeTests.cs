using System.Collections.Immutable;
using System.Text;
using Namespace2Xml.Overlay;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 4.2 overlay nodes and the Section 5.2 / 5.4 orders derived from their marks.
/// </summary>
[TestFixture]
public class OverlayNodeTests
{
    private static StableOrderingKey Key(long source) => StableOrderingKey.FromSource(source, 0);

    private static readonly StableOrderingKey Early = Key(1);
    private static readonly StableOrderingKey Middle = Key(2);
    private static readonly StableOrderingKey Late = Key(3);

    private static OrdinaryPart Part(string text) =>
        new([new LiteralToken(text)]);

    private static string Text(NamePart part) =>
        ((OrdinaryPart)part).LiteralText ?? throw new InvalidOperationException("not literal");

    private static OverlayNode Payload(string text, StableOrderingKey position) =>
        OverlayNode.OfPayload(ScalarPayload.Untyped(text), position);

    /// <summary>
    /// Section 4.2: "Mapping, sequence, scalar, and null are therefore projections of an overlay
    /// node, not mutually exclusive internal node kinds." The worked example is <c>a.x=1</c>
    /// followed by <c>a.x.z=3</c>, where <c>x</c> must retain both facts so that namespace can emit
    /// both while JSON picks one.
    /// </summary>
    [Test]
    public void ANodeRetainsAPayloadAndChildrenAtOnce()
    {
        var node = Payload("1", Early).WithChild(Part("z"), Payload("3", Late));

        node.Payload.ShouldNotBeNull().ToCanonicalText().ShouldBe("1");
        node.Children[Part("z")].Payload.ShouldNotBeNull().ToCanonicalText().ShouldBe("3");
    }

    /// <summary>
    /// Section 4.4 steps 1 to 3: the later of the latest scalar contribution and the latest
    /// container contribution decides the rendered shape. Here the descendant is later.
    /// </summary>
    [Test]
    public void ALaterDescendantWinsTheShapeContestAgainstAnEarlierPayload()
    {
        var node = Payload("1", Early).WithChild(Part("z"), Payload("3", Late));

        node.Marks.RendersAsMapping.ShouldBeTrue();
        node.Marks.RendersAsScalar.ShouldBeFalse();
    }

    /// <summary>
    /// Section 4.4: "reversing source order makes the later scalar win in JSON and YAML". This is
    /// the branch that decides whether the exclusive-shape rule was implemented at all, and it is
    /// the one a payload mark is needed to get right.
    /// </summary>
    [Test]
    public void ALaterPayloadWinsTheShapeContestAgainstAnEarlierDescendant()
    {
        var node = Payload("1", Early)
            .WithChild(Part("z"), Payload("3", Middle))
            .WithPayload(ScalarPayload.Untyped("9"), Late);

        node.Marks.RendersAsScalar.ShouldBeTrue();
        node.Marks.RendersAsMapping.ShouldBeFalse();
        node.Payload.ShouldNotBeNull().ToCanonicalText().ShouldBe("9");
    }

    /// <summary>
    /// The payload mark is not the position mark. An explicit mapping-presence contribution
    /// advances the position mark without being a scalar contribution, so judging payload
    /// precedence by position would make this later payload lose to the earlier one.
    /// </summary>
    [Test]
    public void AnInterveningMappingDoesNotStopALaterPayloadFromWinning()
    {
        var node = Payload("first", Early)
            .WithExplicitMapping(Late)
            .WithPayload(ScalarPayload.Untyped("second"), Middle);

        node.Payload.ShouldNotBeNull().ToCanonicalText().ShouldBe("second");
    }

    [Test]
    public void AnEarlierPayloadDoesNotOverrideALaterOne()
    {
        var node = Payload("later", Late).WithPayload(ScalarPayload.Untyped("earlier"), Early);

        node.Payload.ShouldNotBeNull().ToCanonicalText().ShouldBe("later");
    }

    /// <summary>
    /// Section 4.4: "Empty mappings therefore participate in precedence even though they have no
    /// children", so mapping presence cannot be inferred from the child count.
    /// </summary>
    [Test]
    public void AnEmptyExplicitMappingIsDistinctFromNoMapping()
    {
        var empty = OverlayNode.Intermediate(Early).WithExplicitMapping(Early);

        empty.HasExplicitMapping.ShouldBeTrue();
        empty.Children.ShouldBeEmpty();
        empty.Marks.RendersAsMapping.ShouldBeTrue();
        OverlayNode.Intermediate(Early).HasExplicitMapping.ShouldBeFalse();
    }

    /// <summary>
    /// Section 4.2 distinguishes a null payload from having no payload, which is the difference
    /// between JSON emitting <c>null</c> and emitting nothing.
    /// </summary>
    [Test]
    public void ANullPayloadIsNotTheSameAsNoPayload()
    {
        var nothing = OverlayNode.Intermediate(Early);
        var nulled = OverlayNode.OfPayload(ScalarPayload.Null, Early);

        nothing.Payload.ShouldBeNull();
        nulled.Payload.ShouldNotBeNull().Kind.ShouldBe(ScalarKind.Null);
    }

    /// <summary>Section 5.2: mapping order follows each child's position mark.</summary>
    [Test]
    public void ChildrenAreOrderedByPositionMarkNotByInsertion()
    {
        var node = OverlayNode.Intermediate(Early)
            .WithChild(Part("late"), Payload("l", Late))
            .WithChild(Part("early"), Payload("e", Early))
            .WithChild(Part("middle"), Payload("m", Middle));

        node.OrderedChildren.Select(child => Text(child.Key))
            .ShouldBe(["early", "middle", "late"]);
    }

    /// <summary>
    /// Section 5.2: "Overriding a mapping key moves that exact key ... to the winning
    /// contribution's position mark."
    /// </summary>
    [Test]
    public void OverridingAKeyMovesItToTheWinningPosition()
    {
        var node = OverlayNode.Intermediate(Early)
            .WithChild(Part("a"), Payload("1", Early))
            .WithChild(Part("b"), Payload("2", Middle));

        var overridden = node.WithChild(
            Part("a"), node.Children[Part("a")].WithPayload(ScalarPayload.Untyped("3"), Late));

        overridden.OrderedChildren.Select(child => Text(child.Key)).ShouldBe(["b", "a"]);
    }

    /// <summary>
    /// Section 5.2: "A contribution to a strictly deeper descendant ... does not change an ancestor
    /// position mark or move an ancestor mapping key. Adding a new child therefore never moves its
    /// parent."
    /// </summary>
    [Test]
    public void AddingAGrandchildDoesNotMoveTheParentAmongItsSiblings()
    {
        var root = OverlayNode.Intermediate(Early)
            .WithChild(Part("a"), OverlayNode.Intermediate(Early).WithChild(Part("x"), Payload("1", Early)))
            .WithChild(Part("b"), Payload("2", Middle));

        var deepened = root.WithChild(
            Part("a"), root.Children[Part("a")].WithChild(Part("y"), Payload("3", Late)));

        deepened.OrderedChildren.Select(child => Text(child.Key)).ShouldBe(["a", "b"]);
        deepened.Children[Part("a")].Marks.Position.ShouldBe(Early);
    }

    /// <summary>
    /// Section 5.2: equal position marks are broken by the child name as unsigned UTF-8 bytes. The
    /// names here are chosen so that UTF-16 ordinal order disagrees: U+1D400 is a surrogate pair,
    /// which ordinal comparison places before U+E000 and UTF-8 places after.
    /// </summary>
    [Test]
    public void EqualPositionsAreBrokenByUtf8NameOrder()
    {
        var astral = char.ConvertFromUtf32(0x1D400);
        var privateUse = "\uE000";

        var node = OverlayNode.Intermediate(Early)
            .WithChild(Part(astral), Payload("1", Early))
            .WithChild(Part(privateUse), Payload("2", Early));

        node.OrderedChildren.Select(child => Text(child.Key)).ShouldBe([privateUse, astral]);
        string.CompareOrdinal(astral, privateUse).ShouldBeLessThan(0);
    }

    [Test]
    public void Utf8OrderAgreesWithComparingEncodedBytes()
    {
        string[] names = ["a", "z", "\u00e9", "\uE000", char.ConvertFromUtf32(0x1D400), "ab", ""];

        foreach (var left in names)
        {
            foreach (var right in names)
            {
                var expected = CompareBytes(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
                Math.Sign(Utf8Order.Compare(left, right)).ShouldBe(expected, $"'{left}' vs '{right}'");
            }
        }
    }

    /// <summary>
    /// A comparer is only ever asked about pairs, so sorting two elements cannot reveal an
    /// intransitive or asymmetric order. These properties are therefore asserted directly.
    /// </summary>
    [Test]
    public void Utf8OrderIsATotalOrder()
    {
        string[] names = ["", "a", "ab", "b", "\u00e9", "\uE000", char.ConvertFromUtf32(0x1D400)];

        foreach (var left in names)
        {
            Utf8Order.Compare(left, left).ShouldBe(0);

            foreach (var right in names)
            {
                Math.Sign(Utf8Order.Compare(left, right))
                    .ShouldBe(-Math.Sign(Utf8Order.Compare(right, left)));

                foreach (var third in names)
                {
                    if (Utf8Order.Compare(left, right) <= 0 && Utf8Order.Compare(right, third) <= 0)
                    {
                        Utf8Order.Compare(left, third).ShouldBeLessThanOrEqualTo(0);
                    }
                }
            }
        }
    }

    [Test]
    public void AnAbsentNameSortsBeforeAnyPresentOne()
    {
        Utf8Order.Compare(null, "").ShouldBeLessThan(0);
        Utf8Order.Compare("", null).ShouldBeGreaterThan(0);
        Utf8Order.Compare(null, null).ShouldBe(0);
    }

    /// <summary>
    /// Section 5.4: "Rendering sorts surviving items by ordering value."
    /// </summary>
    /// <remarks>
    /// The ordering values are deliberately spread across the whole Section 5.4 range and inserted
    /// out of order. A handful of small consecutive keys is not enough: the backing dictionary
    /// happens to enumerate those in ascending order anyway, so a version of this test that used
    /// them passed even with the sort removed entirely.
    /// </remarks>
    [Test]
    public void SequenceItemsAreOrderedByOrderingValueNotByInsertion()
    {
        long[] insertion =
        [
            9_223_372_036_854_775_807, 3, 2_147_483_648, 0, 999_999, 7, 100, 1, 42, 5,
            4_294_967_296, 2, 123_456_789_012, 9, 17, 8,
        ];

        var node = insertion.Aggregate(
            OverlayNode.Intermediate(Early),
            (current, value) => current.WithSequenceItem(
                value, SequenceItem.Native(Payload($"v{value}", Early))));

        node.OrderedSequence.Select(item => item.Key).ShouldBe(insertion.Order());
    }

    /// <summary>
    /// Section 5.4: "Gaps and nonzero bases are retained internally", so the keys are the ordering
    /// values themselves and not dense indices.
    /// </summary>
    [Test]
    public void SequenceGapsAreRetained()
    {
        var node = OverlayNode.Intermediate(Early)
            .WithSequenceItem(5, SequenceItem.Native(Payload("a", Early)))
            .WithSequenceItem(9, SequenceItem.Native(Payload("b", Middle)));

        node.Sequence.Keys.OrderBy(key => key).ShouldBe([5L, 9L]);
    }

    /// <summary>Section 5.4: ordering provenance survives on the item.</summary>
    [Test]
    public void SequenceItemsRetainTheirOrderingProvenance()
    {
        var node = OverlayNode.Intermediate(Early)
            .WithSequenceItem(0, SequenceItem.Native(Payload("a", Early)))
            .WithSequenceItem(1, SequenceItem.Numbered(Payload("b", Middle)));

        node.Sequence[0].Provenance.ShouldBe(OrderingProvenance.Implicit);
        node.Sequence[1].Provenance.ShouldBe(OrderingProvenance.Explicit);
    }

    /// <summary>
    /// Section 5.4: "Reusing an explicit ordering value overrides the existing item at that value",
    /// and never displaces anything else.
    /// </summary>
    [Test]
    public void ReusingAnOrderingValueOverridesInPlace()
    {
        var node = OverlayNode.Intermediate(Early)
            .WithSequenceItem(3, SequenceItem.Native(Payload("first", Early)))
            .WithSequenceItem(4, SequenceItem.Native(Payload("other", Early)))
            .WithSequenceItem(3, SequenceItem.Numbered(Payload("second", Late)));

        node.Sequence.Count.ShouldBe(2);
        node.Sequence[3].Node.Payload.ShouldNotBeNull().ToCanonicalText().ShouldBe("second");
        node.Sequence[4].Node.Payload.ShouldNotBeNull().ToCanonicalText().ShouldBe("other");
    }

    /// <summary>
    /// A sequence item is a descendant, so Section 4.4's rule that a deeper contribution refreshes
    /// shape "without changing that ancestor's position mark" applies: appending to a list must not
    /// move the list within its own parent.
    /// </summary>
    [Test]
    public void AddingASequenceItemDoesNotMoveTheSequenceNode()
    {
        var node = OverlayNode.Intermediate(Early)
            .WithSequenceItem(0, SequenceItem.Native(Payload("a", Late)));

        node.Marks.Position.ShouldBe(Early);
        node.Marks.SequenceShape.ShouldBe(Late);
        node.Marks.RendersAsSequence.ShouldBeTrue();
    }

    /// <summary>
    /// Section 4.2 lets one node hold both container facets; the exclusive-shape rule picks one per
    /// output instance without discarding the other.
    /// </summary>
    [Test]
    public void ANodeMayHoldBothAMappingAndASequence()
    {
        var node = OverlayNode.Intermediate(Early)
            .WithChild(Part("k"), Payload("v", Middle))
            .WithSequenceItem(0, SequenceItem.Native(Payload("i", Late)));

        node.Children.Count.ShouldBe(1);
        node.Sequence.Count.ShouldBe(1);
        node.Marks.RendersAsSequence.ShouldBeTrue();
        node.Marks.RendersAsMapping.ShouldBeFalse();
    }

    /// <summary>Section 4.5: comments accumulate in source order.</summary>
    [Test]
    public void CommentsAccumulateInSourceOrder()
    {
        var node = OverlayNode.Intermediate(Early)
            .WithComment(new BoundComment("third", CommentPlacement.Trailing, Late))
            .WithComment(new BoundComment("first", CommentPlacement.Leading, Early))
            .WithComment(new BoundComment("second", CommentPlacement.Inline, Middle));

        node.OrderedComments.Select(comment => comment.Text)
            .ShouldBe(["first", "second", "third"]);
    }

    /// <summary>
    /// Section 4.5: a comment is bound to a path, not a contribution. Binding one must not advance
    /// any mark, or a trailing <c>#</c> could reorder a mapping.
    /// </summary>
    [Test]
    public void BindingACommentDoesNotMoveTheNode()
    {
        var node = Payload("v", Early)
            .WithComment(new BoundComment("late", CommentPlacement.Trailing, Late));

        node.Marks.Position.ShouldBe(Early);
        node.Marks.PayloadMark.ShouldBe(Early);
    }

    /// <summary>
    /// Section 4.5: "overriding a payload does not detach comments already bound to that logical
    /// path".
    /// </summary>
    [Test]
    public void OverridingAPayloadKeepsTheCommentsBoundToThePath()
    {
        var node = Payload("old", Early)
            .WithComment(new BoundComment("note", CommentPlacement.Leading, Early))
            .WithPayload(ScalarPayload.Untyped("new"), Late);

        node.Payload.ShouldNotBeNull().ToCanonicalText().ShouldBe("new");
        node.OrderedComments.Select(comment => comment.Text).ShouldBe(["note"]);
    }

    /// <summary>
    /// Section 5.4 forbids reallocating an ordering value because an item was removed, and the same
    /// reasoning applies to shape: a Section 8.4 mask suppresses a path without rewriting the
    /// evidence that a contribution occurred.
    /// </summary>
    [Test]
    public void MaskingAChildDoesNotLowerTheMappingShapeMark()
    {
        var node = OverlayNode.Intermediate(Early).WithChild(Part("gone"), Payload("1", Late));

        var masked = node.WithoutChild(Part("gone"));

        masked.Children.ShouldBeEmpty();
        masked.Marks.MappingShape.ShouldBe(Late);
    }

    [Test]
    public void MaskingAnAbsentChildChangesNothing()
    {
        var node = OverlayNode.Intermediate(Early).WithChild(Part("a"), Payload("1", Early));

        node.WithoutChild(Part("b")).ShouldBeSameAs(node);
    }

    [Test]
    public void ANodeWithNoContributionsIsEmpty()
    {
        OverlayNode.Intermediate(Early).IsEmpty.ShouldBeTrue();
        Payload("v", Early).IsEmpty.ShouldBeFalse();
        OverlayNode.Intermediate(Early).WithExplicitMapping(Early).IsEmpty.ShouldBeFalse();
    }

    private static int CompareBytes(byte[] left, byte[] right)
    {
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            if (left[index] != right[index])
            {
                return left[index] < right[index] ? -1 : 1;
            }
        }

        return Math.Sign(left.Length - right.Length);
    }
}
