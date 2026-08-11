using Namespace2Xml.Cli;

namespace Namespace2Xml.Diagnostics;

/// <summary>
/// Writes operational messages to a text stream, gated by the Section 6.2 threshold and by the
/// Section 6.4.3 rule that the <c>json</c> encoding suppresses them entirely.
/// </summary>
/// <remarks>
/// <para>
/// Messages are terminated with LF rather than <c>Environment.NewLine</c>. They are not part of
/// Section 24 byte-identity — the specification says their prose is localizable — but a stream
/// that changes shape with the host is harder to consume for no benefit, and the diagnostic lines
/// beside them already use LF.
/// </para>
/// <para>
/// A failure to write is swallowed for the reason Section 6.4.3 gives about the diagnostic stream
/// itself: a full or closed standard error must not turn a decided outcome into a different one.
/// An operational message has even less claim to change a result than a diagnostic does.
/// </para>
/// </remarks>
public sealed class OperationalLogWriter : IOperationalLog
{
    private readonly TextWriter writer;
    private readonly Verbosity verbosity;

    /// <summary>Creates a writer.</summary>
    /// <param name="writer">Where messages are written. Standard error, per Section 6.2.</param>
    /// <param name="verbosity">The selected threshold.</param>
    public OperationalLogWriter(TextWriter writer, Verbosity verbosity)
    {
        ArgumentNullException.ThrowIfNull(writer);

        this.writer = writer;
        this.verbosity = verbosity;
    }

    /// <summary>
    /// Creates the log for one invocation, which is silent under the <c>json</c> encoding.
    /// </summary>
    /// <param name="writer">Where messages are written.</param>
    /// <param name="command">The parsed command line.</param>
    /// <returns>The log to use for the run.</returns>
    /// <remarks>
    /// Section 6.4.3 suppresses operational messages "entirely, at every verbosity", so the
    /// encoding is checked before the threshold and the result is the silent log rather than a
    /// writer that happens never to fire. The difference matters: it means no code path under
    /// <c>json</c> holds a writer aimed at the stream carrying the array.
    /// </remarks>
    public static IOperationalLog For(TextWriter writer, CommandLine command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.DiagnosticsFormat == DiagnosticFormat.Json)
        {
            return SilentOperationalLog.Instance;
        }

        return new OperationalLogWriter(writer, command.Verbosity);
    }

    /// <inheritdoc/>
    public bool IsEnabled(OperationalLevel level) => verbosity.Admits(level);

    /// <inheritdoc/>
    public void Write(OperationalLevel level, string message)
    {
        if (!verbosity.Admits(level))
        {
            return;
        }

        try
        {
            writer.Write(Render(level, message) + "\n");
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Renders one message line.</summary>
    /// <param name="level">The level of the message.</param>
    /// <param name="message">The message.</param>
    /// <returns>The line, without its terminator.</returns>
    /// <remarks>
    /// The level is spelled out in lower case so an operational line is distinguishable from a
    /// diagnostic line at a glance, and so that a reader who filtered with <c>--verbosity</c> can
    /// see which threshold admitted what.
    /// </remarks>
    public static string Render(OperationalLevel level, string message) =>
        level switch
        {
            OperationalLevel.Trace => "trace: ",
            OperationalLevel.Debug => "debug: ",
            _ => "info: ",
        } + message;
}
