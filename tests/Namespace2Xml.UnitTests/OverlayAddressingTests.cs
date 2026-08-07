using System.Collections.Immutable;
using Namespace2Xml.Overlay;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 12.4 candidate enumeration: the walk both the wildcard evaluator and the Section 14.1
/// selector expander share.
/// </summary>
/// <remarks>
/// The pattern-aware overload exists only to avoid enumerating subtrees a pattern's own literal
/// parts already exclude, so the property that matters is that it yields exactly the subsequence
/// the unpruned walk yields for the same pattern. These tests assert that equality directly rather
/// than asserting a hand-listed result, because a pruning bug that also happened to match a
/// hand-listed expectation is precisely the failure this is guarding against.
/// </remarks>
[TestFixture]
public class OverlayAddressingTests
{
    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static OrdinaryPart Wildcard() => new([new WildcardToken(null)]);

    private static ImmutableArray<NamePart> Pattern(params NamePart[] parts) => [.. parts];

    private static OverlayNode Leaf(int source, int item) =>
        OverlayNode.OfPayload(
            ScalarPayload.Untyped($"v{source}.{item}"), StableOrderingKey.FromSource(source, item));

    private static string Text(ImmutableArray<NamePart> path) =>
        CanonicalPath.Of(path) ?? string.Empty;

    /// <summary>
    /// A model with two mapping children, one of which also holds a native sequence, so a literal
    /// pattern part can be asked to follow either facet.
    /// </summary>
    private static OverlayNode Model()
    {
        var a = OverlayNode
            .Empty(NodeMarks.At(StableOrderingKey.FromSource(0, 1)))
            .WithChild(Ordinary("x"), Leaf(0, 2))
            .WithChild(Ordinary("y"), Leaf(0, 3));

        var b = OverlayNode
            .Empty(NodeMarks.At(StableOrderingKey.FromSource(0, 4)))
            .WithChild(Ordinary("x"), Leaf(0, 5));

        b.TryAppendSequenceItem(SequenceItem.Native(Leaf(0, 6)), out b).ShouldBeTrue();
        b.TryAppendSequenceItem(SequenceItem.Native(Leaf(0, 7)), out b).ShouldBeTrue();

        return OverlayNode
            .Empty(NodeMarks.At(StableOrderingKey.First))
            .WithChild(Ordinary("a"), a)
            .WithChild(Ordinary("b"), b);
    }

    /// <summary>
    /// Section 12.4's second condition, "every literal name part before that point equals the
    /// corresponding item part", selects the same items whether it is applied as a filter over the
    /// whole model or as a restriction on the walk.
    /// </summary>
    /// <param name="first">The pattern's first part.</param>
    [TestCase("a")]
    [TestCase("b")]
    [TestCase("absent")]
    public void ALiteralLeadingPartAdmitsExactlyWhatTheFilterAdmits(string first)
    {
        var pattern = Pattern(Ordinary(first), Wildcard());

        var filtered = OverlayAddressing.Candidates(Model(), 2)
            .Where(path => path[0].Equals(Ordinary(first)))
            .Select(Text);

        OverlayAddressing.Candidates(Model(), pattern, 2).Select(Text)
            .ShouldBe(filtered);
    }

    /// <summary>
    /// Section 15.1 makes a numeric mapping child and the sequence item at its ordering value one
    /// node, so a literal part naming an ordering value has to reach the item through the sequence
    /// facet as well as through the mapping facet.
    /// </summary>
    [TestCase("0")]
    [TestCase("1")]
    public void ALiteralPartFollowsASequenceItem(string value)
    {
        OverlayAddressing.Candidates(Model(), Pattern(Ordinary("b"), Ordinary(value)), 2)
            .Select(Text)
            .ShouldBe([$"b.{value}"]);
    }

    /// <summary>A pattern of wildcards excludes nothing, so it yields the whole depth.</summary>
    [Test]
    public void AWildcardPatternAdmitsEveryItemAtTheDepth()
    {
        OverlayAddressing.Candidates(Model(), Pattern(Wildcard(), Wildcard()), 2).Select(Text)
            .ShouldBe(OverlayAddressing.Candidates(Model(), 2).Select(Text));
    }

    /// <summary>
    /// A literal part deeper than the first prunes as well, which is the case a walk that only
    /// indexed the leading part would get wrong.
    /// </summary>
    [Test]
    public void ALiteralPartBelowAWildcardAlsoPrunes()
    {
        var pattern = Pattern(Wildcard(), Ordinary("x"));

        var filtered = OverlayAddressing.Candidates(Model(), 2)
            .Where(path => path[1].Equals(Ordinary("x")))
            .Select(Text);

        OverlayAddressing.Candidates(Model(), pattern, 2).Select(Text)
            .ShouldBe(filtered);
    }

    /// <summary>
    /// Section 12.4 asks for "the distinct depth-k prefixes of existing paths", so a pattern longer
    /// than the depth it is walked to must not consult the parts beyond it.
    /// </summary>
    [Test]
    public void PartsBeyondTheDepthAreNotConsulted()
    {
        var pattern = Pattern(Ordinary("a"), Wildcard(), Ordinary("never"));

        OverlayAddressing.Candidates(Model(), pattern, 2).Select(Text)
            .ShouldBe(["a.x", "a.y"]);
    }
}
