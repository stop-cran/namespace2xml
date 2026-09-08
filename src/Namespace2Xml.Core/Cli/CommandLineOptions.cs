using System.Collections.Immutable;
using System.Globalization;

namespace Namespace2Xml.Cli;

/// <summary>The value cardinality of one specification Section 6.2 option.</summary>
internal enum CommandLineOptionArity
{
    /// <summary>Informational presence flag whose inline spelling is ignored before validation.</summary>
    Informational,

    /// <summary>Valueless operational flag whose inline spelling is invalid.</summary>
    Flag,

    /// <summary>Accepts values until the next option token, concatenating across occurrences.</summary>
    List,

    /// <summary>Accepts exactly one value; a later occurrence overrides an earlier one.</summary>
    Single,
}

/// <summary>The section in which an option is rendered by <c>--help</c>.</summary>
internal enum CommandLineHelpGroup
{
    /// <summary>An option required for an operational invocation.</summary>
    Required,

    /// <summary>An optional operational or informational option.</summary>
    Common,

    /// <summary>A resource-limit option.</summary>
    Limits,
}

/// <summary>The Section 6.2 grammar and display form of a resource limit.</summary>
internal enum ResourceLimitKind
{
    /// <summary>A count or depth rendered as invariant grouped digits.</summary>
    Count,

    /// <summary>A byte count rendered with an exact IEC unit when possible.</summary>
    Bytes,
}

/// <summary>Metadata tying one limit option to its effective <see cref="ResourceLimits"/> value.</summary>
/// <param name="Kind">The value grammar and default display form.</param>
/// <param name="ReadValue">Reads this option's value from a limit set.</param>
internal sealed record ResourceLimitOption(
    ResourceLimitKind Kind,
    Func<ResourceLimits, long> ReadValue)
{
    private const long Kibibyte = 1024;
    private const long Mebibyte = 1024 * 1024;
    private const long Gibibyte = 1024 * 1024 * 1024;

    /// <summary>Formats this limit's effective default for the help inventory.</summary>
    /// <param name="limits">The runtime limit set whose value is displayed.</param>
    /// <returns>Invariant grouped digits, or an exact human-readable IEC byte value.</returns>
    internal string FormatValue(ResourceLimits limits)
    {
        var value = ReadValue(limits);

        if (Kind == ResourceLimitKind.Count)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        foreach (var (suffix, scale) in new[]
                 {
                     ("GiB", Gibibyte),
                     ("MiB", Mebibyte),
                     ("KiB", Kibibyte),
                 })
        {
            if (value % scale == 0)
            {
                return $"{value / scale} {suffix}";
            }
        }

        return $"{value.ToString("N0", CultureInfo.InvariantCulture)} bytes";
    }
}

/// <summary>One accepted command-line option and the metadata shared by parsing and help.</summary>
/// <param name="Name">Canonical long name.</param>
/// <param name="Alias">Optional short alias.</param>
/// <param name="Arity">Value cardinality.</param>
/// <param name="DiagnosticAnchor">Specification anchor for a malformed use.</param>
/// <param name="HelpGroup">The structured help section containing the option.</param>
/// <param name="ValueLabel">Placeholder shown for a value-bearing option.</param>
/// <param name="Description">Help description, excluding a resource limit's effective default.</param>
/// <param name="Limit">Resource-limit metadata, or <see langword="null"/> for other options.</param>
internal sealed record CommandLineOption(
    string Name,
    string? Alias,
    CommandLineOptionArity Arity,
    string DiagnosticAnchor,
    CommandLineHelpGroup HelpGroup,
    string? ValueLabel,
    string Description,
    ResourceLimitOption? Limit = null);

/// <summary>
/// The immutable specification Section 6.2 option catalog consumed by parsing, help, and tests.
/// </summary>
internal static class CommandLineOptions
{
    private const string OptionsAnchor = "§6.2";

    /// <summary>Every accepted option, in structured help order.</summary>
    internal static ImmutableArray<CommandLineOption> All { get; } =
    [
        new(
            "--input",
            "-i",
            CommandLineOptionArity.List,
            OptionsAnchor,
            CommandLineHelpGroup.Required,
            "path",
            "Ordered input file paths."),
        new(
            "--scheme",
            "-s",
            CommandLineOptionArity.List,
            OptionsAnchor,
            CommandLineHelpGroup.Required,
            "path",
            "Ordered scheme file paths."),
        new(
            "--output",
            "-o",
            CommandLineOptionArity.Single,
            OptionsAnchor,
            CommandLineHelpGroup.Common,
            "dir",
            "Output root directory. Default: current directory."),
        new(
            "--variables",
            "-v",
            CommandLineOptionArity.List,
            OptionsAnchor,
            CommandLineHelpGroup.Common,
            "entry",
            "Namespace entries applied after all input files."),
        new(
            "--verbosity",
            null,
            CommandLineOptionArity.Single,
            OptionsAnchor,
            CommandLineHelpGroup.Common,
            "level",
            "trace|debug|information|warning|error|critical|none. Default: information."),
        new(
            "--diagnostics-format",
            null,
            CommandLineOptionArity.Single,
            "§6.4.1",
            CommandLineHelpGroup.Common,
            "format",
            "text|json. Default: text. 'json' writes standard error as one canonical JSON "
                + "array and suppresses operational messages."),
        new(
            "--fail-on-warning",
            null,
            CommandLineOptionArity.Flag,
            OptionsAnchor,
            CommandLineHelpGroup.Common,
            null,
            "Exit 1 and publish nothing if any warning occurs. Diagnostic verbosity does not "
                + "alter this policy. Default: disabled."),
        new(
            "--help",
            null,
            CommandLineOptionArity.Informational,
            OptionsAnchor,
            CommandLineHelpGroup.Common,
            null,
            "Print this help and exit successfully."),
        new(
            "--version",
            null,
            CommandLineOptionArity.Informational,
            OptionsAnchor,
            CommandLineHelpGroup.Common,
            null,
            "Print version information and exit successfully."),
        Limit(
            "--max-input-bytes",
            ResourceLimitKind.Bytes,
            "Maximum bytes per input file.",
            limits => limits.MaxInputBytes),
        Limit(
            "--max-total-input-bytes",
            ResourceLimitKind.Bytes,
            "Maximum total input bytes.",
            limits => limits.MaxTotalInputBytes),
        Limit(
            "--max-depth",
            ResourceLimitKind.Count,
            "Maximum document or qualified-path depth.",
            limits => limits.MaxDepth),
        Limit(
            "--max-nodes",
            ResourceLimitKind.Count,
            "Maximum parsed nodes before generated entries.",
            limits => limits.MaxNodes),
        Limit(
            "--max-xml-attributes",
            ResourceLimitKind.Count,
            "Maximum attributes on one XML element.",
            limits => limits.MaxXmlAttributes),
        Limit(
            "--max-comments",
            ResourceLimitKind.Count,
            "Maximum retained comments.",
            limits => limits.MaxComments),
        Limit(
            "--max-comment-bytes",
            ResourceLimitKind.Bytes,
            "Maximum total decoded comment bytes.",
            limits => limits.MaxCommentBytes),
        Limit(
            "--max-wildcard-rules",
            ResourceLimitKind.Count,
            "Maximum wildcard rules.",
            limits => limits.MaxWildcardRules),
        Limit(
            "--max-wildcard-candidates",
            ResourceLimitKind.Count,
            "Maximum (rule,item) candidate checks.",
            limits => limits.MaxWildcardCandidates),
        Limit(
            "--max-generated",
            ResourceLimitKind.Count,
            "Maximum wildcard-generated nodes.",
            limits => limits.MaxGenerated),
        Limit(
            "--max-wildcard-iterations",
            ResourceLimitKind.Count,
            "Maximum fixed-point iterations.",
            limits => limits.MaxWildcardIterations),
        Limit(
            "--max-reference-depth",
            ResourceLimitKind.Count,
            "Maximum reference recursion depth.",
            limits => limits.MaxReferenceDepth),
        Limit(
            "--max-outputs",
            ResourceLimitKind.Count,
            "Maximum planned destination files.",
            limits => limits.MaxOutputs),
        Limit(
            "--max-total-output-bytes",
            ResourceLimitKind.Bytes,
            "Maximum total staged output bytes.",
            limits => limits.MaxTotalOutputBytes),
    ];

    /// <summary>Finds the descriptor for one canonical or short spelling.</summary>
    /// <param name="spelling">The option name before any inline value.</param>
    /// <returns>The matching descriptor, or <see langword="null"/>.</returns>
    internal static CommandLineOption? Find(string spelling) =>
        All.FirstOrDefault(option => option.Name == spelling || option.Alias == spelling);

    /// <summary>
    /// Finds the short alias at the start of a rejected inline short-option spelling.
    /// </summary>
    /// <param name="spelling">The rejected option-token spelling.</param>
    /// <returns>The declared alias, or <see langword="null"/>.</returns>
    internal static string? ShortAliasPrefixOf(string spelling) =>
        All
            .Select(option => option.Alias)
            .FirstOrDefault(alias => alias is not null
                && spelling.StartsWith(alias + "=", StringComparison.Ordinal));

    private static CommandLineOption Limit(
        string name,
        ResourceLimitKind kind,
        string description,
        Func<ResourceLimits, long> readValue) =>
        new(
            name,
            null,
            CommandLineOptionArity.Single,
            OptionsAnchor,
            CommandLineHelpGroup.Limits,
            kind == ResourceLimitKind.Bytes ? "bytes" : "count",
            description,
            new ResourceLimitOption(kind, readValue));
}
