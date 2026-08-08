using System.Collections.Immutable;
using System.Text;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 19.4 YAML serialization, its Section 16.9 options, and the Section 20 comment rule.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the serializer currently produces.
/// </remarks>
[TestFixture]
public class YamlSerializerTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private static DocumentScalar Scalar(ScalarPayload payload) => new(payload, []);

    private static DocumentScalar Text(string value) => Scalar(ScalarPayload.OfString(value));

    private static DocumentMapping Map(params (string Key, DocumentNode Value)[] members) =>
        new([.. members.Select(member => new DocumentMember(member.Key, member.Value))], []);

    private static DocumentSequence Seq(params DocumentNode[] items) => new([.. items], []);

    private static BoundComment Comment(string text, CommentPlacement placement) =>
        new(text, placement, StableOrderingKey.First);

    private string Serialize(YamlOutputOptions options, DocumentNode document)
    {
        var writer = new OutputBufferWriter(new GlobalBudget(ResourceLimits.Defaults));

        new YamlSerializer(options, diagnostics, new DestinationRef("out.yaml", 0))
            .TrySerialize(document, writer)
            .ShouldBeTrue();

        return Encoding.UTF8.GetString(writer.Build().ToArray());
    }

    private string Serialize(DocumentNode document) => Serialize(YamlOutput.Default, document);

    // ---- Section 19.4 layout ---------------------------------------------------------------------

    /// <summary>
    /// Section 19.4 "does not emit <c>---</c>", so a document begins with its own first line.
    /// </summary>
    [Test]
    public void NoDocumentStartMarkerIsEmitted() =>
        Serialize(Map(("a", Text("1")))).ShouldBe("a: '1'\n");

    /// <summary>
    /// A nested mapping is a block mapping under its key, indented two spaces per level.
    /// </summary>
    [Test]
    public void NestedMappingsIndentTwoSpacesPerLevel() =>
        Serialize(Map(("a", Map(("b", Map(("c", Text("v"))))))))
            .ShouldBe("a:\n  b:\n    c: v\n");

    /// <summary>
    /// Section 19.4 "preserves mapping and sequence order", so members are emitted in document
    /// order rather than sorted by key.
    /// </summary>
    [Test]
    public void MappingOrderIsThePreservedDocumentOrderNotKeyOrder() =>
        Serialize(Map(("z", Text("x")), ("a", Text("y"))))
            .ShouldBe("z: x\na: y\n");

    /// <summary>
    /// A sequence under a key is a block sequence, whose items are indented under it. The two
    /// characters of the <c>-</c> indicator occupy the same width as one indentation step, so the
    /// item content aligns with a mapping value at the same depth.
    /// </summary>
    [Test]
    public void ASequenceUnderAKeyIsABlockSequence() =>
        Serialize(Map(("a", Seq(Text("x"), Text("y")))))
            .ShouldBe("a:\n  - x\n  - y\n");

    /// <summary>
    /// Section 14.1 permits YAML to emit a document that is not a mapping, so a top-level sequence
    /// needs no key and its items start at column zero.
    /// </summary>
    [Test]
    public void ATopLevelSequenceNeedsNoKey() =>
        Serialize(Seq(Text("x"), Text("y"))).ShouldBe("- x\n- y\n");

    /// <summary>
    /// A mapping inside a sequence item shares the item's first line, which is YAML's compact
    /// notation. The remaining members align under it.
    /// </summary>
    [Test]
    public void AMappingInsideASequenceItemSharesTheItemLine() =>
        Serialize(Seq(Map(("k", Text("1")), ("j", Text("2"))), Map(("k", Text("3")))))
            .ShouldBe("- k: '1'\n  j: '2'\n- k: '3'\n");

    /// <summary>
    /// Nesting a sequence inside a sequence item follows the same rule: the inner sequence's first
    /// item shares the outer item's line.
    /// </summary>
    [Test]
    public void ASequenceInsideASequenceItemSharesTheItemLine() =>
        Serialize(Seq(Seq(Text("x"), Text("y")))).ShouldBe("- - x\n  - y\n");

    /// <summary>
    /// Every enclosing sequence contributes its own indicator to the shared first line, so the
    /// depth a reader parses back is the depth that was written. One indicator is two columns wide,
    /// which is what makes the later items of an inner sequence line up beneath it.
    /// </summary>
    [Test]
    public void EveryEnclosingSequenceContributesItsIndicator() =>
        Serialize(Seq(Seq(Seq(Text("x"), Text("y"))))).ShouldBe("- - - x\n    - y\n");

    /// <summary>
    /// A block scalar as a sequence item indents its content beyond the indicator, so the reader's
    /// detected block indentation excludes the item marker.
    /// </summary>
    [Test]
    public void ABlockScalarAsASequenceItemIndentsBeyondTheIndicator() =>
        Serialize(Seq(Text("a\nb\n"))).ShouldBe("- |\n    a\n    b\n");

    /// <summary>
    /// A sequence reached through a mapping key inside a sequence item nests under that key rather
    /// than sharing the item's line, because the key already occupies it.
    /// </summary>
    [Test]
    public void ASequenceUnderAKeyInsideASequenceItemNestsUnderTheKey() =>
        Serialize(Seq(Map(("a", Seq(Text("x")))))).ShouldBe("- a:\n    - x\n");

    /// <summary>
    /// A comment on a sequence item belongs to the item, so it is written at the item's own column
    /// — the one the indicator occupies — and not indented into the item's content.
    /// </summary>
    [Test]
    public void ACommentOnASequenceItemSitsAtTheIndicatorColumn() =>
        Serialize(Seq(new DocumentScalar(
            ScalarPayload.OfString("v"),
            [Comment("why", CommentPlacement.Leading)])))
            .ShouldBe("# why\n- v\n");

    /// <summary>
    /// A block collection has no empty spelling, so an empty mapping or sequence is written in the
    /// flow form. Section 14.1 requires an empty view to emit an empty mapping, which is the
    /// top-level case of the same rule.
    /// </summary>
    /// <param name="document">The empty container.</param>
    /// <param name="expected">Its Section 19.4 spelling.</param>
    [TestCaseSource(nameof(EmptyContainers))]
    public void AnEmptyContainerUsesTheFlowForm(DocumentNode document, string expected) =>
        Serialize(document).ShouldBe(expected);

    private static IEnumerable<TestCaseData> EmptyContainers()
    {
        yield return new TestCaseData(Map(), "{}\n").SetName("AnEmptyDocumentIsAnEmptyMapping");
        yield return new TestCaseData(Seq(), "[]\n").SetName("AnEmptySequenceDocumentIsFlow");
        yield return new TestCaseData(Map(("a", Map())), "a: {}\n").SetName("AnEmptyMappingValueIsFlow");
        yield return new TestCaseData(Map(("a", Seq())), "a: []\n").SetName("AnEmptySequenceValueIsFlow");
    }

    /// <summary>
    /// Section 14.1 permits YAML to emit a scalar document, so a bare scalar view is written alone
    /// rather than under a synthesized key.
    /// </summary>
    [Test]
    public void ABareScalarIsAScalarDocument() => Serialize(Text("bare")).ShouldBe("bare\n");

    // ---- Section 19.4 scalars --------------------------------------------------------------------

    /// <summary>
    /// Section 19.4 "preserves supported scalar types", so a Boolean, an integer and a null are
    /// emitted plain and read back as themselves under Section 10.1.
    /// </summary>
    /// <param name="payload">The typed payload.</param>
    /// <param name="expected">Its plain spelling.</param>
    [TestCaseSource(nameof(TypedScalars))]
    public void TypedScalarsAreEmittedPlain(ScalarPayload payload, string expected) =>
        Serialize(Map(("k", Scalar(payload)))).ShouldBe("k: " + expected + "\n");

    private static IEnumerable<TestCaseData> TypedScalars()
    {
        yield return new TestCaseData(ScalarPayload.OfBoolean(true), "true").SetName("TrueIsPlain");
        yield return new TestCaseData(ScalarPayload.OfBoolean(false), "false").SetName("FalseIsPlain");
        yield return new TestCaseData(ScalarPayload.OfInteger(42), "42").SetName("AnIntegerIsPlain");
        yield return new TestCaseData(ScalarPayload.Null, "null").SetName("NullIsPlain");
    }

    /// <summary>
    /// Section 19.4: "A string whose plain spelling would resolve to a non-string kind under
    /// <c>RestrictedYaml1</c> is emitted single-quoted, with a literal single quote doubled as
    /// <c>''</c>."
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <param name="expected">Its Section 19.4 spelling.</param>
    [TestCase("true", "'true'")]
    [TestCase("42", "'42'")]
    [TestCase("", "''")]
    [TestCase("yes", "yes")]
    [TestCase("it's", "it's")]
    [TestCase("'quoted'", "'''quoted'''")]
    public void AStringThatWouldResolveOtherwiseIsSingleQuoted(string value, string expected) =>
        Serialize(Map(("k", Text(value)))).ShouldBe("k: " + expected + "\n");

    /// <summary>
    /// Section 19.4 "uses literal block scalars for multiline values", so a value with a line break
    /// is not escaped onto one line.
    /// </summary>
    [Test]
    public void AMultilineValueUsesALiteralBlockScalar() =>
        Serialize(Map(("k", Text("first\nsecond\n"))))
            .ShouldBe("k: |\n  first\n  second\n");

    /// <summary>
    /// The chomping indicator is what makes a block scalar exact: the plain form keeps one trailing
    /// line break, <c>-</c> keeps none, and <c>+</c> keeps every one. Choosing wrongly changes the
    /// value, which Section 3 forbids.
    /// </summary>
    /// <param name="value">The multiline value.</param>
    /// <param name="expected">Its Section 19.4 spelling.</param>
    [TestCase("a\nb", "k: |-\n  a\n  b\n")]
    [TestCase("a\nb\n", "k: |\n  a\n  b\n")]
    [TestCase("a\nb\n\n", "k: |+\n  a\n  b\n\n")]
    public void TheChompingIndicatorPreservesTrailingLineBreaksExactly(string value, string expected) =>
        Serialize(Map(("k", Text(value)))).ShouldBe(expected);

    /// <summary>
    /// An empty line inside block content is written empty rather than indented, so the value gains
    /// no trailing whitespace it did not have.
    /// </summary>
    [Test]
    public void AnInteriorEmptyLineIsWrittenEmpty() =>
        Serialize(Map(("k", Text("a\n\nb\n"))))
            .ShouldBe("k: |\n  a\n\n  b\n");

    /// <summary>
    /// A block scalar reproduces its content literally, so a value carrying trailing whitespace or
    /// a carriage return cannot use one and falls back to the double-quoted form, which is exact.
    /// </summary>
    /// <param name="value">A multiline value a block scalar cannot carry.</param>
    /// <param name="expected">Its Section 19.4 spelling.</param>
    [TestCase("a \nb", "k: \"a \\nb\"\n")]
    [TestCase("a\r\nb", "k: \"a\\r\\nb\"\n")]
    public void AValueABlockScalarCannotCarryIsDoubleQuoted(string value, string expected) =>
        Serialize(Map(("k", Text(value)))).ShouldBe(expected);

    /// <summary>
    /// A block scalar nests under its key like any other value, so its content is indented one step
    /// deeper than the key at whatever depth the key sits.
    /// </summary>
    [Test]
    public void ABlockScalarIndentsRelativeToItsKey() =>
        Serialize(Map(("a", Map(("k", Text("x\ny\n"))))))
            .ShouldBe("a:\n  k: |\n    x\n    y\n");

    // ---- Section 20 comments ---------------------------------------------------------------------

    /// <summary>
    /// Section 19.4 "emits retained comments in normalized positions", and Section 4.5 binds a
    /// leading comment to the entry that follows it.
    /// </summary>
    [Test]
    public void ALeadingCommentPrecedesItsOwner() =>
        Serialize(new DocumentMapping(
            [new DocumentMember(
                "a",
                new DocumentScalar(ScalarPayload.OfString("v"), [Comment("why", CommentPlacement.Leading)]))],
            []))
            .ShouldBe("# why\na: v\n");

    /// <summary>
    /// Section 4.5 puts an inline comment "on the same logical line as the entry it belongs to",
    /// which YAML spells after the value with a space before the <c>#</c>.
    /// </summary>
    [Test]
    public void AnInlineCommentFollowsItsValueOnTheSameLine() =>
        Serialize(new DocumentMapping(
            [new DocumentMember(
                "a",
                new DocumentScalar(ScalarPayload.OfString("v"), [Comment("why", CommentPlacement.Inline)]))],
            []))
            .ShouldBe("a: v # why\n");

    /// <summary>
    /// Section 4.5 binds a trailing comment to the entry that precedes it, so it is emitted after
    /// its owner and before whatever follows.
    /// </summary>
    [Test]
    public void ATrailingCommentFollowsItsOwner() =>
        Serialize(new DocumentMapping(
            [
                new DocumentMember(
                    "a",
                    new DocumentScalar(ScalarPayload.OfString("v"), [Comment("after", CommentPlacement.Trailing)])),
                new DocumentMember("b", Text("w")),
            ],
            []))
            .ShouldBe("a: v\n# after\nb: w\n");

    /// <summary>
    /// A comment on a mapping that has a key of its own is emitted around that key line, so the
    /// container's comment does not migrate onto its first child.
    /// </summary>
    [Test]
    public void ACommentOnANestedContainerSitsOnItsKeyLine() =>
        Serialize(Map((
            "a",
            new DocumentMapping(
                [new DocumentMember("b", Text("v"))],
                [Comment("about a", CommentPlacement.Inline)]))))
            .ShouldBe("a: # about a\n  b: v\n");

    /// <summary>
    /// A container written without a key has no line of its own — its first line belongs to its
    /// first child — so an inline comment has no inline spelling there. Section 19.4 normalizes the
    /// position rather than dropping the comment.
    /// </summary>
    [Test]
    public void AnInlineCommentWithNoLineToSitOnBecomesLeading() =>
        Serialize(new DocumentMapping(
            [new DocumentMember("a", Text("v"))],
            [Comment("about the document", CommentPlacement.Inline)]))
            .ShouldBe("# about the document\na: v\n");

    /// <summary>
    /// A comment is a line comment, so a retained comment carrying a line break cannot be emitted
    /// on one line. Each line is emitted as its own comment, which preserves the text.
    /// </summary>
    [Test]
    public void AMultilineCommentIsEmittedAsSuccessiveCommentLines() =>
        Serialize(new DocumentMapping(
            [new DocumentMember(
                "a",
                new DocumentScalar(
                    ScalarPayload.OfString("v"),
                    [Comment("one\ntwo", CommentPlacement.Inline)]))],
            []))
            .ShouldBe("# one\n# two\na: v\n");

    /// <summary>
    /// Section 16.9's <c>DiscardComments</c> emits no comments, which is the only way to get YAML
    /// output free of them because Section 19.4 preserves them by default.
    /// </summary>
    [Test]
    public void DiscardCommentsEmitsNone() =>
        Serialize(
            YamlOutputOptions.DiscardComments,
            new DocumentMapping(
                [new DocumentMember(
                    "a",
                    new DocumentScalar(ScalarPayload.OfString("v"), [Comment("why", CommentPlacement.Leading)]))],
                []))
            .ShouldBe("a: v\n");

    /// <summary>
    /// Discarding comments is what the user asked for, so unlike Section 19.3's JSON discard it is
    /// not a surprise worth a diagnostic.
    /// </summary>
    [Test]
    public void DiscardingCommentsWarnsNothing()
    {
        Serialize(
            YamlOutputOptions.DiscardComments,
            new DocumentMapping(
                [new DocumentMember(
                    "a",
                    new DocumentScalar(ScalarPayload.OfString("v"), [Comment("why", CommentPlacement.Leading)]))],
                []));

        diagnostics.Drain().ShouldBeEmpty();
    }

    // ---- Section 16.9 option validation ----------------------------------------------------------

    /// <summary>
    /// Section 16.9 makes the two comment strategies a mutually exclusive group, so naming both is
    /// the contradiction <c>SCHEME001</c> reports.
    /// </summary>
    [Test]
    public void PreserveAndDiscardCommentsContradictEachOther()
    {
        (YamlOutputOptions.PreserveComments | YamlOutputOptions.DiscardComments)
            .TryValidate(out var contradiction)
            .ShouldBeFalse();

        contradiction.ShouldNotBeNull().ShouldContain("mutually exclusive");
    }

    /// <summary>
    /// Section 16.9: "When a replacement omits every flag from a mutually exclusive mode group,
    /// that group's documented default is reapplied." Section 19.4 documents preservation as that
    /// default.
    /// </summary>
    [Test]
    public void OmittingBothCommentFlagsReappliesPreservation()
    {
        YamlOutputOptions.None.PreservesComments().ShouldBeTrue();
        YamlOutputOptions.PreserveComments.PreservesComments().ShouldBeTrue();
        YamlOutputOptions.DiscardComments.PreservesComments().ShouldBeFalse();
    }
}
