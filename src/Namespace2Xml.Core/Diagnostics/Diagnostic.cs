namespace Namespace2Xml.Diagnostics;

/// <summary>
/// One emitted diagnostic occurrence. The member set is closed by the schema in
/// specification Section 6.4.3; adding a member requires a specification change.
/// </summary>
/// <param name="Code">Stable diagnostic code, for example <c>TYPE001</c>.</param>
/// <param name="Severity">Severity fixed by the Section 22 registry.</param>
/// <param name="Phase">Emission phase of this occurrence.</param>
/// <param name="Spec">Specification anchor of the clause being enforced, for example <c>§13.1</c>.</param>
/// <param name="Message">Localizable prose. Never compared by the conformance harness.</param>
/// <param name="Source">Source file, relative with <c>/</c> separators when inside the invocation's input set.</param>
/// <param name="Line">One-based line. Requires <paramref name="Source"/>.</param>
/// <param name="Column">One-based column. Requires <paramref name="Line"/>.</param>
/// <param name="Path">Canonical qualified path under Appendix A.</param>
/// <param name="Declaration">The scheme declaration that produced the condition.</param>
/// <param name="Rule">The wildcard rule that produced the condition.</param>
/// <param name="Destination">Destination path, relative with <c>/</c> separators when inside the output root.</param>
public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    DiagnosticPhase Phase,
    string Spec,
    string Message,
    string? Source = null,
    int? Line = null,
    int? Column = null,
    string? Path = null,
    string? Declaration = null,
    string? Rule = null,
    string? Destination = null);
