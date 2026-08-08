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
/// The Section 4.4 exclusive-shape projection JSON and YAML share: which facet of an overlay node
/// survives when a path supplies more than one, and what the loss is reported as.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the projection currently produces.
/// </remarks>
[TestFixture]
public class DocumentProjectionTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static OverlayNode Leaf(string text, int source) =>
        OverlayNode.OfPayload(ScalarPayload.Untyped(text), StableOrderingKey.FromSource(source, 0));

    private static OverlayNode Container(int source) =>
        OverlayNode.Empty(NodeMarks.At(StableOrderingKey.FromSource(source, 0)));

    private static SequenceItem Item(string text, int source) =>
        SequenceItem.Native(Leaf(text, source));

    private DocumentNode Project(OverlayNode view, params string[] root) =>
        new DocumentProjection(diagnostics, "\u00a719.3", new DestinationRef("out.json", 0))
            .Project(view, [.. root.Select(Ordinary)]);

    private static string Text(DocumentNode node) =>
        ((DocumentScalar)node).Payload.ToCanonicalText();

    private static DocumentNode Member(DocumentNode node, string key) =>
        ((DocumentMapping)node).Members.Single(member => member.Key == key).Value;

    // ---- Section 4.4 exclusive shape -------------------------------------------------------------

    /// <summary>
    /// Section 4.4's own example: a namespace emitting both <c>a.x=1</c> and <c>a.x.z=3</c> makes
    /// "JSON and YAML render <c>x</c> as an object containing <c>z</c>, omit scalar <c>1</c>, and
    /// warn".
    /// </summary>
    [Test]
    public void AMappingArrivingLaterWinsAgainstAScalar()
    {
        var view = Container(1)
            .WithChild(Ordinary("x"), Leaf("1", 1).WithChild(Ordinary("z"), Leaf("3", 2)));

        var x = Member(Project(view), "x");

        Text(Member(x, "z")).ShouldBe("3");
        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("TYPE002");
    }

    /// <summary>
    /// Section 4.4 continues: "reversing source order makes the later scalar win in JSON and YAML".
    /// The contest is decided by position, not by a preference for containers.
    /// </summary>
    [Test]
    public void AScalarArrivingLaterWinsAgainstAMapping()
    {
        var view = Container(1)
            .WithChild(
                Ordinary("x"),
                Container(1).WithChild(Ordinary("z"), Leaf("3", 1)).WithPayload(
                    ScalarPayload.Untyped("1"), StableOrderingKey.FromSource(2, 0)));

        Text(Member(Project(view), "x")).ShouldBe("1");
        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("TYPE002");
    }

    /// <summary>
    /// Section 4.4 makes the later of the two container contributions win as well, so a sequence
    /// arriving after a mapping renders as a sequence.
    /// </summary>
    [Test]
    public void TheLaterContainerContributionWins()
    {
        var view = Container(1)
            .WithChild(
                Ordinary("x"),
                Container(1)
                    .WithChild(Ordinary("z"), Leaf("mapped", 1))
                    .WithSequenceItem(0, Item("indexed", 2)));

        var items = ((DocumentSequence)Member(Project(view), "x")).Items;

        Text(items.ShouldHaveSingleItem()).ShouldBe("indexed");
        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("TYPE002");
    }

    /// <summary>
    /// A node supplying only one shape has no contest to lose, so Section 4.4 has nothing to warn
    /// about and the run is silent.
    /// </summary>
    [Test]
    public void OneShapePerNodeWarnsNothing()
    {
        var view = Container(1)
            .WithChild(Ordinary("a"), Leaf("1", 1))
            .WithChild(Ordinary("b"), Container(2).WithSequenceItem(0, Item("x", 3)));

        Project(view);

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// <c>TYPE002</c> is counted "once per path and output instance", so a node that loses both a
    /// payload and a container produces one diagnostic naming both omissions rather than two.
    /// </summary>
    [Test]
    public void LosingTwoFacetsAtOneNodeStillWarnsOnce()
    {
        var view = Container(1)
            .WithChild(
                Ordinary("x"),
                Container(1)
                    .WithPayload(ScalarPayload.Untyped("1"), StableOrderingKey.FromSource(1, 0))
                    .WithSequenceItem(0, Item("indexed", 2))
                    .WithChild(Ordinary("z"), Leaf("mapped", 3)));

        Project(view);

        var warning = diagnostics.Drain().ShouldHaveSingleItem();

        warning.Code.ShouldBe("TYPE002");
        warning.Message.ShouldContain("the scalar");
        warning.Message.ShouldContain("the sequence items");
    }

    /// <summary>
    /// <c>TYPE002</c> names the path that lost a facet, which is how a reader finds the value that
    /// is missing from the output.
    /// </summary>
    [Test]
    public void TheWarningNamesTheLosingPath()
    {
        var view = Container(1)
            .WithChild(
                Ordinary("outer"),
                Container(1).WithChild(
                    Ordinary("inner"),
                    Leaf("1", 1).WithChild(Ordinary("z"), Leaf("3", 2))));

        Project(view);

        diagnostics.Drain().ShouldHaveSingleItem().Path.ShouldBe("outer.inner");
    }

    // ---- Section 16.3 root -----------------------------------------------------------------------

    /// <summary>
    /// Section 16.3: <c>root=x.y</c> makes JSON emit <c>{"x":{"y":...}}</c>, so the root wraps the
    /// document in nested single-member mappings rather than prefixing a key.
    /// </summary>
    [Test]
    public void TheRootWrapsTheDocumentInNestedMappings()
    {
        var document = Project(Container(1).WithChild(Ordinary("a"), Leaf("1", 1)), "x", "y");

        Text(Member(Member(Member(document, "x"), "y"), "a")).ShouldBe("1");
    }

    // ---- Section 14.1 empty and bare documents ---------------------------------------------------

    /// <summary>
    /// Section 14.1: an output view containing nothing emits an empty mapping.
    /// </summary>
    [Test]
    public void AnEmptyViewProjectsAnEmptyMapping() =>
        ((DocumentMapping)Project(Container(1))).Members.ShouldBeEmpty();

    /// <summary>
    /// Section 14.1 permits JSON and YAML to "emit a scalar document", so a view that is itself a
    /// payload projects a scalar rather than a mapping wrapping one.
    /// </summary>
    [Test]
    public void ABareScalarViewProjectsAScalarDocument() =>
        Text(Project(Leaf("bare", 1))).ShouldBe("bare");

    /// <summary>
    /// Section 5.4 renders sequence items in ascending ordering value, not in contribution order,
    /// so a later contribution with a lower ordering value is emitted first.
    /// </summary>
    /// <remarks>
    /// The ordering values are large and adversarially chosen rather than small and readable.
    /// <c>ImmutableDictionary&lt;long, T&gt;</c> enumerates every small key in ascending order
    /// anyway — a search over three-key sets below 100,000 found no counterexample — so a test
    /// using values such as <c>2, 7, 40</c> passes with the sort deleted and proves nothing. These
    /// four enumerate as <c>fourth, third, first, second</c> when the sort is removed, which is the
    /// only reason to prefer them. Section 5.4 permits the whole range "0 through
    /// 9,223,372,036,854,775,807", so they are ordinary values.
    /// </remarks>
    [Test]
    public void SequenceItemsFollowAscendingOrderingValue()
    {
        var view = Container(1)
            .WithSequenceItem(3525628317485390336, Item("second", 1))
            .WithSequenceItem(8015552281036764160, Item("fourth", 2))
            .WithSequenceItem(6080635329187212288, Item("third", 3))
            .WithSequenceItem(480807690918821696, Item("first", 4));

        var items = ((DocumentSequence)Project(view)).Items;

        items.Select(Text).ShouldBe(["first", "second", "third", "fourth"]);
    }

    /// <summary>
    /// Section 5.2 renders mapping members in their model order, which the projection carries
    /// through rather than sorting by key.
    /// </summary>
    [Test]
    public void MappingMembersFollowModelOrder()
    {
        var view = Container(1)
            .WithChild(Ordinary("z"), Leaf("1", 1))
            .WithChild(Ordinary("a"), Leaf("2", 2));

        ((DocumentMapping)Project(view)).Members.Select(member => member.Key).ShouldBe(["z", "a"]);
    }
}
