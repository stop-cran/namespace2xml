using System.Collections.Immutable;

namespace Namespace2Xml.Diagnostics;

/// <summary>
/// One emitted diagnostic occurrence. The member set is closed by the schema in
/// specification Section 6.4.3; adding a member requires a specification change.
/// </summary>
/// <remarks>
/// The constructor rejects every combination the Section 6.4.3 schema forbids, so an instance that
/// exists is one the schema accepts. The properties are get-only rather than <c>init</c>, which
/// makes the constructor the sole gate: a <c>with</c> expression cannot route around it. Validating
/// inside the writer instead would leave the schema enforced only on the path that happens to be
/// exercised, which is how a stream ends up invalid only under the encoding nobody tested.
/// </remarks>
public sealed record Diagnostic
{
    /// <summary>Creates a diagnostic, rejecting any shape the Section 6.4.3 schema forbids.</summary>
    /// <param name="code">Stable diagnostic code, for example <c>TYPE001</c>.</param>
    /// <param name="severity">Severity fixed by the Section 22 registry.</param>
    /// <param name="phase">Emission phase of this occurrence.</param>
    /// <param name="spec">Specification anchor of the clause being enforced, for example <c>§13.1</c>.</param>
    /// <param name="message">Localizable prose. Never compared by the conformance harness.</param>
    /// <param name="source">Source file, relative with <c>/</c> separators when inside the invocation's input set.</param>
    /// <param name="line">One-based line. Requires <paramref name="source"/>.</param>
    /// <param name="column">One-based column. Requires <paramref name="line"/>.</param>
    /// <param name="path">Canonical qualified path under Appendix A.</param>
    /// <param name="declaration">The scheme declaration that produced the condition.</param>
    /// <param name="rule">Appendix A canonical names of the wildcard rules held responsible.</param>
    /// <param name="destination">Destination path, relative with <c>/</c> separators when inside the output root.</param>
    public Diagnostic(
        string code,
        DiagnosticSeverity severity,
        DiagnosticPhase phase,
        string spec,
        string message,
        string? source = null,
        int? line = null,
        int? column = null,
        string? path = null,
        string? declaration = null,
        ImmutableArray<string> rule = default,
        string? destination = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(message);

        if (!IsWellFormedSpecAnchor(spec))
        {
            throw new ArgumentException(
                $"'{spec}' is not a specification anchor. Section 6.4.3 spells one as U+00A7 followed by "
                + "the section or appendix number, optionally followed by '.' and further parts, for "
                + "example '\u00A713.1' or '\u00A7B'.",
                nameof(spec));
        }

        if (line is not null && source is null)
        {
            throw new ArgumentException(
                "A line requires a source; the Section 6.4.3 schema forbids one without the other.",
                nameof(line));
        }

        if (column is not null && line is null)
        {
            throw new ArgumentException(
                "A column requires a line; the Section 6.4.3 schema forbids one without the other.",
                nameof(column));
        }

        if (line is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(line), line, "Lines are one-based.");
        }

        if (column is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Columns are one-based.");
        }

        Code = code;
        Severity = severity;
        Phase = phase;
        Spec = spec;
        Message = message;
        Source = source;
        Line = line;
        Column = column;
        Path = path;
        Declaration = declaration;
        Rule = rule.IsDefaultOrEmpty ? [] : rule;
        Destination = destination;
    }

    /// <summary>Stable diagnostic code, for example <c>TYPE001</c>.</summary>
    public string Code { get; }

    /// <summary>Severity fixed by the Section 22 registry.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>Emission phase of this occurrence.</summary>
    public DiagnosticPhase Phase { get; }

    /// <summary>Specification anchor of the clause being enforced, for example <c>§13.1</c>.</summary>
    public string Spec { get; }

    /// <summary>Localizable prose. Never compared by the conformance harness.</summary>
    public string Message { get; }

    /// <summary>Source file, relative with <c>/</c> separators when inside the invocation's input set.</summary>
    public string? Source { get; }

    /// <summary>One-based line. Never present without <see cref="Source"/>.</summary>
    public int? Line { get; }

    /// <summary>One-based column. Never present without <see cref="Line"/>.</summary>
    public int? Column { get; }

    /// <summary>Canonical qualified path under Appendix A.</summary>
    public string? Path { get; }

    /// <summary>The scheme declaration that produced the condition.</summary>
    public string? Declaration { get; }

    /// <summary>
    /// Appendix A canonical names of the wildcard rules held responsible, in Section 12.4 source
    /// order. Empty when the condition locates its rule through <see cref="Source"/>,
    /// <see cref="Line"/> and <see cref="Path"/> instead, in which case the member is omitted.
    /// </summary>
    public ImmutableArray<string> Rule { get; }

    /// <summary>Destination path, relative with <c>/</c> separators when inside the output root.</summary>
    public string? Destination { get; }

    /// <summary>
    /// Recognizes the Section 6.4.3 anchor spelling: U+00A7, then a section number or a single
    /// appendix letter, then zero or more <c>.</c>-separated numeric parts.
    /// </summary>
    private static bool IsWellFormedSpecAnchor(string? spec)
    {
        if (string.IsNullOrEmpty(spec) || spec[0] != '\u00A7')
        {
            return false;
        }

        var parts = spec[1..].Split('.');
        var head = parts[0];
        if (head.Length == 0)
        {
            return false;
        }

        var headIsAppendix = head.Length == 1 && head[0] is >= 'A' and <= 'Z';
        if (!headIsAppendix && !head.All(char.IsAsciiDigit))
        {
            return false;
        }

        return parts.Skip(1).All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }
}
