using System.Collections.Immutable;

namespace Namespace2Xml.Text;

/// <summary>
/// Splits decoded text into physical records under the Appendix A.1 <c>document</c> production.
/// </summary>
/// <remarks>
/// <para>
/// The terminator set is exactly LF, CRLF, and a lone CR. Appendix A.1 states that U+0085, U+2028,
/// and U+2029 are <c>record-scalar</c> and not <c>line-end</c>, which rules out every framework
/// line-splitting helper: <c>string.Split</c> on <c>Environment.NewLine</c> is platform-dependent,
/// and .NET's own <c>ReadLine</c> and <c>EnumerateLines</c> both treat U+0085, U+2028, and U+2029 as
/// terminators. Either would make a value containing U+2028 parse as two records on some inputs and
/// one on others, which is a determinism failure Section 24 forbids.
/// </para>
/// <para>
/// <c>document = [record] *(line-end [record])</c> admits an empty record on both sides of every
/// terminator, so a trailing terminator yields a final empty record and empty text yields one empty
/// record. Both are kept rather than filtered: Section 8.1 discards them by classification, and a
/// splitter that discarded them here would also shift the line numbers of everything after a blank
/// line.
/// </para>
/// </remarks>
public static class PhysicalRecordReader
{
    /// <summary>Splits decoded text into physical records, numbering lines from one.</summary>
    /// <param name="text">Decoded text, with any byte-order mark already removed by Section 7.4.</param>
    public static ImmutableArray<PhysicalRecord> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var records = ImmutableArray.CreateBuilder<PhysicalRecord>();
        var line = 1;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is not ('\n' or '\r'))
            {
                continue;
            }

            records.Add(new PhysicalRecord(text[start..i], line));
            line++;

            // A CR consumes a following LF, so CRLF terminates one record rather than two.
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        records.Add(new PhysicalRecord(text[start..], line));
        return records.ToImmutable();
    }
}
