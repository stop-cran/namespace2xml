namespace Namespace2Xml.Text;

/// <summary>
/// The encoding a source is decoded with, under specification Section 7.4.
/// </summary>
/// <remarks>
/// The specification permits exactly three. There is deliberately no member for UTF-32 or for any
/// other encoding: a source that is not one of these three is rejected, never decoded. A member
/// added here would be an encoding the specification does not allow.
/// </remarks>
public enum SourceEncoding
{
    /// <summary>Strict UTF-8, selected by a UTF-8 byte-order mark or by the Section 7.4 default.</summary>
    Utf8,

    /// <summary>UTF-16 little-endian, selected by an <c>FF FE</c> byte-order mark.</summary>
    Utf16LittleEndian,

    /// <summary>UTF-16 big-endian, selected by an <c>FE FF</c> byte-order mark.</summary>
    Utf16BigEndian,
}
