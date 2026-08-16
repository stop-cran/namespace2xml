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
    /// <remarks>
    /// Section 6.1: presence is decided "by scanning the raw token vector for the option token, in
    /// either the bare or the inline form, up to the first <c>--</c>", and "that scan applies no
    /// other part of the grammar; in particular it does not work out which tokens are option
    /// values". Skipping a detached value here would give one token two readings at once — data to
    /// this scan, and an option token to the Section 6.2 rule that a detached value may not be an
    /// option token — so this loop deliberately knows nothing about values.
    /// </remarks>
    public static InformationalMode ResolveInformationalMode(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var version = false;

        foreach (var token in arguments)
        {
            if (token is null)
            {
                continue;
            }

            if (token == "--")
            {
                break;
            }

            // Section 6.2 makes the inline form uniform, so the mode is decided by the option
            // name rather than by the whole token. An inline value on either flag is ignored,
            // because Section 6.1 decides from presence before any argument is validated.
            var name = NameOf(token);

            if (name == "--help")
            {
                return InformationalMode.Help;
            }

            if (name == "--version")
            {
                version = true;
            }
        }

        return version ? InformationalMode.Version : InformationalMode.None;
    }

    /// <summary>
    /// The option name a token carries: the part before the first <c>=</c> for a long option, and
    /// the whole token otherwise, matching the Section 6.2 grammar.
    /// </summary>
    private static string NameOf(string token)
    {
        if (!token.StartsWith("--", StringComparison.Ordinal))
        {
            return token;
        }

        var separator = token.IndexOf('=', StringComparison.Ordinal);
        return separator < 0 ? token : token[..separator];
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
