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

    /// <summary>
    /// A Section 11.5 comment node, which holds text but is not a value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 11.5 retains comments "as ordered comment nodes" rather than binding them to a
    /// neighbouring value, "because a comment may occur between mixed-content nodes or after the
    /// final child". Section 4.5 says the same from the other side: "standalone XML comments remain
    /// ordered content nodes and are not reassigned to adjacent values". A comment is therefore not
    /// a <see cref="BoundComment"/>, which is the channel for every other format.
    /// </para>
    /// <para>
    /// It is a spelling rather than a node kind of its own because Section 11.4 allows comments to
    /// be "selected for ignore and conversion through <c>#n</c>": a comment occupies an ordinary
    /// content-token slot, and <c>type=text</c> at that slot converts it to the text node it would
    /// otherwise have been. Both operations need it to be an addressable overlay node, and only its
    /// spelling distinguishes it from the text beside it.
    /// </para>
    /// <para>
    /// Section 13.1 nevertheless says comments "have no scalar payload and are invisible to
    /// format-agnostic reference resolution". <see cref="ScalarPayload.IsValue"/> is that rule: the
    /// payload holds the comment's characters so an output can render them, and every consumer that
    /// asks for a <em>value</em> is told there is none.
    /// </para>
    /// </remarks>
    Comment,
}
