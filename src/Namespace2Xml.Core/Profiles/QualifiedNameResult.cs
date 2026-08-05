namespace Namespace2Xml.Profiles;

/// <summary>
/// Why a qualified name did not lex, and where.
/// </summary>
/// <param name="Message">Prose for the <c>PARSE001</c> the name earns.</param>
/// <param name="Offset">
/// Zero-based offset into the name text. Callers hold the name's position in its source and convert
/// this to the one-based column Section 22 defines.
/// </param>
public readonly record struct NameFault(string Message, int Offset);

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
