namespace Namespace2Xml.Overlay;

/// <summary>The document position of an XML comment outside the document element.</summary>
public enum XmlEnvelopePlacement
{
    /// <summary>After the XML declaration and before the document element.</summary>
    Leading,

    /// <summary>After the document element.</summary>
    Trailing,
}

/// <summary>A Section 11.5 XML comment retained outside the addressable overlay.</summary>
/// <param name="Text">The decoded comment text.</param>
/// <param name="Placement">Its leading or trailing document position.</param>
/// <param name="Order">Its stable source-occurrence identity and ordering key.</param>
public readonly record struct XmlEnvelopeComment(
    string Text,
    XmlEnvelopePlacement Placement,
    StableOrderingKey Order);
