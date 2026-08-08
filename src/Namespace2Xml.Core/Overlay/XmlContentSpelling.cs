namespace Namespace2Xml.Overlay;

/// <summary>How Section 11.6 spells a textual payload when XML holds it.</summary>
/// <remarks>
/// <para>
/// "CDATA is retained as a distinct XML node kind", and "XML output must preserve imported CDATA
/// as CDATA unless an output option requests conversion to ordinary text". A reader that returned
/// only the decoded characters would have thrown the distinction away before any output option
/// could act on it, so the spelling travels with the value.
/// </para>
/// <para>
/// It rides on <see cref="ScalarPayload"/> rather than on the node, because Section 4.4 settles a
/// node's payload by replacing it outright with the latest contribution. Anything held beside the
/// payload would have to be merged by a rule of its own, and could then describe text that a later
/// contribution had already replaced. Here the two cannot disagree.
/// </para>
/// </remarks>
public enum XmlContentSpelling
{
    /// <summary>An ordinary text node, and the spelling of every payload from every other format.</summary>
    Text,

    /// <summary>A CDATA section.</summary>
    Cdata,
}
