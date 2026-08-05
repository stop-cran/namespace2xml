using Namespace2Xml.Text;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// The Appendix A.1 <c>document</c> production and the Section 8.1 record bullets.
/// </summary>
[TestFixture]
public sealed class PhysicalRecordReaderTests
{
    private static string[] TextOf(string document) =>
        [.. PhysicalRecordReader.Read(document).Select(r => r.Text)];

    private static int[] LinesOf(string document) =>
        [.. PhysicalRecordReader.Read(document).Select(r => r.Line)];

    // "LF, CRLF, and lone CR terminate a record and are not part of it."

    [Test]
    public void LineFeedTerminatesARecord() =>
        TextOf("a=1\nb=2").ShouldBe(["a=1", "b=2"]);

    [Test]
    public void CarriageReturnLineFeedTerminatesOneRecord() =>
        TextOf("a=1\r\nb=2").ShouldBe(["a=1", "b=2"]);

    [Test]
    public void ALoneCarriageReturnTerminatesARecord() =>
        TextOf("a=1\rb=2").ShouldBe(["a=1", "b=2"]);

    [Test]
    public void ATerminatorIsNotPartOfTheRecord() =>
        TextOf("a\r\n").ShouldBe(["a", string.Empty]);

    [Test]
    public void ALineFeedFollowedByACarriageReturnIsTwoTerminators() =>
        TextOf("a\n\rb").ShouldBe(["a", string.Empty, "b"]);

    [Test]
    public void ACarriageReturnFollowedByTwoLineFeedsIsTwoTerminators() =>
        TextOf("a\r\n\nb").ShouldBe(["a", string.Empty, "b"]);

    // "the final record need not have a terminator"

    [Test]
    public void TheFinalRecordNeedNoTerminator() =>
        TextOf("a=1").ShouldBe(["a=1"]);

    [Test]
    public void ATrailingTerminatorYieldsAFinalEmptyRecord() =>
        TextOf("a=1\n").ShouldBe(["a=1", string.Empty]);

    [Test]
    public void AnEmptyDocumentYieldsOneEmptyRecord() =>
        TextOf(string.Empty).ShouldBe([string.Empty]);

    // "U+0085, U+2028, and U+2029 are ordinary data and never terminate a record."

    [Test]
    public void NextLineDoesNotTerminateARecord() =>
        TextOf("a=x\u0085y").ShouldBe(["a=x\u0085y"]);

    [Test]
    public void TheLineSeparatorDoesNotTerminateARecord() =>
        TextOf("a=x\u2028y").ShouldBe(["a=x\u2028y"]);

    [Test]
    public void TheParagraphSeparatorDoesNotTerminateARecord() =>
        TextOf("a=x\u2029y").ShouldBe(["a=x\u2029y"]);

    [Test]
    public void AVerticalTabAndFormFeedDoNotTerminateARecord() =>
        TextOf("a=x\u000by\u000cz").ShouldBe(["a=x\u000by\u000cz"]);

    // "every other record is not trimmed"

    [Test]
    public void RecordsAreNotTrimmed() =>
        TextOf("  a = 1  \n\tb\t").ShouldBe(["  a = 1  ", "\tb\t"]);

    // Section 22 line numbering.

    [Test]
    public void LinesAreNumberedFromOne() =>
        LinesOf("a\nb\nc").ShouldBe([1, 2, 3]);

    [Test]
    public void ABlankRecordStillOccupiesItsLine() =>
        LinesOf("a\n\nc").ShouldBe([1, 2, 3]);

    [Test]
    public void CarriageReturnLineFeedAdvancesTheLineOnce() =>
        LinesOf("a\r\nb\r\nc").ShouldBe([1, 2, 3]);

    [Test]
    public void ARecordContainingUnicodeSeparatorsDoesNotAdvanceTheLine() =>
        LinesOf("a\u2028b\nc").ShouldBe([1, 2]);
}
