using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Section 19.1 flat projection: which scalars a flat output emits, in which order, and under
/// which container shape.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the projection currently produces.
/// </remarks>
[TestFixture]
public class FlatProjectionTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static OverlayNode Leaf(string text, int source) =>
        OverlayNode.OfPayload(
            ScalarPayload.Untyped(text), StableOrderingKey.FromSource(source, 0));

    private static OverlayNode Container(int source) =>
        OverlayNode.Empty(NodeMarks.At(StableOrderingKey.FromSource(source, 0)));

    private static SequenceItem Item(string text, int source) =>
        SequenceItem.Native(Leaf(text, source));

    private ImmutableArray<FlatEntry> Project(
        OverlayNode view, params string[] root) =>
        new FlatProjection(diagnostics, "out.txt")
            .Project(view, [.. root.Select(Ordinary)]);

    /// <summary>The emitted key paths, spelled with the default delimiter.</summary>
    private static ImmutableArray<string> Keys(ImmutableArray<FlatEntry> entries) =>
        [.. entries.Select(entry => Spell(entry.Path))];

    private static ImmutableArray<string> Values(ImmutableArray<FlatEntry> entries) =>
        [.. entries.Select(entry => entry.Payload.ToCanonicalText())];

    private static string Spell(ImmutableArray<NamePart> path) =>
        string.Join(
            '.',
            path.Select(part => ((OrdinaryPart)part).LiteralText));

    // Section 19.1: the emission order.

    /// <summary>
    /// Section 19.1: a flat projection "visits the selected view depth first in pre-order: a node's
    /// own scalar is emitted before anything beneath it". Section 4.4's worked example is exactly
    /// this pair, <c>a.x=1</c> then <c>a.x.z=3</c>, from one node carrying both projections.
    /// </summary>
    [Test]
    public void ANodesOwnScalarPrecedesEverythingBeneathIt()
    {
        var view = Container(1)
            .WithChild(
                Ordinary("a"),
                Container(1).WithChild(
                    Ordinary("x"),
                    Leaf("1", 1).WithChild(Ordinary("z"), Leaf("3", 2))));

        var entries = Project(view);

        Keys(entries).ShouldBe(["a.x", "a.x.z"]);
        Values(entries).ShouldBe(["1", "3"]);
    }

    /// <summary>
    /// Section 19.1: "its mapping children follow in their Section 5.2 order", which is by position
    /// mark and not by name. A projection sorting by name would pass every test whose input happens
    /// to arrive alphabetically, so the input here does not.
    /// </summary>
    [Test]
    public void MappingChildrenFollowTheirPositionMarks()
    {
        var view = Container(1)
            .WithChild(Ordinary("z"), Leaf("first", 1))
            .WithChild(Ordinary("a"), Leaf("second", 2));

        Keys(Project(view)).ShouldBe(["z", "a"]);
    }

    /// <summary>
    /// Section 19.1: "its sequence items follow in ascending ordering value". Ordering value, not
    /// arrival: Section 5.4 lets a later contribution land on a lower value.
    /// </summary>
    [Test]
    public void SequenceItemsFollowAscendingOrderingValue()
    {
        var view = Container(1)
            .WithSequenceItem(7, Item("late", 1))
            .WithSequenceItem(2, Item("early", 2));

        Values(Project(view)).ShouldBe(["early", "late"]);
    }

    /// <summary>
    /// Section 19.1: "sequences use generated zero-based decimal parts after all concatenation and
    /// merging", and Section 5.4 requires "fresh dense indices where their projection requires
    /// indices". Sparse stable values therefore emit as 0 and 1.
    /// </summary>
    [Test]
    public void SequenceIndicesAreDensifiedInTheEmittedKey()
    {
        var view = Container(1)
            .WithSequenceItem(0, Item("first", 1))
            .WithSequenceItem(5, Item("second", 2));

        Keys(Project(view)).ShouldBe(["0", "1"]);
    }

    /// <summary>
    /// Section 5.4: "matching and precedence continue to use stable ordering values". The densified
    /// key is a spelling, so the entry has to keep naming the path a user wrote or every diagnostic
    /// about it names a path that does not exist.
    /// </summary>
    [Test]
    public void DensificationDoesNotDisturbTheLogicalPath()
    {
        var view = Container(1)
            .WithSequenceItem(0, Item("first", 1))
            .WithSequenceItem(5, Item("second", 2));

        var entries = Project(view);

        Spell(entries[1].Path).ShouldBe("1");
        Spell(entries[1].LogicalPath).ShouldBe("5");
    }

    /// <summary>
    /// Section 16.3: "namespace output prefixes keys with <c>x.y</c>". The root wraps the selected
    /// content uniformly, so it prefixes every entry and not merely the first.
    /// </summary>
    [Test]
    public void TheRootPrefixesEveryEmittedPath()
    {
        var view = Container(1)
            .WithChild(Ordinary("a"), Leaf("1", 1))
            .WithChild(Ordinary("b"), Leaf("2", 2));

        Keys(Project(view, "x", "y")).ShouldBe(["x.y.a", "x.y.b"]);
    }

    /// <summary>
    /// Section 16.3 wraps the selected content, so a scalar at the view root is named by the root
    /// alone rather than losing its key.
    /// </summary>
    [Test]
    public void TheRootNamesAScalarAtTheViewRoot()
    {
        Keys(Project(Leaf("1", 1), "x")).ShouldBe(["x"]);
    }

    /// <summary>
    /// Section 19.6: "container-only paths do not emit keys". Only a scalar produces an entry.
    /// </summary>
    [Test]
    public void AContainerWithoutAScalarEmitsNothing()
    {
        var view = Container(1).WithChild(Ordinary("a"), Container(2));

        Project(view).ShouldBeEmpty();
    }

    // Section 16.4: one container shape.

    /// <summary>
    /// Section 16.4 makes flat output "a destination requiring one container shape", and
    /// Section 17.1 says such a destination "uses the later container contribution". Here the
    /// sequence contribution is later, so the mapping children are not emitted.
    /// </summary>
    [Test]
    public void TheLaterContainerWinsWhenItIsTheSequence()
    {
        var view = Container(1)
            .WithChild(Ordinary("b"), Leaf("mapped", 2))
            .WithSequenceItem(0, Item("indexed", 3));

        var entries = Project(view);

        Keys(entries).ShouldBe(["0"]);
        Values(entries).ShouldBe(["indexed"]);
    }

    /// <summary>
    /// The same rule with the marks the other way round: the mapping contribution is later, so the
    /// sequence items are not emitted. Both directions are asserted because a projection that
    /// always preferred one facet would satisfy either one alone.
    /// </summary>
    [Test]
    public void TheLaterContainerWinsWhenItIsTheMapping()
    {
        var view = Container(1)
            .WithSequenceItem(0, Item("indexed", 2))
            .WithChild(Ordinary("b"), Leaf("mapped", 3));

        var entries = Project(view);

        Keys(entries).ShouldBe(["b"]);
        Values(entries).ShouldBe(["mapped"]);
    }

    /// <summary>
    /// Section 17.1: a destination requiring one container shape "uses the later container
    /// contribution and warns". The warning is <c>TYPE002</c>, shape conflict resolved by
    /// precedence.
    /// </summary>
    [Test]
    public void DroppingAContainerFacetWarns()
    {
        var view = Container(1)
            .WithChild(
                Ordinary("a"),
                Container(1)
                    .WithChild(Ordinary("b"), Leaf("mapped", 2))
                    .WithSequenceItem(0, Item("indexed", 3)));

        Project(view);

        var warning = diagnostics.Drain().ShouldHaveSingleItem();

        warning.Code.ShouldBe("TYPE002");
        warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
        warning.Path.ShouldBe("a");
        warning.Destination.ShouldBe("out.txt");
    }

    /// <summary>
    /// A node with one container facet has nothing to resolve by precedence, so warning there would
    /// make <c>TYPE002</c> mean "this node has children" and tell a reader nothing.
    /// </summary>
    [Test]
    public void OneContainerFacetDoesNotWarn()
    {
        Project(Container(1).WithChild(Ordinary("a"), Leaf("1", 2)));
        Project(Container(1).WithSequenceItem(0, Item("1", 2)));

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 19.6: "no shape warning is emitted merely because one logical path supplies both
    /// projections", for a scalar beside a container. Flat output emits both, so there is no
    /// conflict to resolve; only the two container facets contest each other.
    /// </summary>
    [Test]
    public void AScalarBesideAContainerDoesNotWarn()
    {
        var view = Container(1)
            .WithChild(Ordinary("a"), Leaf("1", 1).WithChild(Ordinary("z"), Leaf("3", 2)));

        Keys(Project(view)).ShouldBe(["a", "a.z"]);
        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// The warning names the path whose facets conflicted, so <c>TYPE002</c>'s "once per path and
    /// output instance" cardinality separates two conflicting paths instead of collapsing them.
    /// </summary>
    [Test]
    public void EachConflictingPathWarnsSeparately()
    {
        OverlayNode Conflicted(int source) =>
            Container(source)
                .WithChild(Ordinary("b"), Leaf("mapped", source))
                .WithSequenceItem(0, Item("indexed", source + 1));

        var view = Container(1)
            .WithChild(Ordinary("a"), Conflicted(2))
            .WithChild(Ordinary("c"), Conflicted(4));

        Project(view);

        diagnostics.Drain().Select(entry => entry.Path).ShouldBe(["a", "c"]);
    }

    /// <summary>
    /// Section 4.5 binds comments to the node that carries the value, and Section 19.1 emits them
    /// beside their key, so an entry has to carry its own node's comments and not its parent's.
    /// </summary>
    [Test]
    public void AnEntryCarriesItsOwnNodesComments()
    {
        var view = Container(1)
            .WithComment(new BoundComment("root", CommentPlacement.Leading, StableOrderingKey.FromSource(1, 0)))
            .WithChild(
                Ordinary("a"),
                Leaf("1", 2).WithComment(
                    new BoundComment("mine", CommentPlacement.Leading, StableOrderingKey.FromSource(2, 1))));

        var entry = Project(view).ShouldHaveSingleItem();

        entry.Comments.Select(comment => comment.Text).ShouldBe(["mine"]);
    }

    /// <summary>
    /// Section 19.1 gives null a canonical spelling, so a null payload is a scalar with a key and
    /// not an absent one.
    /// </summary>
    [Test]
    public void ANullPayloadIsAnEmittedScalar()
    {
        var view = Container(1)
            .WithChild(
                Ordinary("a"),
                OverlayNode.Empty(NodeMarks.At(StableOrderingKey.FromSource(2, 0)))
                    .WithPayload(ScalarPayload.Null, StableOrderingKey.FromSource(2, 0)));

        var entry = Project(view).ShouldHaveSingleItem();

        entry.Payload.IsNull.ShouldBeTrue();
        Spell(entry.Path).ShouldBe("a");
    }

    /// <summary>
    /// Pre-order over a whole subtree: a branch is finished before its sibling begins, which a
    /// breadth-first walk would reverse.
    /// </summary>
    [Test]
    public void ASubtreeIsFinishedBeforeItsSiblingBegins()
    {
        var view = Container(1)
            .WithChild(
                Ordinary("a"),
                Container(1).WithChild(Ordinary("deep"), Leaf("1", 1)))
            .WithChild(Ordinary("b"), Leaf("2", 2));

        Keys(Project(view)).ShouldBe(["a.deep", "b"]);
    }

    /// <summary>
    /// Section 19.1 orders sequence items by ordering value and mapping children by position mark,
    /// and a node emits one facet, so an item's own descendants are still walked in pre-order.
    /// </summary>
    [Test]
    public void ASequenceItemsDescendantsAreWalkedInPreOrder()
    {
        var view = Container(1)
            .WithSequenceItem(
                0,
                SequenceItem.Native(Leaf("own", 1).WithChild(Ordinary("k"), Leaf("under", 2))))
            .WithSequenceItem(1, Item("next", 3));

        var entries = Project(view);

        Keys(entries).ShouldBe(["0", "0.k", "1"]);
        Values(entries).ShouldBe(["own", "under", "next"]);
    }
}
