using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Pipeline step 9: Section 15.1 exposure of ordering values as path parts.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the exposer currently produces.
/// </remarks>
[TestFixture]
public class OrderingValueExposerTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private static NamePart Ordinary(string text) => new OrdinaryPart([new LiteralToken(text)]);

    private static QualifiedName Path(params string[] steps) => new([.. steps.Select(Ordinary)]);

    private static MergeStrategyMap Strategy(MergeStrategy strategy, params string[] path) =>
        MergeStrategyMap.Create([new(Path(path), strategy)]);

    private static OverlayNode Leaf(string text, int source, int item = 0) =>
        OverlayNode.OfPayload(
            ScalarPayload.Untyped(text), StableOrderingKey.FromSource(source, item));

    private static OverlayNode Container(int source, int item = 0) =>
        OverlayNode.Empty(NodeMarks.At(StableOrderingKey.FromSource(source, item)));

    private static string Value(OverlayNode node) =>
        node.Payload.ShouldNotBeNull().ToCanonicalText();

    private OverlayNode Expose(OverlayNode node, MergeStrategyMap? strategies = null) =>
        new OrderingValueExposer(
            new OverlayMerger(strategies ?? MergeStrategyMap.Default, diagnostics))
            .Expose(node);

    /// <summary>
    /// A node holding one native item at ordering value 0 from <paramref name="itemSource"/> and one
    /// mapping child spelled <paramref name="key"/> from <paramref name="childSource"/>.
    /// </summary>
    private static OverlayNode Collision(
        string key, int itemSource, string itemText, int childSource, string childText) =>
        Container(itemSource)
            .WithSequenceItem(0, SequenceItem.Native(Leaf(itemText, itemSource, 1)))
            .WithChild(Ordinary(key), Leaf(childText, childSource));

    // Section 15.1 step 9: combining the two facets of one address.

    /// <summary>
    /// Section 15.1: "a mapping child whose name is an in-range canonical ordering value and the
    /// sequence item with that value at the same path are one structural overlay node". One node,
    /// not two equal ones: a later graft through either address has to be visible through the
    /// other, which only holds if they are the same object.
    /// </summary>
    [Test]
    public void TheItemAndTheMappingChildNamingItsValueBecomeOneNode()
    {
        var exposed = Expose(Collision("0", itemSource: 1, "q", childSource: 2, "z"));

        ReferenceEquals(exposed.Children[Ordinary("0")], exposed.Sequence[0].Node)
            .ShouldBeTrue();
    }

    /// <summary>
    /// Section 15.1: step 9 merges the pair "in source order", so "under the default <c>deep</c>
    /// the later contribution therefore patches the earlier item at its ordering value".
    /// </summary>
    [Test]
    public void ALaterMappingChildPatchesTheEarlierItem()
    {
        var exposed = Expose(Collision("0", itemSource: 1, "q", childSource: 2, "z"));

        Value(exposed.Sequence[0].Node).ShouldBe("z");
    }

    /// <summary>
    /// The same clause read in the other direction: source order decides, not which facet the
    /// contribution happens to live in.
    /// </summary>
    [Test]
    public void ALaterItemWinsOverAnEarlierMappingChild()
    {
        var exposed = Expose(Collision("0", itemSource: 2, "q", childSource: 1, "z"));

        Value(exposed.Sequence[0].Node).ShouldBe("q");
    }

    /// <summary>
    /// Section 15.1 merges the pair "in source order", and Section 17.1's "later payload wins" has
    /// no later payload to prefer when the two marks are equal, so what is already in the sequence
    /// stays. A tie is unreachable from input — no single source contributes both a sequence item
    /// and a numeric mapping child at one path — and is pinned here only so that the step is
    /// deterministic for every overlay, not only for the ones a parser can build.
    /// </summary>
    [Test]
    public void ATieLeavesTheItemInPlace()
    {
        var node = Container(1)
            .WithSequenceItem(0, SequenceItem.Native(Leaf("q", 1)))
            .WithChild(Ordinary("0"), Leaf("z", 1));

        Value(Expose(node).Sequence[0].Node).ShouldBe("q");
    }

    /// <summary>
    /// Section 15.1: "the combined item keeps the ordering provenance the sequence item already
    /// had, because the value was acquired when that item was placed and step 9 supplies no new
    /// value".
    /// </summary>
    [Test]
    public void TheCombinedItemKeepsTheSequenceItemsOrderingProvenance()
    {
        var exposed = Expose(Collision("0", itemSource: 1, "q", childSource: 2, "z"));

        exposed.Sequence[0].Provenance.ShouldBe(OrderingProvenance.Implicit);
    }

    /// <summary>
    /// Section 15.1 combines the pair "under the effective input <c>merge</c> strategy at their
    /// shared path". Under <c>replace</c> Section 16.10 makes "the later complete value replace the
    /// earlier value", so the earlier item's own children do not survive.
    /// </summary>
    [Test]
    public void CombiningAppliesTheStrategyAtTheSharedPath()
    {
        var item = Container(1, 1).WithChild(Ordinary("kept"), Leaf("1", 1, 2));
        var node = Container(1)
            .WithSequenceItem(0, SequenceItem.Native(item))
            .WithChild(Ordinary("0"), Leaf("z", 2));

        var exposed = Expose(node, Strategy(MergeStrategy.Replace, "0"));

        exposed.Sequence[0].Node.Children.ShouldBeEmpty();
        Value(exposed.Sequence[0].Node).ShouldBe("z");
    }

    /// <summary>
    /// The strategy is looked up at the shared path, which is the item's path and not its parent's.
    /// Section 16.10: "a <c>merge</c> directive governs only the node it matches".
    /// </summary>
    [Test]
    public void AStrategyAtTheParentDoesNotGovernTheCombination()
    {
        var item = Container(1, 1).WithChild(Ordinary("kept"), Leaf("1", 1, 2));
        var node = Container(1)
            .WithSequenceItem(0, SequenceItem.Native(item))
            .WithChild(Ordinary("0"), Leaf("z", 2));

        var exposed = Expose(node, Strategy(MergeStrategy.Replace, "a"));

        exposed.Sequence[0].Node.Children.ShouldContainKey(Ordinary("kept"));
    }

    /// <summary>
    /// Section 16.10 <c>error</c>: "any distinct second source or generated contribution at the
    /// path is an error". The two facets came from different sources, so combining them under
    /// <c>error</c> is exactly the collision the strategy exists to report.
    /// </summary>
    [Test]
    public void MergeErrorAtTheSharedPathRejectsTheCombination()
    {
        var root = Container(1).WithChild(
            Ordinary("a"), Collision("0", itemSource: 1, "q", childSource: 2, "z"));

        Expose(root, Strategy(MergeStrategy.Error, "a", "0"));

        var diagnostic = diagnostics.Drain().ShouldHaveSingleItem();

        diagnostic.Code.ShouldBe("TYPE001");
        diagnostic.Path.ShouldBe("a.0");
    }

    // Section 8.7 spelling: what is and is not an ordering value.

    /// <summary>
    /// Section 15.1 combines only a child "whose name is an in-range canonical ordering value", and
    /// Section 8.7 makes "leading-zero spellings such as <c>00</c> and <c>01</c> ordinary mapping
    /// keys".
    /// </summary>
    [Test]
    public void ALeadingZeroSpellingIsNotAnOrderingValueSoNothingIsCombined()
    {
        var exposed = Expose(Collision("00", itemSource: 1, "q", childSource: 2, "z"));

        Value(exposed.Sequence[0].Node).ShouldBe("q");
        Value(exposed.Children[Ordinary("00")]).ShouldBe("z");
    }

    /// <summary>
    /// Section 8.7: "a canonically spelled decimal above the supported maximum is an ordinary
    /// mapping key".
    /// </summary>
    [Test]
    public void ADecimalAboveTheMaximumIsNotAnOrderingValue()
    {
        var key = "9223372036854775808";
        var exposed = Expose(Collision(key, itemSource: 1, "q", childSource: 2, "z"));

        Value(exposed.Sequence[0].Node).ShouldBe("q");
        Value(exposed.Children[Ordinary(key)]).ShouldBe("z");
    }

    /// <summary>
    /// An ordinary name is not an address into the sequence at all, so both facets survive
    /// untouched.
    /// </summary>
    [Test]
    public void AnOrdinaryMappingChildIsNotCombined()
    {
        var exposed = Expose(Collision("x", itemSource: 1, "q", childSource: 2, "z"));

        Value(exposed.Sequence[0].Node).ShouldBe("q");
        Value(exposed.Children[Ordinary("x")]).ShouldBe("z");
    }

    /// <summary>
    /// A numeric child with no item at that value is left alone: Section 8.7 places projection of a
    /// numeric mapping at step 11, and step 9 combines only what already exists in both facets.
    /// </summary>
    [Test]
    public void ANumericChildWithNoItemAtThatValueIsNotProjected()
    {
        var node = Container(1)
            .WithSequenceItem(0, SequenceItem.Native(Leaf("q", 1, 1)))
            .WithChild(Ordinary("5"), Leaf("z", 2));

        var exposed = Expose(node);

        exposed.Sequence.Keys.ShouldBe([0L]);
        Value(exposed.Children[Ordinary("5")]).ShouldBe("z");
    }

    // Recursion: exposure is a property of the whole overlay, not of its root.

    /// <summary>
    /// The combined node is itself exposed, so a collision the combination creates is combined too.
    /// Section 15.1 states the rule for every path, not only for the ones the root can see.
    /// </summary>
    [Test]
    public void ACollisionInsideACombinedNodeIsAlsoCombined()
    {
        var item = Container(1, 1).WithSequenceItem(0, SequenceItem.Native(Leaf("p", 1, 2)));
        var child = Container(2).WithChild(Ordinary("0"), Leaf("z", 2, 1));
        var node = Container(1)
            .WithSequenceItem(0, SequenceItem.Native(item))
            .WithChild(Ordinary("0"), child);

        var inner = Expose(node).Sequence[0].Node;

        ReferenceEquals(inner.Children[Ordinary("0")], inner.Sequence[0].Node).ShouldBeTrue();
        Value(inner.Sequence[0].Node).ShouldBe("z");
    }

    /// <summary>
    /// A collision under a child that is not itself combined is still combined.
    /// </summary>
    [Test]
    public void ACollisionBelowAnOrdinaryChildIsCombined()
    {
        var root = Container(1).WithChild(
            Ordinary("x"), Collision("0", itemSource: 1, "q", childSource: 2, "z"));

        var exposed = Expose(root).Children[Ordinary("x")];

        ReferenceEquals(exposed.Children[Ordinary("0")], exposed.Sequence[0].Node).ShouldBeTrue();
    }

    /// <summary>
    /// A collision inside a sequence item is combined, which requires descending through the
    /// sequence facet and not only through mapping children.
    /// </summary>
    [Test]
    public void ACollisionInsideAnUncombinedSequenceItemIsCombined()
    {
        var root = Container(1).WithSequenceItem(
            3, SequenceItem.Native(Collision("0", itemSource: 1, "q", childSource: 2, "z")));

        var exposed = Expose(root).Sequence[3].Node;

        ReferenceEquals(exposed.Children[Ordinary("0")], exposed.Sequence[0].Node).ShouldBeTrue();
    }

    /// <summary>
    /// Exposure adds addresses; it does not disturb the overlay. Section 5.4 keeps the high-water
    /// mark at "the greatest ordering value ever allocated or explicitly supplied at that path", so
    /// a step that rebuilt nodes without carrying it would hand a later item a used value.
    /// </summary>
    [Test]
    public void ExposureLeavesAnOverlayWithoutACollisionAlone()
    {
        var node = Container(1)
            .WithSequenceItem(0, SequenceItem.Native(Leaf("q", 1, 1)))
            .WithChild(Ordinary("x"), Leaf("z", 2))
            .WithPayload(ScalarPayload.Untyped("scalar"), StableOrderingKey.FromSource(2, 1))
            .WithComment(new BoundComment(
                "kept", CommentPlacement.Leading, StableOrderingKey.FromSource(1, 2)))
            .WithReservedOrderingValue(9)
            .WithExplicitMapping(StableOrderingKey.FromSource(3, 0));

        var exposed = Expose(node);

        exposed.SequenceHighWater.ShouldBe(9);
        exposed.HasExplicitMapping.ShouldBeTrue();
        exposed.Marks.Position.ShouldBe(node.Marks.Position);
        Value(exposed).ShouldBe("scalar");
        exposed.Comments.Select(comment => comment.Text).ShouldBe(["kept"]);
        Value(exposed.Sequence[0].Node).ShouldBe("q");
        Value(exposed.Children[Ordinary("x")]).ShouldBe("z");
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 17.1: comments "accumulate and survive merge whenever their logical path survives".
    /// Combining the two facets does not remove the path, so neither set of comments goes with it.
    /// </summary>
    [Test]
    public void CombiningKeepsTheCommentsOfBothFacets()
    {
        var item = Leaf("q", 1, 1).WithComment(new BoundComment(
            "from the item", CommentPlacement.Leading, StableOrderingKey.FromSource(1, 1)));
        var child = Leaf("z", 2).WithComment(new BoundComment(
            "from the key", CommentPlacement.Leading, StableOrderingKey.FromSource(2, 0)));
        var node = Container(1)
            .WithSequenceItem(0, SequenceItem.Native(item))
            .WithChild(Ordinary("0"), child);

        Expose(node).Sequence[0].Node.OrderedComments.Select(comment => comment.Text)
            .ShouldBe(["from the item", "from the key"]);
    }

    // Section 11: "Sequences expose their stable ordering values as decimal name parts."

    /// <summary>
    /// Section 11 exposes <c>a[0]</c> as <c>a.0</c>, so the ordering value is a real address.
    /// </summary>
    [Test]
    public void ASequenceItemIsAddressableByItsOrderingValue()
    {
        var node = Container(1).WithSequenceItem(7, SequenceItem.Native(Leaf("q", 1, 1)));

        OrderingValueExposer.TryResolve(Expose(node), Ordinary("7"), out var item).ShouldBeTrue();
        Value(item!).ShouldBe("q");
    }

    /// <summary>
    /// Section 8.7: "before that phase, numeric mapping keys are ordinary addressable path parts".
    /// </summary>
    [Test]
    public void AMappingChildKeepsItsOwnAddress()
    {
        var node = Container(1).WithChild(Ordinary("x"), Leaf("z", 1));

        OrderingValueExposer.TryResolve(Expose(node), Ordinary("x"), out var child).ShouldBeTrue();
        Value(child!).ShouldBe("z");
    }

    /// <summary>
    /// Section 8.7 admits only the canonical spelling, so <c>00</c> does not address item 0.
    /// </summary>
    [Test]
    public void ANonCanonicalSpellingDoesNotAddressASequenceItem()
    {
        var node = Container(1).WithSequenceItem(0, SequenceItem.Native(Leaf("q", 1, 1)));

        OrderingValueExposer.TryResolve(Expose(node), Ordinary("00"), out _).ShouldBeFalse();
    }

    /// <summary>A name neither facet holds resolves to nothing rather than to an empty node.</summary>
    [Test]
    public void AnUnknownNameResolvesToNothing()
    {
        var node = Container(1).WithSequenceItem(0, SequenceItem.Native(Leaf("q", 1, 1)));

        OrderingValueExposer.TryResolve(Expose(node), Ordinary("1"), out var missing)
            .ShouldBeFalse();
        missing.ShouldBeNull();
    }

    /// <summary>
    /// Section 11's example addresses a whole path through a sequence item, so resolution has to
    /// cross the facet boundary mid-path.
    /// </summary>
    [Test]
    public void APathResolvesThroughASequenceItem()
    {
        var item = Container(1, 1).WithChild(Ordinary("x"), Leaf("q", 1, 2));
        var root = Container(1).WithChild(
            Ordinary("a"), Container(1).WithSequenceItem(0, SequenceItem.Native(item)));

        OrderingValueExposer.TryResolve(
            Expose(root), [Ordinary("a"), Ordinary("0"), Ordinary("x")], out var node)
            .ShouldBeTrue();
        Value(node!).ShouldBe("q");
    }

    /// <summary>A path that leaves the graph part-way resolves to nothing.</summary>
    [Test]
    public void APathThatLeavesTheGraphResolvesToNothing()
    {
        var root = Container(1).WithChild(Ordinary("a"), Leaf("q", 1, 1));

        OrderingValueExposer.TryResolve(
            Expose(root), [Ordinary("a"), Ordinary("missing")], out _)
            .ShouldBeFalse();
    }

    /// <summary>An empty path names the node it starts from.</summary>
    [Test]
    public void AnEmptyPathNamesTheNodeItStartsFrom()
    {
        var root = Container(1).WithChild(Ordinary("a"), Leaf("q", 1, 1));

        OrderingValueExposer.TryResolve(root, [], out var node).ShouldBeTrue();
        ReferenceEquals(node, root).ShouldBeTrue();
    }
}
