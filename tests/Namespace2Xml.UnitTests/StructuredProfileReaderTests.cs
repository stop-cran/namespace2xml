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

    private ProfileContribution Contribution(string document)
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
            diagnostics);
    }

    private OverlayNode Project(string document)
    {
        return Contribution(document).Overlay;
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
        var root = Contribution("""{"a":{"*":1}}""");

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
        var contribution = Contribution("""{"a":{"b":"${x}"}}""");

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
        Project("\"${x}\"");

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
        var contribution = Contribution("""{"*":1}""");

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
        var contribution = Contribution("""{"a":{"*":{"c":"X","d":{"e":"Y"}}}}""");


        contribution.Templates
            .Select(entry => CanonicalPath.Of(entry.Name))
            .ShouldBe(["a.*.c", "a.*.d.e"], ignoreOrder: true);
    }

    /// <summary>
    /// Section 10.4 extracts a sequence beneath a wildcard key "through its items' ordering
    /// values, which Section 5.4 exposes as decimal name parts", so the entries are the ones the
    /// equivalent namespace template would declare.
    /// </summary>
    [Test]
    public void ASequenceUnderAWildcardKeyIsExtractedThroughItsOrderingValues()
    {
        Contribution("""{"a":{"*":[1,2]}}""").Templates
            .Select(entry => CanonicalPath.Of(entry.Name))
            .ShouldBe(["a.*.0", "a.*.1"], ignoreOrder: true);

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Extraction is entry-by-entry all the way down, so a mapping inside a sequence item
    /// contributes the ordering value and the member name as two further parts.
    /// </summary>
    [Test]
    public void ASequenceOfMappingsUnderAWildcardKeyExtractsThroughBoth()
    {
        Contribution("""{"a":{"*":[{"c":1},{"c":2}]}}""").Templates
            .Select(entry => CanonicalPath.Of(entry.Name))
            .ShouldBe(["a.*.0.c", "a.*.1.c"], ignoreOrder: true);
    }

    /// <summary>
    /// Section 10.4: an empty mapping or an empty sequence "has no entries for entry-by-entry
    /// extraction to find, so the template would contribute nothing at every path it matched. That
    /// is PARSE001 against this section, once per failing source".
    /// </summary>
    /// <param name="document">A document whose wildcard key names an empty container.</param>
    [TestCase("""{"a":{"*":{}}}""", TestName = "AnEmptyMappingUnderAWildcardKeyIsParse001")]
    [TestCase("""{"a":{"*":[]}}""", TestName = "AnEmptySequenceUnderAWildcardKeyIsParse001")]
    public void AnEmptyContainerUnderAWildcardKeyIsRejected(string document)
    {
        Contribution(document).Templates.ShouldBeEmpty();

        var reported = diagnostics.Drain().ShouldHaveSingleItem();

        reported.Code.ShouldBe("PARSE001");
        reported.Spec.ShouldBe("\u00A710.4");
        reported.Message.ShouldContain("a.*", Case.Sensitive);
    }

    /// <summary>
    /// Section 9.1 keeps <c>\*</c> as the escape for a literal asterisk, so an escaped wildcard is
    /// an ordinary key and must survive projection as one.
    /// </summary>
    [Test]
    public void AnEscapedAsteriskIsAnOrdinaryKey()
    {
        var root = Contribution("""{"\\*":1}""");

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
            root.ShouldNotBeNull(), 1, ProfileSource.OfFile("d.json"), SubstituteModeMap.Default, diagnostics);
        var second = StructuredProfileReader.Read(
            root, 2, ProfileSource.OfFile("d.json"), SubstituteModeMap.Default, diagnostics);

        Child(second.Overlay, "a").Marks.Position
            .ShouldBeGreaterThan(Child(first.Overlay, "a").Marks.Position);
    }
}
