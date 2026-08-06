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
/// Section 19.1 namespace and Section 19.2 quoted-namespace serialization, with the Section 20
/// comment rule and the Section 24 termination rule.
/// </summary>
/// <remarks>
/// Every expectation here is authored from the specification clause named in the test, never from
/// what the serializer currently produces.
/// </remarks>
[TestFixture]
public class FlatTextSerializerTests
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

    private string Serialize(FlatFormat format, params FlatEntry[] entries)
    {
        var writer = new OutputBufferWriter(new GlobalBudget(ResourceLimits.Defaults));

        Write(format, writer, entries).ShouldBeTrue();

        return Encoding.UTF8.GetString(writer.Build().ToArray());
    }

    private bool Write(FlatFormat format, OutputBufferWriter writer, params FlatEntry[] entries)
    {
        var delimiter = FlatKeyProjector.DefaultDelimiter(format);
        var keyed = new FlatKeyProjector(format, delimiter, diagnostics, "out.txt").Project(entries);

        keyed.Length.ShouldBe(entries.Length);

        return new FlatTextSerializer(format, delimiter, diagnostics, "out.txt")
            .TrySerialize(keyed, writer);
    }

    private bool Fails(FlatFormat format, params FlatEntry[] entries) =>
        !Write(format, new OutputBufferWriter(new GlobalBudget(ResourceLimits.Defaults)), entries);

    private string SoleCode() => diagnostics.Drain().ShouldHaveSingleItem().Code;

    // ---- Section 19.1 namespace ------------------------------------------------------------------

    /// <summary>
    /// Section 19.1 writes one <c>name=value</c> line per surviving scalar, at the delimiter
    /// Section 16.4 configures.
    /// </summary>
    [Test]
    public void NamespaceWritesOneAssignmentPerEntry()
    {
        Serialize(FlatFormat.Namespace, Entry("a.b", "1"), Entry("a.c", "2"))
            .ShouldBe("a.b=1\na.c=2\n");

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 19.1 escapes a value through the inverse of the value lexer, so a literal <c>${</c>
    /// is written back as <c>\${</c> and never re-read as a reference.
    /// </summary>
    [Test]
    public void NamespaceEscapesAValueThatWouldOtherwiseLexAsSyntax()
    {
        Serialize(FlatFormat.Namespace, Entry("a", "${x} * \\ done"))
            .ShouldBe("a=\\${x} \\* \\\\ done\n");
    }

    /// <summary>
    /// Section 19.1 escapes LF, CR, and tab in a value, because Section 8.1 ends a namespace record
    /// at a line break and an unescaped one would split a single value across two records.
    /// </summary>
    [Test]
    public void NamespaceEscapesControlCharactersThatWouldEndTheRecord()
    {
        Serialize(FlatFormat.Namespace, Entry("a", "one\ntwo\tthree\rfour"))
            .ShouldBe("a=one\\ntwo\\tthree\\rfour\n");
    }

    /// <summary>
    /// Section 19.1 spells typed values as canonical locale-independent text, which is what makes
    /// the same overlay render identically under every host culture.
    /// </summary>
    [TestCase(true, "true")]
    [TestCase(false, "false")]
    public void NamespaceSpellsBooleansCanonically(bool value, string expected) =>
        Serialize(FlatFormat.Namespace, Entry("a", ScalarPayload.OfBoolean(value)))
            .ShouldBe($"a={expected}\n");

    /// <summary>Section 19.1: a null payload is spelled <c>null</c>.</summary>
    [Test]
    public void NamespaceSpellsNullAsTheWordNull() =>
        Serialize(FlatFormat.Namespace, Entry("a", ScalarPayload.Null))
            .ShouldBe("a=null\n");

    /// <summary>
    /// Section 19.1: an empty value is written as an empty assignment, which Section 8.1 reads back
    /// as the empty string rather than as a missing value.
    /// </summary>
    [Test]
    public void NamespaceWritesAnEmptyValueAsABareAssignment() =>
        Serialize(FlatFormat.Namespace, Entry("a", string.Empty)).ShouldBe("a=\n");

    /// <summary>
    /// Section 19.1 rejects an unpaired surrogate, because it has no UTF-8 encoding at all and the
    /// Section 24 promise of UTF-8 output cannot be kept for it.
    /// </summary>
    [Test]
    public void NamespaceRejectsAnUnpairedSurrogate()
    {
        Fails(FlatFormat.Namespace, Entry("a", "x\ud800y")).ShouldBeTrue();

        SoleCode().ShouldBe("SERIALIZE001");
    }

    // ---- Section 19.2 quoted namespace -----------------------------------------------------------

    /// <summary>
    /// Section 19.2 is "POSIX shell assignment output without <c>export</c>", so the key is spelled
    /// at the shell-safe delimiter and the value is single-quoted.
    /// </summary>
    [Test]
    public void QuotedNamespaceWritesShellAssignments()
    {
        Serialize(FlatFormat.QuotedNamespace, Entry("a.b", "1"))
            .ShouldBe("a_b='1'\n");

        diagnostics.Drain().ShouldBeEmpty();
    }

    /// <summary>
    /// Section 19.2 preserves "spaces, <c>$</c>, backticks, double quotes, backslashes, exclamation
    /// marks, and line breaks without expansion": inside a single-quoted word none of them are
    /// special, so none of them are escaped.
    /// </summary>
    [Test]
    public void QuotedNamespacePreservesShellMetacharactersLiterally() =>
        Serialize(FlatFormat.QuotedNamespace, Entry("a", "$x `y` \"z\" \\ ! and\na break"))
            .ShouldBe("a='$x `y` \"z\" \\ ! and\na break'\n");

    /// <summary>
    /// A single quote cannot appear inside a single-quoted word, so Section 19.2's escaping closes
    /// the word, emits an escaped quote, and reopens it. The shell concatenates the adjacent words,
    /// so the assignment still receives exactly one value.
    /// </summary>
    [Test]
    public void QuotedNamespaceClosesAndReopensTheWordAroundASingleQuote() =>
        Serialize(FlatFormat.QuotedNamespace, Entry("a", "can't"))
            .ShouldBe("a='can'\\''t'\n");

    /// <summary>
    /// Section 19.2 makes NUL an error rather than dropping it: a shell word cannot carry NUL, and
    /// silently truncating the value there would publish a different value than the model holds.
    /// </summary>
    [Test]
    public void QuotedNamespaceRejectsNul()
    {
        Fails(FlatFormat.QuotedNamespace, Entry("a", "x\0y")).ShouldBeTrue();

        SoleCode().ShouldBe("SERIALIZE001");
    }

    /// <summary>
    /// Section 19.2 adopts Section 19.1's spelling of null. An empty assignment would instead make
    /// null and the empty string indistinguishable to the shell consumer.
    /// </summary>
    [Test]
    public void QuotedNamespaceSpellsNullDistinguishablyFromTheEmptyString()
    {
        Serialize(FlatFormat.QuotedNamespace, Entry("a", ScalarPayload.Null))
            .ShouldBe("a='null'\n");

        Serialize(FlatFormat.QuotedNamespace, Entry("a", string.Empty))
            .ShouldBe("a=''\n");
    }

    // ---- Section 20 comments ---------------------------------------------------------------------

    /// <summary>
    /// Section 20: both flat text formats "emit normalized leading <c>#</c> comments", immediately
    /// before the entry they are bound to.
    /// </summary>
    [TestCase(FlatFormat.Namespace, "a=1\n")]
    [TestCase(FlatFormat.QuotedNamespace, "a='1'\n")]
    public void CommentsPrecedeTheirEntry(FlatFormat format, string assignment) =>
        Serialize(format, Entry("a", "1", "first", "second"))
            .ShouldBe($"# first\n# second\n{assignment}");

    /// <summary>
    /// Section 20 prefixes "every physical line ... independently", which is what stops a multiline
    /// comment from introducing an executable shell assignment or a live namespace entry.
    /// </summary>
    [Test]
    public void EveryPhysicalLineOfAMultilineCommentIsPrefixed() =>
        Serialize(FlatFormat.QuotedNamespace, Entry("a", "1", "note\nEVIL='rm -rf /'"))
            .ShouldBe("# note\n# EVIL='rm -rf /'\na='1'\n");

    /// <summary>
    /// Section 20 normalizes comment text to LF first, so a CRLF in a comment does not put a CR into
    /// output that Section 24 says uses LF terminators.
    /// </summary>
    [Test]
    public void ACommentIsNormalizedToLfBeforeItIsPrefixed() =>
        Serialize(FlatFormat.Namespace, Entry("a", "1", "one\r\ntwo"))
            .ShouldBe("# one\n# two\na=1\n");

    /// <summary>
    /// Section 20 rejects NUL in comment text. Nothing in the namespace comment syntax escapes it,
    /// so writing it would produce a file the reader cannot round-trip.
    /// </summary>
    [Test]
    public void ACommentContainingNulIsAnError()
    {
        Fails(FlatFormat.Namespace, Entry("a", "1", "x\0y")).ShouldBeTrue();

        SoleCode().ShouldBe("SERIALIZE001");
    }

    // ---- Section 24 determinism ------------------------------------------------------------------

    /// <summary>
    /// Section 24: every text output "ends with exactly one LF", and a zero-byte output is valid, so
    /// no lone terminator is emitted to terminate nothing.
    /// </summary>
    [Test]
    public void AnEmptyViewProducesZeroBytes()
    {
        var writer = new OutputBufferWriter(new GlobalBudget(ResourceLimits.Defaults));

        new FlatTextSerializer(FlatFormat.Namespace, ".", diagnostics)
            .TrySerialize([], writer)
            .ShouldBeTrue();

        writer.Build().Length.ShouldBe(0);
    }

    /// <summary>Section 24's termination rule holds for the buffers this serializer builds.</summary>
    [Test]
    public void TheBuiltBufferIsWellTerminatedText()
    {
        var writer = new OutputBufferWriter(new GlobalBudget(ResourceLimits.Defaults));

        Write(FlatFormat.Namespace, writer, Entry("a", "1")).ShouldBeTrue();

        writer.TryBuildText(out _, out var violation).ShouldBeTrue();
        violation.ShouldBeNull();
    }

    /// <summary>
    /// A crossing of <c>--max-total-output-bytes</c> stops serialization without a diagnostic of its
    /// own: Section 23 reports the crossing once, from the budget, rather than once per writer.
    /// </summary>
    [Test]
    public void ABudgetCrossingStopsSerializationAndIsLeftToTheBudgetToReport()
    {
        var writer = new OutputBufferWriter(
            new GlobalBudget(ResourceLimits.Defaults with { MaxTotalOutputBytes = 4 }));

        Write(FlatFormat.Namespace, writer, Entry("a", "1"), Entry("b", "2")).ShouldBeFalse();

        writer.Fault.ShouldNotBeNull();
        diagnostics.Drain().ShouldBeEmpty();
    }

    // ---- Construction ----------------------------------------------------------------------------

    /// <summary>
    /// INI is not a flat text format in this sense: Section 19.6 gives it sections, a hoisted global
    /// preamble, and its own value rules, so routing it here would silently lose all three.
    /// </summary>
    [Test]
    public void TheIniFormatIsRefused() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlatTextSerializer(FlatFormat.Ini, ":", diagnostics));
}
