namespace Namespace2Xml.Diagnostics;

/// <summary>Diagnostic severity as fixed by specification Section 22.</summary>
public enum DiagnosticSeverity
{
    /// <summary>A warning. Warnings never change the success exit code.</summary>
    Warning,

    /// <summary>A blocking error.</summary>
    Error,
}

/// <summary>
/// Emission phase as fixed by specification Section 6.4.3. Phase is a property of the
/// individual occurrence, not of the code: <c>TYPE001</c>, <c>SERIALIZE001</c> and
/// <c>LIMIT001</c> each arise in more than one phase.
/// </summary>
public enum DiagnosticPhase
{
    /// <summary>Argument scanning and option validation, before Section 15.1 step 1.</summary>
    Cli,

    /// <summary>Section 15.1 steps 1 through 4.</summary>
    Scheme,

    /// <summary>Section 15.1 steps 5 through 12.</summary>
    Input,

    /// <summary>Section 15.1 steps 13 through 19.</summary>
    Planning,

    /// <summary>Section 15.1 step 20.</summary>
    Publication,
}

/// <summary>Encoding of the diagnostic stream, selected by <c>--diagnostics-format</c>.</summary>
public enum DiagnosticFormat
{
    /// <summary>Human-readable stream interleaved with operational messages.</summary>
    Text,

    /// <summary>Single canonical JSON array, specification Section 6.4.3.</summary>
    Json,
}
