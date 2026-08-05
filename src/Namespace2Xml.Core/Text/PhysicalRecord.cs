namespace Namespace2Xml.Text;

/// <summary>
/// One physical record of a namespace-profile document, as produced by the Appendix A.1
/// <c>document</c> production.
/// </summary>
/// <param name="Text">
/// The record's scalars, without its terminator. Never trimmed: Section 8.1 keeps leading and
/// trailing spaces and tabs, which are part of a name or value.
/// </param>
/// <param name="Line">One-based line number under Section 22.</param>
public readonly record struct PhysicalRecord(string Text, int Line);
