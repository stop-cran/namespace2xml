namespace Namespace2Xml.Overlay;

/// <summary>
/// Where a value was written, for the Section 22 members a diagnostic about it supplies.
/// </summary>
/// <param name="Source">
/// The Section 6.4.3 <c>source</c> member, or <see langword="null"/> when the value came from an
/// origin that has no file name, such as a command-line variable.
/// </param>
/// <param name="Line">
/// The one-based logical line, or <see langword="null"/> when the origin has no line to report a
/// column against. Section 22: "column additionally requires line".
/// </param>
/// <param name="Column">
/// The one-based logical column, or <see langword="null"/> when the condition is about a whole
/// record rather than a position inside one.
/// </param>
/// <remarks>
/// Only an unresolved payload carries this. Every other payload is either already correct or is
/// the subject of a diagnostic raised over the merged model, which Section 22 says supplies no
/// <c>source</c> at all — the tie-breaker there is that a member names "where the condition is,
/// not where its subject came from". A reference is the exception: Section 22 counts the reference
/// diagnostics "once per reachable owning value", so the condition *is* the written value, and the
/// value has to remember where it was written to be able to say so.
/// </remarks>
public readonly record struct ValueOrigin(string? Source, int? Line, int? Column)
{
    /// <summary>
    /// The origin of a value substituted into a generated contribution by a Section 12 rule.
    /// </summary>
    /// <param name="source">The rule's source.</param>
    /// <param name="line">The line the rule was written on.</param>
    /// <remarks>
    /// There is no column, because Section 22 says a condition "raised over a compiled declaration
    /// or a wildcard rule rather than over the text that produced it, supplies <c>line</c> without
    /// <c>column</c> rather than inventing a precision it does not have". The generated value's
    /// reference name is not the text at any column of the rule: capture substitution rewrote it.
    /// </remarks>
    public static ValueOrigin OfRule(string? source, int? line) => new(source, line, null);
}
