using Namespace2Xml.Diagnostics;

namespace Namespace2Xml.Cli;

/// <summary>
/// The outcome of parsing an argument vector: either a command line, or the single <c>CLI001</c>
/// the vector earned.
/// </summary>
/// <remarks>
/// Section 22 scopes <c>CLI001</c> to once per invocation, so this carries one diagnostic rather
/// than a collection. A parser that reported every fault it could find would have to choose an
/// order to report them in, and that order would be a second contract nobody wrote down.
/// </remarks>
public sealed class CommandLineResult
{
    internal CommandLineResult(CommandLine commandLine) => CommandLine = commandLine;

    internal CommandLineResult(Diagnostic diagnostic) => Diagnostic = diagnostic;

    /// <summary>The parsed command line, or <see langword="null"/> when parsing failed.</summary>
    public CommandLine? CommandLine { get; }

    /// <summary>The <c>CLI001</c> occurrence, or <see langword="null"/> when parsing succeeded.</summary>
    public Diagnostic? Diagnostic { get; }

    /// <summary>Whether the vector parsed.</summary>
    public bool Succeeded => CommandLine is not null;
}
