namespace Namespace2Xml.Profiles;

/// <summary>
/// Why a qualified name did not lex, and where.
/// </summary>
/// <param name="Message">Prose for the diagnostic the name earns.</param>
/// <param name="Offset">
/// Zero-based offset into the name text. Callers hold the name's position in its source and convert
/// this to the one-based column Section 22 defines.
/// </param>
public readonly record struct NameFault(string Message, int Offset)
{
    /// <summary>
    /// Whether the fault is an invalid wildcard capture rather than a malformed name.
    /// </summary>
    /// <remarks>
    /// Appendix B maps every condition to exactly one most-specific code, and an invalid capture
    /// outside a reference is <c>WILDCARD001</c> rather than the <c>PARSE001</c> a malformed name
    /// earns. Inside a reference both are <c>REFERENCE001</c>, so a caller that lexed a reference
    /// name ignores this.
    /// </remarks>
    public bool IsWildcardFault { get; init; }
}

/// <summary>
/// The outcome of lexing a qualified name: the name, or the single fault it earned.
/// </summary>
public sealed class QualifiedNameResult
{
    internal QualifiedNameResult(QualifiedName name) => Name = name;

    internal QualifiedNameResult(NameFault fault) => Fault = fault;

    /// <summary>The lexed name, or <see langword="null"/> when lexing failed.</summary>
    public QualifiedName? Name { get; }

    /// <summary>The fault, or <see langword="null"/> when lexing succeeded.</summary>
    public NameFault? Fault { get; }

    /// <summary>Whether the name lexed.</summary>
    public bool Succeeded => Name is not null;
}
