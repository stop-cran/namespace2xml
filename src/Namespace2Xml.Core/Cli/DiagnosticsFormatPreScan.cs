using Namespace2Xml.Diagnostics;

namespace Namespace2Xml.Cli;

/// <summary>
/// Total pre-scan of the raw argument vector that resolves the diagnostic stream encoding
/// before any other argument is validated, so that an invalid command line can itself be
/// reported in the requested encoding. Specification Section 6.4.1.
/// </summary>
/// <remarks>
/// This scan always terminates, always resolves exactly one encoding, and never emits a
/// diagnostic. Ordinary option parsing validates the option separately and reports
/// <c>CLI001</c> for a missing or unrecognized value, using the encoding resolved here.
/// </remarks>
public static class DiagnosticsFormatPreScan
{
    private const string Option = "--diagnostics-format";
    private const string InlinePrefix = Option + "=";

    /// <summary>Resolves the encoding. Never throws for any argument vector.</summary>
    public static DiagnosticFormat Resolve(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string? winning = null;

        for (var i = 0; i < arguments.Count; i++)
        {
            var token = arguments[i];

            // A host cannot put a null in argv, but the public contract above promises this method
            // never throws for any argument vector, and an in-process caller can supply one.
            if (token is null)
            {
                continue;
            }

            // Tokens at or after the first bare "--" are list-option values (Section 6.2).
            if (token == "--")
            {
                break;
            }

            if (token == Option)
            {
                var next = i + 1 < arguments.Count ? arguments[i + 1] : null;

                if (next is not null && next != "--")
                {
                    winning = next;
                    i++;
                }

                continue;
            }

            if (token.StartsWith(InlinePrefix, StringComparison.Ordinal))
            {
                winning = token[InlinePrefix.Length..];
            }
        }

        return Ascii.EqualsIgnoreCase(winning, "json") ? DiagnosticFormat.Json : DiagnosticFormat.Text;
    }

    /// <summary>
    /// Reports whether an informational mode is present, honouring the Section 6.1 precedence
    /// in which <c>--help</c> outranks <c>--version</c> and neither validates other arguments.
    /// </summary>
    public static InformationalMode ResolveInformationalMode(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var version = false;

        for (var i = 0; i < arguments.Count; i++)
        {
            var token = arguments[i];

            if (token == "--")
            {
                break;
            }

            if (token == "--help")
            {
                return InformationalMode.Help;
            }

            if (token == "--version")
            {
                version = true;
            }
            else if (token == Option && i + 1 < arguments.Count && arguments[i + 1] != "--")
            {
                // Skip the value so that "--diagnostics-format --version" treats --version as data.
                i++;
            }
        }

        return version ? InformationalMode.Version : InformationalMode.None;
    }
}

/// <summary>Immediate informational modes of specification Section 6.1.</summary>
public enum InformationalMode
{
    /// <summary>Ordinary operation.</summary>
    None,

    /// <summary>Print help and exit successfully.</summary>
    Help,

    /// <summary>Print version and exit successfully.</summary>
    Version,
}
