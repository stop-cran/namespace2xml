using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 15.1 steps 6 and 7: projecting a native structured document into an overlay.
/// </summary>
/// <remarks>
/// This projection is shared by JSON, YAML, and XML, so every rule proved here is proved once for
/// all three. Documents are written as JSON because that is the format whose reader exists; the
/// behaviour under test belongs to the projection, not to the reader.
/// </remarks>
[TestFixture]
public class StructuredProfileReaderTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private ProfileContribution Project(string document, out UnsupportedCapability? unsupported)
    {
        var limits = ResourceLimits.Defaults;
        var root = JsonInputReader.Read(
            document,
            limits,
            new SourceBudget(limits, 0),
            ProfileSource.OfFile("d.json"),
            DiagnosticPhase.Input,
            diagnostics,
            StableOrderingKey.FromSource(1, 1));

        return StructuredProfileReader.Read(
            root.ShouldNotBeNull(),
            sourceOrdinal: 1,
            ProfileSource.OfFile("d.json"),
            SubstituteModeMap.Default,
            diagnostics,
            out unsupported);
    }

    private OverlayNode Project(string document)
    {
        var contribution = Project(document, out var unsupported);

        unsupported.ShouldBeNull();

        return contribution.Overlay;
    }

    private static OverlayNode Child(OverlayNode node, string name) =>
        node.Children
            .Single(pair => ((OrdinaryPart)pair.Key).LiteralText == name)
            .Value;

    private static string Value(OverlayNode node) =>
        node.Payload.ShouldNotBeNull().ToCanonicalText();

    /// <summary>
    /// Section 4.4: "Empty mappings therefore participate in precedence even though they have no
    /// children." The projection has nothing else to record for <c>{}</c>, so if it does not mark
    /// presence here the contribution disappears entirely.
    /// </summary>
    [Test]
    public void AnEmptyMappingIsAShapeContribution()
    {
        var empty = Child(Project("""{"a":{}}"""), "a");

        empty.HasExplicitMapping.ShouldBeTrue();
        empty.Children.ShouldBeEmpty();
        empty.IsEmpty.ShouldBeFalse();
    }

    /// <summary>
    /// Section 4.4 makes the sequence shape-mark "the latest surviving sequence contribution", and
    /// an empty sequence is one. It is the exact analogue of the empty mapping above, and has the
    /// same consequence: without the mark, <c>[]</c> contributes nothing at all.
    /// </summary>
    [Test]
    public void AnEmptySequenceIsAShapeContribution()
    {
        var empty = Child(Project("""{"a":[]}"""), "a");

        empty.HasExplicitSequence.ShouldBeTrue();
        empty.Sequence.ShouldBeEmpty();
        empty.IsEmpty.ShouldBeFalse();
    }

    /// <summary>
    /// Section 4.4 distinguishes the two container shapes, so an empty sequence must not be
    /// recorded as a mapping and an empty mapping must not be recorded as a sequence. One flag
    /// serving both would make <c>[]</c> and <c>{}</c> indistinguishable in the overlay.
    /// </summary>
    [Test]
    public void TheTwoEmptyContainerShapesAreRecordedSeparately()
    {
        var root = Project("""{"m":{},"s":[]}""");

        Child(root, "m").HasExplicitSequence.ShouldBeFalse();
        Child(root, "s").HasExplicitMapping.ShouldBeFalse();
    }

    /// <summary>
    /// Section 5.4 allocates ordering values "beginning at 0" in the order items appear, and
    /// Section 4.7 keeps document order stable, so an array's items are addressable by position.
    /// </summary>
    [Test]
    public void SequenceItemsTakeConsecutiveOrderingValuesFromZero()
    {
        var sequence = Child(Project("""{"a":["x","y","z"]}"""), "a");

        sequence.OrderedSequence.Select(item => item.Key).ShouldBe([0L, 1L, 2L]);
        sequence.OrderedSequence.Select(item => Value(item.Value.Node))
            .ShouldBe(["x", "y", "z"]);
    }

    /// <summary>
    /// Section 18: "Typed JSON and YAML input scalars retain their source kind without
    /// re-inference." A projection that re-lexed them would turn the JSON string <c>"1"</c> into an
    /// integer and lose the one distinction the format was able to make.
    /// </summary>
    [Test]
    public void TypedScalarsKeepTheKindTheyWereReadWith()
    {
        var root = Project("""{"n":1,"s":"1","b":true,"z":null}""");

        Child(root, "n").Payload.ShouldNotBeNull().Kind.ShouldBe(ScalarKind.Integer);
        Child(root, "s").Payload.ShouldNotBeNull().Kind.ShouldBe(ScalarKind.String);
        Child(root, "b").Payload.ShouldNotBeNull().Kind.ShouldBe(ScalarKind.Boolean);
        Child(root, "z").Payload.ShouldNotBeNull().Kind.ShouldBe(ScalarKind.Null);
    }

    /// <summary>
    /// Section 10.4: "carrier ancestors created only to contain an extracted template do not
    /// contribute mapping-presence marks". A child whose own contribution came to nothing is such a
    /// carrier, so attaching it would give its parent a mapping mark nothing stands behind.
    /// </summary>
    /// <remarks>
    /// The wildcard key is what empties the inner mapping: its subtree leaves as template entries
    /// rather than as concrete data, so <c>a</c> exists in the document only to hold a rule. A
    /// reference-bearing value is by contrast an ordinary Section 13.1 payload held unresolved, and
    /// a genuinely empty mapping carries a presence mark of its own.
    /// </remarks>
    [Test]
    public void AChildThatContributedNothingIsNotAttached()
    {
        var root = Project("""{"a":{"*":1}}""", out var unsupported);

        unsupported.ShouldBeNull();
        root.Overlay.Children.ShouldBeEmpty();
        root.Templates.ShouldHaveSingleItem();
    }

    /// <summary>
    /// Section 13.1 resolves references after "ordinary data merging", so a reference-bearing
    /// native string is an ordinary scalar contribution at its owning path, carrying its text
    /// unresolved rather than becoming a literal or vanishing from the overlay.
    /// </summary>
    [Test]
    public void AReferenceIsRecordedAgainstItsOwningPath()
    {
        var contribution = Project("""{"a":{"b":"${x}"}}""", out var unsupported);

        unsupported.ShouldBeNull();
        diagnostics.Drain().ShouldBeEmpty();

        var payload = Child(Child(contribution.Overlay, "a"), "b").Payload.ShouldNotBeNull();

        payload.IsUnresolved.ShouldBeTrue();
        payload.UnresolvedValue.ContainsReference.ShouldBeTrue();
    }

    /// <summary>
    /// Section 8.4 gives a reference no meaning without an owning name, and a root scalar has none,
    /// so the document is refused rather than silently resolved against nothing.
    /// </summary>
    [Test]
    public void AReferenceAtTheDocumentRootIsRefused()
    {
        Project("\"${x}\"", out _);

        diagnostics.Drain().ShouldHaveSingleItem().Code.ShouldBe("REFERENCE001");
    }

    /// <summary>
    /// Section 9.2 keeps an unescaped asterisk in a native object-property name as a
    /// wildcard-template token, and Section 10.4 extracts the branch it heads "entry-by-entry"
    /// rather than merging it as data.
    /// </summary>
    [Test]
    public void AWildcardInANativeKeyBecomesATemplate()
    {
        var contribution = Project("""{"*":1}""", out var unsupported);

        unsupported.ShouldBeNull();
        diagnostics.Drain().ShouldBeEmpty();

        var template = contribution.Templates.ShouldHaveSingleItem();

        CanonicalPath.Of(template.Name).ShouldBe("*");
        contribution.Overlay.Children.ShouldBeEmpty();
    }

    /// <summary>
    /// Section 10.4 makes extraction entry-by-entry, so a template branch yields one entry per
    /// scalar leaf and each entry carries the whole path from the wildcard key down to that leaf.
    /// </summary>
    [Test]
    public void ATemplateBranchYieldsOneEntryPerScalarLeaf()
    {
        var contribution = Project("""{"a":{"*":{"c":"X","d":{"e":"Y"}}}}""", out var unsupported);

        unsupported.ShouldBeNull();

        contribution.Templates
            .Select(entry => CanonicalPath.Of(entry.Name))
            .ShouldBe(["a.*.c", "a.*.d.e"], ignoreOrder: true);
    }

    /// <summary>
    /// Section 10.4 shows a mapping under a wildcard key and never a sequence. A native sequence
    /// item takes its ordering value from the destination path's Section 5.4 high-water mark, and
    /// under a template the destination is unknown until Section 12.4 expansion, so the shape is
    /// refused rather than resolved to one of its readings.
    /// </summary>
    [Test]
    public void ASequenceUnderAWildcardKeyIsDeclined()
    {
        Project("""{"a":{"*":[1,2]}}""", out var unsupported);

        unsupported.ShouldNotBeNull().Detail.ShouldContain("a.*", Case.Sensitive);
    }

    /// <summary>
    /// An empty mapping has no scalar leaf, so entry-by-entry extraction yields nothing. What it
    /// expresses is a shape rather than a value, and Section 10.4 gives a template no way to carry
    /// one: "carrier ancestors created only to contain an extracted template do not contribute
    /// mapping-presence marks".
    /// </summary>
    [Test]
    public void AnEmptyMappingUnderAWildcardKeyIsDeclined()
    {
        Project("""{"a":{"*":{}}}""", out var unsupported);

        unsupported.ShouldNotBeNull().Detail.ShouldContain("a.*", Case.Sensitive);
    }

    /// <summary>
    /// Section 9.1 keeps <c>\*</c> as the escape for a literal asterisk, so an escaped wildcard is
    /// an ordinary key and must survive projection as one.
    /// </summary>
    [Test]
    public void AnEscapedAsteriskIsAnOrdinaryKey()
    {
        var root = Project("""{"\\*":1}""", out var unsupported);

        unsupported.ShouldBeNull();
        Value(Child(root.Overlay, "*")).ShouldBe("1");
    }

    /// <summary>
    /// Section 4.7 derives every position mark from the source ordinal, so two documents projected
    /// at different ordinals must not produce marks that compare equal. Merging depends on this:
    /// contributions that cannot be ordered cannot be overlaid.
    /// </summary>
    [Test]
    public void PositionMarksCarryTheSourceOrdinal()
    {
        var root = JsonInputReader.Read(
            """{"a":1}""",
            ResourceLimits.Defaults,
            new SourceBudget(ResourceLimits.Defaults, 0),
            ProfileSource.OfFile("d.json"),
            DiagnosticPhase.Input,
            diagnostics,
            StableOrderingKey.FromSource(1, 1));

        var first = StructuredProfileReader.Read(
            root.ShouldNotBeNull(), 1, ProfileSource.OfFile("d.json"), SubstituteModeMap.Default, diagnostics, out _);
        var second = StructuredProfileReader.Read(
            root, 2, ProfileSource.OfFile("d.json"), SubstituteModeMap.Default, diagnostics, out _);

        Child(second.Overlay, "a").Marks.Position
            .ShouldBeGreaterThan(Child(first.Overlay, "a").Marks.Position);
    }
}
