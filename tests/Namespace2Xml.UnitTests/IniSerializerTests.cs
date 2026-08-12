using System.Collections.Immutable;
using System.Text;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Output;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 19.6 <c>PortableIni1</c> serialization, its Section 16.9 options, and the Section 20 INI
/// comment rule.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the serializer currently produces.
/// </remarks>
[TestFixture]
public class IniSerializerTests
{
    private DiagnosticBuffer diagnostics = null!;

    [SetUp]
    public void SetUp() => diagnostics = new DiagnosticBuffer();

    private static OrdinaryPart Ordinary(string text) => new([new LiteralToken(text)]);

    private static FlatEntry Entry(string path, ScalarPayload payload, params string[] comments)
    {
        ImmutableArray<NamePart> parts =
            [.. path.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(Ordinary)];

        return new FlatEntry(
            parts,
            parts,
            payload,
            [.. comments.Select(text =>
                new BoundComment(text, CommentPlacement.Leading, StableOrderingKey.First))]);
    }

    private static FlatEntry Entry(string path, string value, params string[] comments) =>
        Entry(path, ScalarPayload.Untyped(value), comments);

    private string Serialize(IniOutputOptions options, params FlatEntry[] entries)
    {
        var writer = new OutputBufferWriter(new GlobalBudget(ResourceLimits.Defaults));

        Write(options, writer, entries).ShouldBeTrue();

        return Encoding.UTF8.GetString(writer.Build().ToArray());
    }

    private bool Write(
        IniOutputOptions options, OutputBufferWriter writer, params FlatEntry[] entries)
    {
        var keyed = new FlatKeyProjector(
            FlatFormat.Ini,
            FlatKeyProjector.DefaultDelimiter(FlatFormat.Ini),
            diagnostics,
            new DestinationRef("out.ini", 0)).Project(entries);

        keyed.Length.ShouldBe(entries.Length);

        return new IniSerializer(options, diagnostics, new DestinationRef("out.ini", 0))
            .TrySerialize(new FlatKeyedDocument([], keyed, []), writer);
    }

    private bool Fails(IniOutputOptions options, params FlatEntry[] entries) =>
        !Write(options, new OutputBufferWriter(new GlobalBudget(ResourceLimits.Defaults)), entries);

    private string SoleCode() => diagnostics.Drain().ShouldHaveSingleItem().Code;

    // ---- Section 19.6 layout ---------------------------------------------------------------------

    /// <summary>
    /// Section 19.6: "all global keys are emitted in one preamble before the first section", and
    /// hoisting them "does not change value precedence" — it is a layout projection only.
    /// </summary>
    [Test]
    public void GlobalKeysAreHoistedIntoOnePreambleAheadOfEverySection() =>
        Serialize(
            IniOutput.Default,
            Entry("a", "1"),
            Entry("s.x", "2"),
            Entry("b", "3"))
            .ShouldBe("a=1\nb=3\n[s]\nx=2\n");

    /// <summary>
    /// Section 19.6 preserves the winning source order of global keys, so the preamble is a
    /// reordering of nothing: the entries keep the order Section 19.1 emitted them in.
    /// </summary>
    [Test]
    public void GlobalKeysKeepTheirWinningSourceOrder() =>
        Serialize(IniOutput.Default, Entry("b", "1"), Entry("a", "2"))
            .ShouldBe("b=1\na=2\n");

    /// <summary>
    /// Section 19.6 orders sections by their Section 5.2 mapping order, which the Section 19.1
    /// emission order presents as first appearance — not by name. Keys of one section are gathered
    /// under a single header even when another section intervenes.
    /// </summary>
    [Test]
    public void SectionsFollowFirstAppearanceAndTheirKeysAreGathered() =>
        Serialize(
            IniOutput.Default,
            Entry("t.y", "1"),
            Entry("s.x", "2"),
            Entry("t.z", "3"))
            .ShouldBe("[t]\ny=1\nz=3\n[s]\nx=2\n");

    /// <summary>
    /// Section 19.6 joins every part before the last with the configured delimiter to form the
    /// section name, so nesting is spelled in the header rather than implied by more headers.
    /// </summary>
    [Test]
    public void NestedSectionsAreJoinedWithTheDelimiter() =>
        Serialize(IniOutput.Default, Entry("a.b.c", "1")).ShouldBe("[a:b]\nc=1\n");

    /// <summary>
    /// Section 19.6 allows one overlay path to emit both a key and a section "when their projected
    /// identities are distinct", and emits "no shape warning ... merely because one logical path
    /// supplies both projections".
    /// </summary>
    [Test]
    public void AScalarAndItsDescendantEmitAKeyAndASectionWithoutWarning()
    {
        Serialize(IniOutput.Default, Entry("a.x", "1"), Entry("a.x.z", "2"))
            .ShouldBe("[a]\nx=1\n[a:x]\nz=2\n");

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>Section 19.6: "no spaces around <c>=</c> for compatibility".</summary>
    [Test]
    public void NoSpacesSurroundTheEqualsSign() =>
        Serialize(IniOutput.Default, Entry("a", "1")).ShouldNotContain(" = ");

    /// <summary>
    /// Section 24: a zero-byte output is valid, so an empty view emits no header, no key, and no
    /// lone terminator.
    /// </summary>
    [Test]
    public void AnEmptyViewProducesZeroBytes()
    {
        var writer = new OutputBufferWriter(new GlobalBudget(ResourceLimits.Defaults));

        new IniSerializer(IniOutput.Default, diagnostics)
            .TrySerialize(new FlatKeyedDocument([], [], []), writer).ShouldBeTrue();

        writer.Build().Length.ShouldBe(0);
    }

    // ---- Section 19.6 values ---------------------------------------------------------------------

    /// <summary>Section 19.6: default values are unquoted single-line UTF-8 text.</summary>
    [Test]
    public void ADefaultValueIsUnquoted() =>
        Serialize(IniOutput.Default, Entry("a", "plain text")).ShouldBe("a=plain text\n");

    /// <summary>
    /// Section 19.6 spells a null payload <c>null</c>, because <c>PortableIni1</c> has no null
    /// literal and an empty value is a legal empty string, which is a different payload.
    /// </summary>
    [Test]
    public void NullAndTheEmptyStringAreSpelledDistinguishably()
    {
        Serialize(IniOutput.Default, Entry("a", ScalarPayload.Null)).ShouldBe("a=null\n");

        Serialize(IniOutput.Default, Entry("a", string.Empty)).ShouldBe("a=\n");
    }

    /// <summary>
    /// Section 19.6 makes NUL, CR, and LF errors under <c>RejectMultiline</c>: the format is one key
    /// per line, so an embedded line break would read back as a second key.
    /// </summary>
    [TestCase("x\ny")]
    [TestCase("x\ry")]
    public void ALineBreakIsRejectedByDefault(string value)
    {
        Fails(IniOutput.Default, Entry("a", value)).ShouldBeTrue();

        SoleCode().ShouldBe("INI001");
    }

    /// <summary>
    /// NUL is rejected under every option, because Section 19.6 defines no escape for it and
    /// <c>EscapeMultiline</c> covers only LF, CR, and tab.
    /// </summary>
    [TestCase(IniOutputOptions.RejectMultiline)]
    [TestCase(IniOutputOptions.EscapeMultiline)]
    [TestCase(IniOutputOptions.EscapeMultiline | IniOutputOptions.QuoteValues)]
    public void NulIsRejectedUnderEveryOption(IniOutputOptions options)
    {
        Fails(options, Entry("a", "x\0y")).ShouldBeTrue();

        SoleCode().ShouldBe("INI001");
    }

    /// <summary>
    /// Section 19.6: "a value beginning with <c>;</c> or <c>#</c> ... is an error unless
    /// <c>QuoteValues</c> is selected", because an unquoted one reads back as a comment.
    /// </summary>
    [TestCase(";not a comment")]
    [TestCase("#not a comment")]
    public void AValueThatWouldReadBackAsACommentNeedsQuoting(string value)
    {
        Fails(IniOutput.Default, Entry("a", value)).ShouldBeTrue();
        SoleCode().ShouldBe("INI001");

        Serialize(IniOutputOptions.QuoteValues, Entry("a", value)).ShouldBe($"a=\"{value}\"\n");
    }

    /// <summary>
    /// Section 19.6 rejects leading or trailing whitespace unquoted, because a reader that trims
    /// padding cannot tell it from content.
    /// </summary>
    [TestCase(" x")]
    [TestCase("x ")]
    [TestCase("\tx")]
    public void PaddingWhitespaceNeedsQuoting(string value)
    {
        Fails(IniOutput.Default, Entry("a", value)).ShouldBeTrue();
        SoleCode().ShouldBe("INI001");

        Serialize(IniOutputOptions.QuoteValues, Entry("a", value)).ShouldBe($"a=\"{value}\"\n");
    }

    /// <summary>
    /// Interior whitespace is not padding and needs no option: Section 19.6 restricts only the
    /// leading and trailing positions.
    /// </summary>
    [Test]
    public void InteriorWhitespaceIsWrittenLiterally() =>
        Serialize(IniOutput.Default, Entry("a", "x \t y")).ShouldBe("a=x \t y\n");

    /// <summary>
    /// Section 19.6: <c>QuoteValues</c> "emits double-quoted values, escaping <c>\</c> as
    /// <c>\\</c> and <c>"</c> as <c>\"</c>".
    /// </summary>
    [Test]
    public void QuoteValuesEscapesBackslashAndDoubleQuote() =>
        Serialize(IniOutputOptions.QuoteValues, Entry("a", "c:\\p \"q\""))
            .ShouldBe("a=\"c:\\\\p \\\"q\\\"\"\n");

    /// <summary>
    /// Section 19.6: <c>EscapeMultiline</c> emits LF, CR, and tab as escapes, and does so without
    /// requiring quotes — an unquoted value carrying <c>\n</c> is still one line.
    /// </summary>
    [Test]
    public void EscapeMultilineWritesEscapesWithoutQuoting() =>
        Serialize(IniOutputOptions.EscapeMultiline, Entry("a", "x\ny\rz\tw"))
            .ShouldBe("a=x\\ny\\rz\\tw\n");

    /// <summary>
    /// A double quote is escaped only by <c>QuoteValues</c>, which is what makes it special.
    /// Escaping it in an unquoted value would put a backslash into data that never had one.
    /// </summary>
    [Test]
    public void EscapeMultilineAloneLeavesADoubleQuoteAlone() =>
        Serialize(IniOutputOptions.EscapeMultiline, Entry("a", "say \"hi\""))
            .ShouldBe("a=say \"hi\"\n");

    /// <summary>
    /// Section 19.6: under <c>EscapeMultiline</c> "a literal backslash is always emitted as
    /// <c>\\</c> ... whether or not <c>QuoteValues</c> is also selected". Without that the reader
    /// could not tell a literal <c>\n</c> in the data from an escaped line break.
    /// </summary>
    [Test]
    public void EscapeMultilineDoublesABackslashEvenUnquoted() =>
        Serialize(IniOutputOptions.EscapeMultiline, Entry("a", "x\\ny"))
            .ShouldBe("a=x\\\\ny\n");

    /// <summary>
    /// Both options together double a backslash exactly once. Section 19.6 states the rule in the
    /// <c>EscapeMultiline</c> bullet precisely so that selecting both does not double it twice and
    /// change the value.
    /// </summary>
    [Test]
    public void BothOptionsTogetherDoubleABackslashOnlyOnce() =>
        Serialize(
            IniOutputOptions.EscapeMultiline | IniOutputOptions.QuoteValues,
            Entry("a", "x\\y"))
            .ShouldBe("a=\"x\\\\y\"\n");

    // ---- Section 20 comments ---------------------------------------------------------------------

    /// <summary>
    /// Section 20: INI emits comments "only when enabled by <c>inioutputoptions</c>", using the
    /// marker the selected option names.
    /// </summary>
    [TestCase(IniOutputOptions.SemicolonComments, ';')]
    [TestCase(IniOutputOptions.HashComments, '#')]
    public void TheSelectedOptionChoosesTheCommentMarker(IniOutputOptions options, char marker)
    {
        Serialize(options, Entry("a", "1", "note")).ShouldBe($"{marker} note\na=1\n");

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 20: "a comment attached to the first direct key in a section is emitted after the
    /// section header and before that key" — a comment above the header would read as belonging to
    /// the preceding section.
    /// </summary>
    [Test]
    public void ACommentOnTheFirstKeyOfASectionFollowsTheHeader() =>
        Serialize(IniOutputOptions.HashComments, Entry("s.x", "1", "note"))
            .ShouldBe("[s]\n# note\nx=1\n");

    /// <summary>
    /// Section 20 emits INI comments "only as full-line leading comments", so each physical line of
    /// a multiline comment gets its own marker rather than trailing off the end of one.
    /// </summary>
    [Test]
    public void EveryPhysicalLineOfAMultilineCommentIsAFullLineComment() =>
        Serialize(IniOutputOptions.HashComments, Entry("a", "1", "one\r\ntwo"))
            .ShouldBe("# one\n# two\na=1\n");

    /// <summary>
    /// Section 20 discards INI comments "with summarized warning" when neither comment option is
    /// selected. The warning is summarized — once per output file, not once per lost comment.
    /// </summary>
    [Test]
    public void DiscardedCommentsWarnOncePerOutputFile()
    {
        Serialize(IniOutput.Default, Entry("a", "1", "one"), Entry("b", "2", "two"))
            .ShouldBe("a=1\nb=2\n");

        SoleCode().ShouldBe("WARN003");
    }

    /// <summary>
    /// The warning reports a loss, so it is not emitted when there was nothing to lose. A warning on
    /// every comment-free INI output would train its reader to ignore it.
    /// </summary>
    [Test]
    public void NoWarningIsEmittedWhenThereAreNoComments()
    {
        Serialize(IniOutput.Default, Entry("a", "1")).ShouldBe("a=1\n");

        diagnostics.Drain().ShouldBeEmpty();
    }

    // ---- Section 16.9 options --------------------------------------------------------------------

    /// <summary>
    /// Section 16.9 default: <c>RejectMultiline</c>, with comments discarded. The default is the
    /// narrowest dialect, because Section 19.6 requires consumers to opt into anything wider.
    /// </summary>
    [Test]
    public void TheDefaultIsRejectMultilineWithoutComments()
    {
        IniOutput.Default.ShouldBe(IniOutputOptions.RejectMultiline);
        IniOutput.Default.CommentMarker().ShouldBeNull();
        IniOutput.Default.EscapesMultiline().ShouldBeFalse();
    }

    /// <summary>
    /// Section 19.6 rejects multiline values "by default unless an explicit strategy is selected",
    /// so naming no strategy at all still rejects.
    /// </summary>
    [Test]
    public void NamingNoStrategyStillRejects()
    {
        IniOutputOptions.QuoteValues.EscapesMultiline().ShouldBeFalse();

        Fails(IniOutputOptions.QuoteValues, Entry("a", "x\ny")).ShouldBeTrue();
        SoleCode().ShouldBe("INI001");
    }

    /// <summary>
    /// Section 16.9 makes the two comment options and the two multiline strategies mutually
    /// exclusive; a scheme naming both is <c>SCHEME001</c> rather than a silent winner.
    /// </summary>
    [TestCase(IniOutputOptions.SemicolonComments | IniOutputOptions.HashComments)]
    [TestCase(IniOutputOptions.RejectMultiline | IniOutputOptions.EscapeMultiline)]
    public void ContradictoryOptionsAreRejected(IniOutputOptions options)
    {
        options.TryValidate(out var contradiction).ShouldBeFalse();
        contradiction.ShouldNotBeNullOrEmpty();
    }

    /// <summary>Combinations Section 16.9 does not forbid are legal.</summary>
    [TestCase(IniOutput.Default)]
    [TestCase(IniOutputOptions.HashComments | IniOutputOptions.EscapeMultiline
        | IniOutputOptions.QuoteValues)]
    public void CompatibleOptionsAreAccepted(IniOutputOptions options)
    {
        options.TryValidate(out var contradiction).ShouldBeTrue();
        contradiction.ShouldBeNull();
    }
    /// <summary>
    /// Section 19.6: "sections follow their mapping order under Section 5.2, which for a section is
    /// the position of its first key in the emission order of Section 19.1", and Section 19.6
    /// states the consequence that a nested section precedes its parent when the parent's own keys
    /// come later.
    /// </summary>
    [Test]
    public void ASectionTakesThePositionOfItsFirstKey()
    {
        var ini = Serialize(
            IniOutputOptions.None,
            Entry("a.p.x", "1"),
            Entry("a.q", "2"),
            Entry("b.y", "3"));

        ini.ShouldBe("[a:p]\nx=1\n[a]\nq=2\n[b]\ny=3\n");
    }

    /// <summary>
    /// Section 19.6: "Within each block the entries keep the Section 19.1 order they arrived in",
    /// so a section interrupted by another section is written once with its keys in arrival order.
    /// </summary>
    [Test]
    public void ASectionIsWrittenOnceWithEveryKeyItOwns()
    {
        var ini = Serialize(
            IniOutputOptions.None,
            Entry("a.x", "1"),
            Entry("b.y", "2"),
            Entry("a.z", "3"));

        ini.ShouldBe("[a]\nx=1\nz=3\n[b]\ny=2\n");
    }
}
