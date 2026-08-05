using Namespace2Xml.Diagnostics;

namespace Namespace2Xml.Cli;

/// <summary>
/// Ordinary validation of <c>--diagnostics-format</c>, run after the Section 6.4.1 pre-scan has
/// already fixed the encoding. A missing or unrecognized value is <c>CLI001</c>, reported in the
/// encoding the pre-scan resolved.
/// </summary>
public static class DiagnosticsFormatValidation
{
    private const string Option = "--diagnostics-format";
    private const string InlinePrefix = Option + "=";

    /// <summary>
    /// Returns the single <c>CLI001</c> occurrence for this option, or <see langword="null"/>
    /// when every occurrence is well formed. <c>CLI001</c> is once per invocation, so at most
    /// one diagnostic is produced however many occurrences are malformed.
    /// </summary>
    public static Diagnostic? Validate(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        for (var i = 0; i < arguments.Count; i++)
        {
            var token = arguments[i];

            if (token == "--")
            {
                break;
            }

            string? value;

            if (token == Option)
            {
                var next = i + 1 < arguments.Count ? arguments[i + 1] : null;

                if (next is null || next == "--")
                {
                    return Malformed("--diagnostics-format requires a value of 'text' or 'json'.");
                }

                value = next;
                i++;
            }
            else if (token.StartsWith(InlinePrefix, StringComparison.Ordinal))
            {
                value = token[InlinePrefix.Length..];
            }
            else
            {
                continue;
            }

            if (!Ascii.EqualsIgnoreCase(value, "text") && !Ascii.EqualsIgnoreCase(value, "json"))
            {
                return Malformed(
                    $"--diagnostics-format accepts 'text' or 'json', case-insensitively, but got '{value}'.");
            }
        }

        return null;
    }

    private static Diagnostic Malformed(string message) => new(
        Code: "CLI001",
        Severity: DiagnosticSeverity.Error,
        Phase: DiagnosticPhase.Cli,
        Spec: "§6.4.1",
        Message: message);
}
