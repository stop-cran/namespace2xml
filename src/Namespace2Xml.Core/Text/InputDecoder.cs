using System.Text;
using Namespace2Xml.Diagnostics;

namespace Namespace2Xml.Text;

/// <summary>
/// Byte-order-mark detection and decoding under specification Section 7.4.
/// </summary>
/// <remarks>
/// <para>
/// Section 7.4 names five byte-order marks, which is all of them: a byte-order mark is an encoding
/// of U+FEFF and U+FEFF has exactly five encodings. Three are recognized and two are prohibited, so
/// the "unrecognized byte-order mark" case has no members and this type carries no signature table
/// beyond those five. That is a deliberate omission rather than one to be filled in later. A table
/// that also matched UTF-7's <c>2B 2F 76</c> introducer would reject a namespace-profile file whose
/// first record happens to begin <c>+/v8</c>, which is ordinary text under strict UTF-8.
/// </para>
/// <para>
/// Every decoder here throws on invalid input rather than substituting U+FFFD. A replacement
/// character is indistinguishable from one the source actually contained, so a substituting decoder
/// turns a rejected source into a silently corrupted one.
/// </para>
/// </remarks>
public static class InputDecoder
{
    private const string Anchor = "\u00A77.4";

    /// <summary>
    /// Decodes one source, selecting the encoding from its byte-order mark under Section 7.4.
    /// </summary>
    /// <param name="bytes">The complete byte content of the source.</param>
    /// <param name="source">
    /// The source path reported on failure, already in the Section 6.4.3 relative form.
    /// </param>
    /// <param name="phase">
    /// The phase the failure reports. The same decoder reads scheme files and input files, and
    /// Section 24 orders scheme-loading diagnostics before input-parsing ones, so a decode failure
    /// that named its phase from where the code lives rather than from who called it would both
    /// mislabel the Section 6.4.3 <c>phase</c> member and sort into the wrong group.
    /// </param>
    /// <returns>The decoded text, or the <c>PARSE002</c> the source earned. Never throws.</returns>
    public static DecodedSource Decode(ReadOnlySpan<byte> bytes, string source, DiagnosticPhase phase)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Section 7.4 orders this: four-byte signatures are tested before three- or two-byte ones,
        // so FF FE 00 00 is the prohibited UTF-32LE mark and not UTF-16LE followed by a NUL.
        if (StartsWith(bytes, Utf32LittleEndianBom))
        {
            return Prohibited(source, "UTF-32 little-endian", phase);
        }

        if (StartsWith(bytes, Utf32BigEndianBom))
        {
            return Prohibited(source, "UTF-32 big-endian", phase);
        }

        var (encoding, markLength) =
            StartsWith(bytes, Utf8Bom) ? (SourceEncoding.Utf8, Utf8Bom.Length)
            : StartsWith(bytes, Utf16LittleEndianBom) ? (SourceEncoding.Utf16LittleEndian, Utf16LittleEndianBom.Length)
            : StartsWith(bytes, Utf16BigEndianBom) ? (SourceEncoding.Utf16BigEndian, Utf16BigEndianBom.Length)
            : (SourceEncoding.Utf8, 0);

        var content = bytes[markLength..];
        var decoder = EncodingFor(encoding);

        try
        {
            return new DecodedSource(decoder.GetString(content), encoding);
        }
        catch (DecoderFallbackException)
        {
            var (line, column) = PositionAfterLongestDecodablePrefix(content, encoding);
            return new DecodedSource(DiagnosticCodes.Parse002(
                phase,
                Anchor,
                $"{Describe(encoding)} decoding failed: the byte sequence at this position is not valid "
                + "and Section 7.4 makes an invalid byte sequence an error.",
                cardinalityKey: source,
                source: source,
                line: line,
                column: column));
        }
    }

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private static ReadOnlySpan<byte> Utf16LittleEndianBom => [0xFF, 0xFE];

    private static ReadOnlySpan<byte> Utf16BigEndianBom => [0xFE, 0xFF];

    private static ReadOnlySpan<byte> Utf32LittleEndianBom => [0xFF, 0xFE, 0x00, 0x00];

    private static ReadOnlySpan<byte> Utf32BigEndianBom => [0x00, 0x00, 0xFE, 0xFF];

    private static bool StartsWith(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> mark) =>
        bytes.StartsWith(mark);

    private static DecodedSource Prohibited(string source, string mark, DiagnosticPhase phase) =>
        new(DiagnosticCodes.Parse002(
            phase,
            Anchor,
            $"this source begins with a {mark} byte-order mark, and Section 7.4 permits only UTF-8, "
            + "UTF-16LE, and UTF-16BE.",
            cardinalityKey: source,
            source: source,
            line: 1,
            column: 1));

    private static Encoding EncodingFor(SourceEncoding encoding) => encoding switch
    {
        SourceEncoding.Utf8 => StrictUtf8,
        SourceEncoding.Utf16LittleEndian => StrictUtf16LittleEndian,
        SourceEncoding.Utf16BigEndian => StrictUtf16BigEndian,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Section 7.4 permits three encodings."),
    };

    private static string Describe(SourceEncoding encoding) => encoding switch
    {
        SourceEncoding.Utf8 => "UTF-8",
        SourceEncoding.Utf16LittleEndian => "UTF-16LE",
        SourceEncoding.Utf16BigEndian => "UTF-16BE",
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Section 7.4 permits three encodings."),
    };

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly UnicodeEncoding StrictUtf16LittleEndian = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);

    private static readonly UnicodeEncoding StrictUtf16BigEndian = new(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Finds the Section 7.4 failure position: the line and column immediately after the longest
    /// prefix of <paramref name="content"/> that decodes.
    /// </summary>
    /// <remarks>
    /// The position is defined by the decoding rules, not by where a particular decoder chooses to
    /// report the fault. Those differ. A decoder that reads a lone UTF-16 high surrogate emits
    /// nothing, buffers it, and only rejects the input on the following unit, so its own error index
    /// points past the surrogate; the longest decodable prefix ends before it. Feeding one byte at a
    /// time and counting the characters that emerge measures the prefix rather than the decoder.
    /// This runs only on the failure path, where an extra pass costs nothing.
    /// </remarks>
    private static (int Line, int Column) PositionAfterLongestDecodablePrefix(
        ReadOnlySpan<byte> content,
        SourceEncoding encoding)
    {
        var decoder = EncodingFor(encoding).GetDecoder();
        var line = 1;
        var column = 1;
        var previousWasCarriageReturn = false;
        Span<char> produced = stackalloc char[4];
        Span<byte> one = stackalloc byte[1];

        foreach (var b in content)
        {
            one[0] = b;
            int count;
            try
            {
                count = decoder.GetChars(one, produced, flush: false);
            }
            catch (DecoderFallbackException)
            {
                break;
            }

            for (var i = 0; i < count; i++)
            {
                Advance(produced[i], ref line, ref column, ref previousWasCarriageReturn);
            }
        }

        return (line, column);
    }

    /// <summary>
    /// Advances the Section 22 position by one UTF-16 code unit, counting a surrogate pair once.
    /// </summary>
    private static void Advance(char c, ref int line, ref int column, ref bool previousWasCarriageReturn)
    {
        // Section 22: LF, CRLF, and a lone CR each end a line, and nothing else does. The CR of a
        // CRLF ends the line, so the following LF must not end a second one.
        if (previousWasCarriageReturn && c == '\n')
        {
            previousWasCarriageReturn = false;
            return;
        }

        previousWasCarriageReturn = c == '\r';

        if (c is '\n' or '\r')
        {
            line++;
            column = 1;
            return;
        }

        // A surrogate pair is one scalar and therefore one column, counted on its low half.
        if (!char.IsHighSurrogate(c))
        {
            column++;
        }
    }
}
