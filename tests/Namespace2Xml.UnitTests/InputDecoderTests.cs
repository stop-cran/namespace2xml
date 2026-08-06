using System.Text;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Text;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Section 7.4 byte-order-mark detection and decoding, and the Section 22 position rule.
/// </summary>
/// <remarks>
/// Authored from the specification text, not from observed output. Section 7.4 is short enough to
/// read in full, and every case below quotes the clause it exercises.
/// </remarks>
[TestFixture]
public sealed class InputDecoderTests
{
    private const string Source = "inputs/main.txt";

    private static DecodedSource Decode(params byte[] bytes) => InputDecoder.Decode(bytes, Source, DiagnosticPhase.Input);

    private static string DecodeText(params byte[] bytes)
    {
        var result = Decode(bytes);
        result.Diagnostic.ShouldBeNull();
        return result.Text!;
    }

    private static Diagnostic DecodeFailure(params byte[] bytes)
    {
        var result = Decode(bytes);
        result.Succeeded.ShouldBeFalse();
        result.Text.ShouldBeNull();
        result.Encoding.ShouldBeNull();
        return result.Diagnostic!.Diagnostic;
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    private static byte[] Utf8Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Utf16LeBytes(string text) => Encoding.Unicode.GetBytes(text);

    private static byte[] Utf16BeBytes(string text) => Encoding.BigEndianUnicode.GetBytes(text);

    // "UTF-8 is the default input encoding." / "Without a recognized BOM, input is strict UTF-8."

    [Test]
    public void InputWithoutAByteOrderMarkIsStrictUtf8()
    {
        var result = Decode(Utf8Bytes("a.b=\u00e9\u4e2d"));

        result.Text.ShouldBe("a.b=\u00e9\u4e2d");
        result.Encoding.ShouldBe(SourceEncoding.Utf8);
    }

    [Test]
    public void EmptyInputDecodesToEmptyText()
    {
        var result = Decode();

        result.Text.ShouldBe(string.Empty);
        result.Encoding.ShouldBe(SourceEncoding.Utf8);
    }

    // "Recognized byte-order marks are: UTF-8 BOM: decode as UTF-8; UTF-16 little-endian BOM:
    //  decode as UTF-16LE; UTF-16 big-endian BOM: decode as UTF-16BE."

    [Test]
    public void AUtf8ByteOrderMarkSelectsUtf8()
    {
        var result = Decode(Concat([0xEF, 0xBB, 0xBF], Utf8Bytes("a=1")));

        result.Text.ShouldBe("a=1");
        result.Encoding.ShouldBe(SourceEncoding.Utf8);
    }

    [Test]
    public void AUtf16LittleEndianByteOrderMarkSelectsUtf16Le()
    {
        var result = Decode(Concat([0xFF, 0xFE], Utf16LeBytes("a=1")));

        result.Text.ShouldBe("a=1");
        result.Encoding.ShouldBe(SourceEncoding.Utf16LittleEndian);
    }

    [Test]
    public void AUtf16BigEndianByteOrderMarkSelectsUtf16Be()
    {
        var result = Decode(Concat([0xFE, 0xFF], Utf16BeBytes("a=1")));

        result.Text.ShouldBe("a=1");
        result.Encoding.ShouldBe(SourceEncoding.Utf16BigEndian);
    }

    [Test]
    public void AFileConsistingOnlyOfAByteOrderMarkDecodesToEmptyText() =>
        DecodeText(0xEF, 0xBB, 0xBF).ShouldBe(string.Empty);

    // "UTF-32 and unrecognized byte-order marks are errors."
    // "BOM detection tests four-byte signatures before three- or two-byte signatures. Consequently
    //  FF FE 00 00 and 00 00 FE FF are recognized as prohibited UTF-32 BOMs rather than as UTF-16
    //  prefixes."

    [Test]
    public void AUtf32LittleEndianByteOrderMarkIsRejected() =>
        DecodeFailure(0xFF, 0xFE, 0x00, 0x00).Code.ShouldBe("PARSE002");

    [Test]
    public void AUtf32BigEndianByteOrderMarkIsRejected() =>
        DecodeFailure(0x00, 0x00, 0xFE, 0xFF).Code.ShouldBe("PARSE002");

    [Test]
    public void TheFourByteTestPrecedesTheTwoByteTest()
    {
        // Valid UTF-16LE for "\0a" if FF FE were taken as a UTF-16LE mark. Section 7.4 says it is
        // not: the four-byte signature matches first, so this is a prohibited UTF-32LE mark.
        DecodeFailure(0xFF, 0xFE, 0x00, 0x00, 0x61, 0x00).Code.ShouldBe("PARSE002");
    }

    [Test]
    public void TheFourByteTestDoesNotSwallowAnOrdinaryUtf16LittleEndianFile() =>
        DecodeText(0xFF, 0xFE, 0x41, 0x00).ShouldBe("A");

    [Test]
    public void ThreeBytesOfAUtf32BigEndianMarkAreNotAMarkAndFailStrictUtf8()
    {
        // 00 00 FE is not the four-byte 00 00 FE FF, so Section 7.4 leaves it as strict UTF-8, in
        // which FE is not a valid lead byte.
        var diagnostic = DecodeFailure(0x00, 0x00, 0xFE);

        diagnostic.Code.ShouldBe("PARSE002");
        diagnostic.Line.ShouldBe(1);
        diagnostic.Column.ShouldBe(3);
    }

    // "A recognized byte-order mark is an encoding signature and not text. Decoding consumes it,
    //  and it is not part of the decoded character stream. U+FEFF anywhere else is ordinary data,
    //  including immediately after a byte-order mark, so a UTF-8 file beginning EF BB BF EF BB BF
    //  decodes to a single U+FEFF character."

    [Test]
    public void TheSpecificationsDoubledUtf8MarkExampleDecodesToOneCharacter() =>
        DecodeText(0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF).ShouldBe("\uFEFF");

    [Test]
    public void ADoubledUtf16LittleEndianMarkDecodesToOneCharacter() =>
        DecodeText(0xFF, 0xFE, 0xFF, 0xFE).ShouldBe("\uFEFF");

    [Test]
    public void ADoubledUtf16BigEndianMarkDecodesToOneCharacter() =>
        DecodeText(0xFE, 0xFF, 0xFE, 0xFF).ShouldBe("\uFEFF");

    [Test]
    public void AZeroWidthNoBreakSpaceInsideTheTextIsOrdinaryData() =>
        DecodeText(Utf8Bytes("a=x\uFEFFy")).ShouldBe("a=x\uFEFFy");

    // "A byte-order mark is an encoding of U+FEFF, and U+FEFF has exactly five encodings. ...
    //  Introducers for other encodings, notably UTF-7's 2B 2F 76 and the UTF-1, UTF-EBCDIC, SCSU,
    //  BOCU-1, and GB18030 signatures, are not byte-order marks and receive no special treatment:
    //  each is either ordinary UTF-8 text or an invalid byte sequence under the rules already
    //  stated."

    [Test]
    public void TheUtf7IntroducerIsOrdinaryTextAndNotAByteOrderMark()
    {
        // 2B 2F 76 38 is "+/v8". A signature table would reject this file; Section 7.4 says there
        // is no such table, so the bytes are exactly what strict UTF-8 makes of them.
        var result = Decode(Utf8Bytes("+/v8.name=1"));

        result.Text.ShouldBe("+/v8.name=1");
        result.Encoding.ShouldBe(SourceEncoding.Utf8);
    }

    [Test]
    public void EveryOtherNamedIntroducerIsRejectedAsAnInvalidByteSequenceRatherThanRecognized()
    {
        byte[][] introducers =
        [
            [0xF7, 0x64, 0x4C],             // UTF-1
            [0xDD, 0x73, 0x66, 0x73],       // UTF-EBCDIC
            [0x0E, 0xFE, 0xFF],             // SCSU
            [0xFB, 0xEE, 0x28],             // BOCU-1
            [0x84, 0x31, 0x95, 0x33],       // GB18030
        ];

        foreach (var introducer in introducers)
        {
            DecodeFailure(introducer).Code.ShouldBe("PARSE002");
        }
    }

    // "Invalid byte sequences are errors."

    [Test]
    public void ATruncatedUtf8SequenceIsRejected() =>
        DecodeFailure(0x41, 0xC3).Code.ShouldBe("PARSE002");

    [Test]
    public void ABareUtf8ContinuationByteIsRejected() =>
        DecodeFailure(0x41, 0x80, 0x42).Code.ShouldBe("PARSE002");

    [Test]
    public void AnOverlongUtf8EncodingIsRejected() =>
        DecodeFailure(0xC0, 0xAF).Code.ShouldBe("PARSE002");

    [Test]
    public void ASurrogateEncodedInUtf8IsRejected() =>
        DecodeFailure(0xED, 0xA0, 0x80).Code.ShouldBe("PARSE002");

    [Test]
    public void AUtf8SequenceAboveTheUnicodeRangeIsRejected() =>
        DecodeFailure(0xF5, 0x80, 0x80, 0x80).Code.ShouldBe("PARSE002");

    [Test]
    public void AnOddByteCountAfterAUtf16MarkIsRejected() =>
        DecodeFailure(0xFF, 0xFE, 0x41, 0x00, 0x42).Code.ShouldBe("PARSE002");

    [Test]
    public void AnUnpairedHighSurrogateInUtf16IsRejected() =>
        DecodeFailure(0xFF, 0xFE, 0x00, 0xD8, 0x41, 0x00).Code.ShouldBe("PARSE002");

    [Test]
    public void AnUnpairedLowSurrogateInUtf16IsRejected() =>
        DecodeFailure(0xFF, 0xFE, 0x00, 0xDC, 0x41, 0x00).Code.ShouldBe("PARSE002");

    [Test]
    public void AValidSurrogatePairInUtf16IsAccepted() =>
        DecodeText(0xFF, 0xFE, 0x3D, 0xD8, 0x00, 0xDE).ShouldBe("\uD83D\uDE00");

    // "When decoding fails, the reported position is the first character position the byte stream
    //  could not produce. The longest prefix of the byte stream that decodes successfully
    //  determines it, and line and column locate the position immediately after that prefix."

    [Test]
    public void ThePositionIsTheStartOfTheUndecodableRegionNotWhereADecoderNoticesIt()
    {
        // "A" then a lone high surrogate then "A". A decoder emits "A", buffers the surrogate, and
        // only rejects the input on the following unit, so its own error index points past the
        // surrogate. The longest decodable prefix is "A", so the position is column 2.
        var diagnostic = DecodeFailure(0xFF, 0xFE, 0x41, 0x00, 0x00, 0xD8, 0x41, 0x00);

        diagnostic.Line.ShouldBe(1);
        diagnostic.Column.ShouldBe(2);
    }

    [Test]
    public void AnIncompleteSequenceAtEndOfInputIsReportedWhereItBegins()
    {
        var diagnostic = DecodeFailure(0x41, 0x42, 0xC3);

        diagnostic.Line.ShouldBe(1);
        diagnostic.Column.ShouldBe(3);
    }

    [Test]
    public void AProhibitedByteOrderMarkIsReportedAtLineOneColumnOne()
    {
        var diagnostic = DecodeFailure(0xFF, 0xFE, 0x00, 0x00);

        diagnostic.Line.ShouldBe(1);
        diagnostic.Column.ShouldBe(1);
    }

    // Section 22: "Line 1 is the first line. A line is terminated by LF, CRLF, or a lone CR, and by
    // nothing else ... Column 1 is the first Unicode scalar value of a line, and each subsequent
    // scalar advances the column by one."

    [Test]
    public void LineFeedsAdvanceTheLine()
    {
        var diagnostic = DecodeFailure(Concat(Utf8Bytes("a=1\nb=2\nc"), [0x80]));

        diagnostic.Line.ShouldBe(3);
        diagnostic.Column.ShouldBe(2);
    }

    [Test]
    public void ACarriageReturnLineFeedPairEndsExactlyOneLine()
    {
        var diagnostic = DecodeFailure(Concat(Utf8Bytes("a=1\r\nb"), [0x80]));

        diagnostic.Line.ShouldBe(2);
        diagnostic.Column.ShouldBe(2);
    }

    [Test]
    public void ALoneCarriageReturnEndsALine()
    {
        var diagnostic = DecodeFailure(Concat(Utf8Bytes("a=1\rb"), [0x80]));

        diagnostic.Line.ShouldBe(2);
        diagnostic.Column.ShouldBe(2);
    }

    [Test]
    public void ALineFeedFollowedByACarriageReturnEndsTwoLines()
    {
        var diagnostic = DecodeFailure(Concat(Utf8Bytes("a\n\rb"), [0x80]));

        diagnostic.Line.ShouldBe(3);
        diagnostic.Column.ShouldBe(2);
    }

    [Test]
    public void NextLineAndTheSeparatorsDoNotTerminateALine()
    {
        // U+0085, U+2028, U+2029 are one scalar each and stay on line 1.
        var diagnostic = DecodeFailure(Concat(Utf8Bytes("a\u0085b\u2028c\u2029d"), [0x80]));

        diagnostic.Line.ShouldBe(1);
        diagnostic.Column.ShouldBe(8);
    }

    [Test]
    public void ACharacterOutsideTheBasicMultilingualPlaneOccupiesOneColumn()
    {
        var diagnostic = DecodeFailure(Concat(Utf8Bytes("a\U0001F600b"), [0x80]));

        diagnostic.Line.ShouldBe(1);
        diagnostic.Column.ShouldBe(4);
    }

    [Test]
    public void ATabOccupiesOneColumn()
    {
        var diagnostic = DecodeFailure(Concat(Utf8Bytes("a\tb"), [0x80]));

        diagnostic.Line.ShouldBe(1);
        diagnostic.Column.ShouldBe(4);
    }

    [Test]
    public void PositionsAreMeasuredAfterTheByteOrderMarkIsRemoved()
    {
        // The mark is not text, so it occupies no column: the failure is at column 2, not 3.
        var diagnostic = DecodeFailure(Concat([0xEF, 0xBB, 0xBF], Utf8Bytes("a"), [0x80]));

        diagnostic.Line.ShouldBe(1);
        diagnostic.Column.ShouldBe(2);
    }

    // Diagnostic shape: Section 22 registry and Section 6.4.3.

    [Test]
    public void TheDiagnosticCarriesTheRegistryShapeAndTheSectionSevenPointFourAnchor()
    {
        var occurrence = Decode(0x80).Diagnostic!;

        occurrence.Diagnostic.Code.ShouldBe("PARSE002");
        occurrence.Diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
        occurrence.Diagnostic.Phase.ShouldBe(DiagnosticPhase.Input);
        occurrence.Diagnostic.Spec.ShouldBe("\u00A77.4");
        occurrence.Diagnostic.Source.ShouldBe(Source);
    }

    [Test]
    public void TheCardinalityKeyIsTheFailingSourceBecauseTheRegistrySaysOncePerFailingSource()
    {
        // Two distinct sources must not collapse into one occurrence.
        InputDecoder.Decode([0x80], "inputs/a.txt", DiagnosticPhase.Input).Diagnostic!.CardinalityKey.ShouldBe("inputs/a.txt");
        InputDecoder.Decode([0x80], "inputs/b.txt", DiagnosticPhase.Input).Diagnostic!.CardinalityKey.ShouldBe("inputs/b.txt");
    }

    [Test]
    public void DecodingIsTotalOverArbitraryBytes()
    {
        // A source is attacker-controlled input. Every byte pattern must produce a result rather
        // than an exception, or a malformed file becomes a crash instead of a PARSE002.
        var random = new Random(20260805);
        var buffer = new byte[64];

        for (var i = 0; i < 2000; i++)
        {
            random.NextBytes(buffer);
            var length = random.Next(buffer.Length + 1);
            var result = InputDecoder.Decode(buffer.AsSpan(0, length), Source, DiagnosticPhase.Input);

            (result.Text is not null ^ result.Diagnostic is not null).ShouldBeTrue();
        }
    }
}
