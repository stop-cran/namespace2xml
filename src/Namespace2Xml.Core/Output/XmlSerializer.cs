using System.Text;
using System.Xml;
using System.Xml.Linq;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;

namespace Namespace2Xml.Output;

/// <summary>
/// Writes the Section 19.5 XML document.
/// </summary>
/// <remarks>
/// <para>
/// The layout is produced by <see cref="XmlWriter"/> rather than by hand, because Section 16.9's
/// option names are that writer's settings: <c>Indent</c>, <c>NewLineOnAttributes</c>, and the
/// declaration switch each map to one property. Indentation "outside mixed content" is the same
/// writer's rule that an element containing text is written on one line.
/// </para>
/// <para>
/// Section 24 requires LF line endings and no byte-order mark on every platform, so neither is left
/// to the writer's defaults.
/// </para>
/// </remarks>
public sealed class XmlSerializer
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly XmlOutputOptions options;
    private readonly DiagnosticBuffer diagnostics;
    private readonly DestinationRef? destination;

    /// <summary>Creates a serializer.</summary>
    /// <param name="options">The Section 16.9 XML options.</param>
    /// <param name="diagnostics">The buffer refusals accumulate in.</param>
    /// <param name="destination">The Section 6.4.3 <c>destination</c> this instance writes to.</param>
    public XmlSerializer(
        XmlOutputOptions options,
        DiagnosticBuffer diagnostics,
        DestinationRef? destination = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.options = options;
        this.diagnostics = diagnostics;
        this.destination = destination;
    }

    /// <summary>Writes the document.</summary>
    /// <param name="document">The projected document element.</param>
    /// <param name="writer">The buffer to write into.</param>
    /// <returns>
    /// Whether the whole output was written. A false result is either a budget crossing the caller
    /// reads from the writer's fault, or a refusal already reported here.
    /// </returns>
    public bool TrySerialize(XElement document, OutputBufferWriter writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryRender(Marked(document), out var bytes))
        {
            return false;
        }

        return writer.TryWrite(bytes)
            // Section 24: a text output with content ends with exactly one LF. XmlWriter ends the
            // declaration line and every nested element with one, but never the document element.
            && writer.TryWrite("\n"u8);
    }

    /// <summary>
    /// Section 19.5 "preserves mixed content without inserting indentation inside it".
    /// </summary>
    /// <remarks>
    /// <see cref="XmlWriter"/> decides an element is mixed only once it has written text into it,
    /// so an element whose text follows a child element is indented up to that point and then
    /// stops — which puts formatting whitespace inside mixed content and changes what the document
    /// says. The projection already knows the whole element, so an element with any direct text or
    /// CDATA content is given an empty leading text node: the writer then treats it as mixed from
    /// its first child, and an empty string contributes no characters to the output.
    /// </remarks>
    private static XElement Marked(XElement document)
    {
        foreach (var element in document.DescendantsAndSelf())
        {
            if (element.FirstNode is not (null or XText)
                && element.Nodes().Any(node => node is XText))
            {
                element.AddFirst(new XText(string.Empty));
            }
        }

        return document;
    }

    private bool TryRender(XElement document, out byte[] bytes)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = Utf8NoBom,
            Indent = options.Indents(),
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineOnAttributes = options.BreaksAttributeLines(),
            OmitXmlDeclaration = !options.WritesDeclaration(),
            CloseOutput = false,
        };

        // Section 19.5 "uses UTF-8", and XmlWriter takes the declared encoding from the sink rather
        // than from the settings: writing through a StringBuilder declares utf-16 whatever the
        // Encoding says. A byte sink is also what the output buffer wants, so no re-encoding step
        // stands between the writer and the file.
        var stream = new MemoryStream();

        try
        {
            using (var xml = XmlWriter.Create(stream, settings))
            {
                document.WriteTo(xml);
            }
        }
        catch (ArgumentException failure)
        {
            // Section 6.3 forbids a user-caused error escaping "only as an unhandled exception".
            // A name or comment that XML cannot spell is user-caused, and XmlWriter reports it by
            // throwing rather than by a result.
            Refuse(failure.Message);
            bytes = [];

            return false;
        }
        catch (InvalidOperationException failure)
        {
            Refuse(failure.Message);
            bytes = [];

            return false;
        }
        catch (XmlException failure)
        {
            Refuse(failure.Message);
            bytes = [];

            return false;
        }

        bytes = stream.ToArray();

        return true;
    }

    private void Refuse(string because) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Serialize001(
                DiagnosticPhase.Planning,
                "\u00A719.5",
                $"this document has no XML spelling: {because}",
                cardinalityKey: destination?.Canonical ?? "xml",
                destination: destination?.Canonical),
            DestinationOrder: destination?.Order));
}
