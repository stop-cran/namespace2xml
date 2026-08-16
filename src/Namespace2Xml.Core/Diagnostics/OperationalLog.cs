namespace Namespace2Xml.Diagnostics;

/// <summary>
/// The level of an operational message, on the Section 6.2 scale.
/// </summary>
/// <remarks>
/// Section 22 puts these outside the diagnostic registry: "information, debug, and trace
/// operational messages are not diagnostics". They carry no code, no phase and no specification
/// anchor, they are never compared for byte-identity, and they never affect an exit code. What
/// they do carry is a level, because Section 6.2 gates them by one.
/// </remarks>
public enum OperationalLevel
{
    /// <summary>
    /// Section 6.2 level 1: per-file parsing, wildcard candidate and match, generated node,
    /// reference chain, and publication detail.
    /// </summary>
    Trace,

    /// <summary>
    /// Section 6.2 level 2: pipeline phase progress, merge decisions, expansion counters, and
    /// output plan summaries.
    /// </summary>
    Debug,

    /// <summary>
    /// Section 6.2 level 3. The specification names exactly one message at this level: Section
    /// 21.4, replacing an existing destination.
    /// </summary>
    Information,
}

/// <summary>Where operational messages go.</summary>
/// <remarks>
/// This is separate from <see cref="Namespace2Xml.Pipeline.DiagnosticBuffer"/> on purpose.
/// Diagnostics are buffered, ordered by Section 24, and byte-identical across platforms;
/// operational messages are none of those things and must not be able to reach the diagnostic
/// stream by accident. Section 6.4.3 requires standard error under <c>json</c> to carry "exactly
/// one JSON array and nothing else", which the two types being distinct makes structurally true
/// rather than merely observed.
/// </remarks>
public interface IOperationalLog
{
    /// <summary>Gets whether any message at this level would be written.</summary>
    /// <param name="level">The level to test.</param>
    /// <returns><see langword="true"/> when a message at that level is written.</returns>
    /// <remarks>
    /// Offered so that a caller can skip composing a message nobody will read. Every call site is
    /// obliged to behave identically whether or not it consults this, because Section 6.2 says
    /// verbosity "never changes exit codes, output files, resource accounting, or the underlying
    /// deterministic diagnostic order".
    /// </remarks>
    bool IsEnabled(OperationalLevel level);

    /// <summary>Writes one operational message.</summary>
    /// <param name="level">The level of the message.</param>
    /// <param name="message">The message, without a trailing line terminator.</param>
    void Write(OperationalLevel level, string message);
}

/// <summary>An operational log that discards everything.</summary>
/// <remarks>
/// The default wherever a log is optional, so that a caller which supplies none behaves exactly as
/// one that selected <c>--verbosity none</c> rather than crashing on a null.
/// </remarks>
public sealed class SilentOperationalLog : IOperationalLog
{
    /// <summary>The single instance.</summary>
    public static SilentOperationalLog Instance { get; } = new();

    private SilentOperationalLog()
    {
    }

    /// <inheritdoc/>
    public bool IsEnabled(OperationalLevel level) => false;

    /// <inheritdoc/>
    public void Write(OperationalLevel level, string message)
    {
    }
}
