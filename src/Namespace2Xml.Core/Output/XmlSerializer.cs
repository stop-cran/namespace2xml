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
    /// <param name="document">The projected document.</param>
    /// <param name="writer">The buffer to write into.</param>
    /// <returns>
    /// Whether the whole output was written. A false result is either a budget crossing the caller
    /// reads from the writer's fault, or a refusal already reported here.
    /// </returns>
    public bool TrySerialize(XmlDocumentProjection document, OutputBufferWriter writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryRender(document, out var bytes))
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

    /// <summary>
    /// Section 19.5: declares a generated prefix for every namespace an attribute needs, named
    /// <c>n1</c>, <c>n2</c>, … in order of first need in document order.
    /// </summary>
    /// <remarks>
    /// An unprefixed attribute is in no namespace, so a namespaced attribute cannot borrow the
    /// default declaration an element uses and must have a prefix. Left alone, <see cref="XmlWriter"/>
    /// invents one from its own scope counter, which produced names like <c>p2</c> — deterministic
    /// for this writer, and unguessable for any other implementation of the same specification.
    /// Section 24 asks two conforming implementations to agree byte for byte, so the name has to
    /// come from the specification rather than from the library.
    /// </remarks>
    private static XElement Prefixed(XElement document)
    {
        var assigned = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var element in document.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                var uri = attribute.Name.NamespaceName;

                if (uri.Length == 0 || attribute.IsNamespaceDeclaration || assigned.ContainsKey(uri))
                {
                    continue;
                }

                assigned.Add(uri, $"n{assigned.Count + 1}");
            }
        }

        foreach (var (uri, prefix) in assigned)
        {
            document.Add(new XAttribute(XNamespace.Xmlns + prefix, uri));
        }

        return document;
    }

    private bool TryRender(XmlDocumentProjection document, out byte[] bytes)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = Utf8NoBom,
            Indent = options.Indents(),
            IndentChars = "  ",
            NewLineChars = "\n",
            // Section 3.3 requires a round trip to preserve content. XmlWriter's default
            // NewLineHandling.Replace rewrites a CR inside text content to NewLineChars, and a
            // literal CR would be lost anyway: XML 1.0 section 2.11 makes every parser normalize
            // one to LF. Entitize writes '&#xD;', which is the only spelling that survives.
            NewLineHandling = NewLineHandling.Entitize,
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
                // XML allows comments in the prolog and the epilogue, which is the only place a
                // Section 20 document comment can go once Section 19.5 has promoted the view's only
                // member to the document element.
                foreach (var comment in document.Leading)
                {
                    comment.WriteTo(xml);
                }

                Prefixed(Marked(document.Element)).WriteTo(xml);

                foreach (var comment in document.Trailing)
                {
                    comment.WriteTo(xml);
                }
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
