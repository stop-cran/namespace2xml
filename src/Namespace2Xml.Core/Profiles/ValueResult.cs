namespace Namespace2Xml.Profiles;

/// <summary>
/// The diagnostic a value fault earns, as Appendix B assigns it.
/// </summary>
public enum ValueFaultKind
{
    /// <summary>A malformed or free-wildcard reference: <c>REFERENCE001</c>.</summary>
    Reference,

    /// <summary>An invalid wildcard capture outside a reference: <c>WILDCARD001</c>.</summary>
    Wildcard,
}

/// <summary>
/// Why a value did not lex, and where.
/// </summary>
/// <param name="Message">Prose for the diagnostic the value earns.</param>
/// <param name="Offset">
/// Zero-based offset into the value text. Callers hold the value's position in its source and
/// convert this to the one-based column Section 22 defines.
/// </param>
/// <param name="Kind">Which diagnostic the fault earns.</param>
public readonly record struct ValueFault(string Message, int Offset, ValueFaultKind Kind);

/// <summary>
/// The outcome of lexing a value: the value, or the single fault it earned.
/// </summary>
/// <remarks>
/// One fault, not a list, because Section 22 gives <c>REFERENCE001</c> and <c>WILDCARD001</c> the
/// cardinalities "once per reachable owning value" and "once per rule".
/// </remarks>
public sealed class ValueResult
{
    internal ValueResult(InterpretedValue value) => Value = value;

    internal ValueResult(ValueFault fault) => Fault = fault;

    /// <summary>The lexed value, or <see langword="null"/> when lexing failed.</summary>
    public InterpretedValue? Value { get; }

    /// <summary>The fault, or <see langword="null"/> when lexing succeeded.</summary>
    public ValueFault? Fault { get; }

    /// <summary>Whether the value lexed.</summary>
    public bool Succeeded => Value is not null;
}
