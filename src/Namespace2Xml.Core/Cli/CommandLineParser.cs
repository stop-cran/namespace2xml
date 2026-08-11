using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;

namespace Namespace2Xml.Cli;

/// <summary>
/// Hand-written parser for the Section 6.2 option-token grammar.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than delegated to a library because Section 6.2 fixes behaviour no general
/// parser offers: values concatenate across repeated occurrences in exact token order, a bare
/// <c>--</c> hands every following token to the immediately preceding list option, and a malformed
/// limit value has to surface as <c>CLI001</c> rather than as the library's own message on its own
/// exit path. Version 2.4.0 delegated, and its unknown-option failure carried no stable code and no
/// machine-readable stream.
/// </para>
/// <para>
/// The grammar is uniform: every long option accepts <c>--name=value</c>, and the parser resolves
/// the inline form once, before dispatching on the option name, so no option can implement it
/// differently from the rest.
/// </para>
/// <para>
/// Values are validated where they are accepted rather than after the whole vector has been read,
/// so the reported fault is the first one in command-line token order. Validating afterwards would
/// make the reported fault depend on the order a dictionary happened to enumerate.
/// </para>
/// </remarks>
public static class CommandLineParser
{
    private enum Arity
    {
        /// <summary>Takes no value at all.</summary>
        None,

        /// <summary>Accepts values until the next option token, concatenating across occurrences.</summary>
        List,

        /// <summary>Accepts exactly one value; a later occurrence overrides an earlier one.</summary>
        Single,
    }

    private sealed record OptionSpec(string Name, string? Alias, Arity Arity, string Anchor = "§6.2");

    private static readonly ImmutableArray<OptionSpec> Options =
    [
        new("--input", "-i", Arity.List),
        new("--scheme", "-s", Arity.List),
        new("--variables", "-v", Arity.List),
        new("--output", "-o", Arity.Single),
        new("--verbosity", null, Arity.Single),

        // Section 6.4.1 states this option's validation itself, so its faults anchor there rather
        // than at the general grammar.
        new("--diagnostics-format", null, Arity.Single, "§6.4.1"),
        new("--max-input-bytes", null, Arity.Single),
        new("--max-total-input-bytes", null, Arity.Single),
        new("--max-depth", null, Arity.Single),
        new("--max-nodes", null, Arity.Single),
        new("--max-xml-attributes", null, Arity.Single),
        new("--max-comments", null, Arity.Single),
        new("--max-comment-bytes", null, Arity.Single),
        new("--max-wildcard-rules", null, Arity.Single),
        new("--max-wildcard-candidates", null, Arity.Single),
        new("--max-generated", null, Arity.Single),
        new("--max-wildcard-iterations", null, Arity.Single),
        new("--max-reference-depth", null, Arity.Single),
        new("--max-outputs", null, Arity.Single),
        new("--max-total-output-bytes", null, Arity.Single),
        new("--help", null, Arity.None),
        new("--version", null, Arity.None),
    ];

    /// <summary>
    /// Parses an argument vector. Never throws for any vector, including one containing nulls.
    /// </summary>
    /// <param name="arguments">Raw argument vector, exactly as supplied by the host.</param>
    /// <returns>
    /// The parsed command line, or the single <c>CLI001</c> the vector earns. Section 22 scopes
    /// <c>CLI001</c> to once per invocation, so parsing stops at the first fault.
    /// </returns>
    public static CommandLineResult Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var state = new ParseState();

        OptionSpec? current = null;
        var currentSatisfied = true;
        var optionsEnded = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var token = arguments[index] ?? string.Empty;

            // An option still owed a value takes precedence over every other reading of the next
            // token, so that "--diagnostics-format --" is a missing value rather than a misplaced
            // end-of-options marker. The two are both CLI001, but they anchor at different clauses.
            if (!optionsEnded && !currentSatisfied && (token == "--" || IsOptionToken(token)))
            {
                return Failure(current!.Anchor, token == "--"
                    ? $"'{current.Name}' requires a value, but the next token is the end-of-options marker '--'."
                    : $"'{current.Name}' requires a value, but the next token is the option '{token}'.");
            }

            if (!optionsEnded && token == "--")
            {
                // Section 6.2: every following token is a value of the immediately preceding
                // list-valued option, and there must be one.
                if (current is not { Arity: Arity.List })
                {
                    return Failure("§6.2",
                        "'--' ends option recognition and hands every following token to the immediately "
                        + "preceding list-valued option, but no list-valued option precedes it here.");
                }

                optionsEnded = true;
                continue;
            }

            if (!optionsEnded && IsOptionToken(token))
            {
                // Only a long option splits at '='. A short-option token is a name in its
                // entirety, so "-i=a" names an option that does not exist rather than quietly
                // creating an input file called "a" or "=a".
                var isLong = token.StartsWith("--", StringComparison.Ordinal);
                var separator = isLong ? token.IndexOf('=', StringComparison.Ordinal) : -1;
                var name = separator < 0 ? token : token[..separator];
                string? inline = separator < 0 ? null : token[(separator + 1)..];

                var spec = Options.FirstOrDefault(option => option.Name == name || option.Alias == name);
                if (spec is null)
                {
                    var alias = ShortAliasPrefixOf(name);
                    return Failure("§6.2", alias is null
                        ? $"'{name}' is not a recognized option. Run 'namespace2xml --help' for "
                            + "the complete list."
                        : $"'{name}' is not a recognized option. A short option has no '=value' form; "
                            + $"write '{alias} <value>'.");
                }

                if (spec.Arity == Arity.None)
                {
                    // Section 6.1 resolves --help and --version from presence alone, before any
                    // argument is validated, so an inline value here is ignored rather than
                    // diagnosed. Reaching this branch means the caller bypassed that check.
                    current = null;
                    currentSatisfied = true;
                    continue;
                }

                current = spec;
                currentSatisfied = spec.Arity == Arity.List;

                if (inline is null)
                {
                    continue;
                }

                if (state.Accept(spec, inline) is { } inlineFault)
                {
                    return Failure(spec.Anchor, inlineFault);
                }

                currentSatisfied = true;

                // A single-valued option is complete; a list option stays current and keeps
                // consuming, exactly as though the inline value had been a separate token.
                if (spec.Arity == Arity.Single)
                {
                    current = null;
                }

                continue;
            }

            if (current is null)
            {
                return Failure("§6.2", $"'{token}' is not attached to any option.");
            }

            if (state.Accept(current, token) is { } fault)
            {
                return Failure(current.Anchor, fault);
            }

            currentSatisfied = true;

            if (current.Arity == Arity.Single)
            {
                current = null;
            }
        }

        if (!currentSatisfied)
        {
            return Failure(current!.Anchor,
                $"'{current.Name}' reaches the end of the command line still requiring a value.");
        }

        return state.MissingRequiredOption() is { } incomplete
            ? Failure("§6.2", incomplete)
            : new CommandLineResult(state.ToCommandLine());
    }

    /// <summary>
    /// Recognizes an option token: a token beginning with <c>-</c> other than <c>-</c> and
    /// <c>--</c>, which Section 6.2 makes an ordinary value and the end-of-options marker.
    /// </summary>
    private static bool IsOptionToken(string token) =>
        token.Length > 1 && token[0] == '-' && token != "--";

    /// <summary>
    /// The declared short alias a rejected token starts with, when the token has the shape
    /// <c>-x=…</c>, so the message can name the missing-inline-form rule instead of leaving the
    /// author to guess why a familiar-looking token was refused.
    /// </summary>
    private static string? ShortAliasPrefixOf(string token) =>
        Options
            .Select(option => option.Alias)
            .FirstOrDefault(alias => alias is not null
                && token.StartsWith(alias + "=", StringComparison.Ordinal));

    private static CommandLineResult Failure(string anchor, string message) =>
        new(DiagnosticCodes.Cli001(DiagnosticPhase.Cli, anchor, message).Diagnostic);

    /// <summary>Accumulates accepted values, validating each one where it is accepted.</summary>
    private sealed class ParseState
    {
        private readonly ImmutableArray<string>.Builder inputs = ImmutableArray.CreateBuilder<string>();
        private readonly ImmutableArray<string>.Builder schemes = ImmutableArray.CreateBuilder<string>();
        private readonly ImmutableArray<string>.Builder variables = ImmutableArray.CreateBuilder<string>();

        private string outputRoot = ".";
        private Verbosity verbosity = Verbosity.Information;
        private DiagnosticFormat diagnosticsFormat = DiagnosticFormat.Text;
        private ResourceLimits limits = ResourceLimits.Defaults;

        /// <summary>Accepts one value, returning the fault message when it is not well formed.</summary>
        public string? Accept(OptionSpec spec, string value)
        {
            switch (spec.Name)
            {
                case "--input":
                    inputs.Add(value);
                    return null;

                case "--scheme":
                    schemes.Add(value);
                    return null;

                case "--variables":
                    variables.Add(value);
                    return null;

                case "--output":
                    outputRoot = value;
                    return null;

                case "--verbosity":
                    return AcceptVerbosity(value);

                case "--diagnostics-format":
                    return AcceptDiagnosticsFormat(value);

                default:
                    return AcceptLimit(spec.Name, value);
            }
        }

        /// <summary>Reports what the completed vector still lacks, or <see langword="null"/>.</summary>
        public string? MissingRequiredOption() => (inputs.Count, schemes.Count) switch
        {
            (0, _) => "No input files. '-i' or '--input' is required.",
            (_, 0) => "No scheme files. '-s' or '--scheme' is required.",
            _ => null,
        };

        public CommandLine ToCommandLine() => new(
            inputs.ToImmutable(),
            schemes.ToImmutable(),
            variables.ToImmutable(),
            outputRoot,
            verbosity,
            diagnosticsFormat,
            limits);

        private string? AcceptVerbosity(string value)
        {
            foreach (var candidate in Enum.GetValues<Verbosity>())
            {
                if (Ascii.EqualsIgnoreCase(value, candidate.ToString()))
                {
                    verbosity = candidate;
                    return null;
                }
            }

            return "'--verbosity' accepts trace, debug, information, warning, error, critical or none, "
                + $"case-insensitively, but got '{value}'.";
        }

        private string? AcceptDiagnosticsFormat(string value)
        {
            if (Ascii.EqualsIgnoreCase(value, "text"))
            {
                diagnosticsFormat = DiagnosticFormat.Text;
                return null;
            }

            if (Ascii.EqualsIgnoreCase(value, "json"))
            {
                diagnosticsFormat = DiagnosticFormat.Json;
                return null;
            }

            return $"'--diagnostics-format' accepts 'text' or 'json', case-insensitively, but got '{value}'.";
        }

        private string? AcceptLimit(string option, string value)
        {
            switch (option)
            {
                case "--max-input-bytes":
                    return LimitValue.TryParseBytes(value, out var maxInputBytes)
                        ? Set(current => current with { MaxInputBytes = maxInputBytes })
                        : ByteFault(option, value);

                case "--max-total-input-bytes":
                    return LimitValue.TryParseBytes(value, out var maxTotalInputBytes)
                        ? Set(current => current with { MaxTotalInputBytes = maxTotalInputBytes })
                        : ByteFault(option, value);

                case "--max-comment-bytes":
                    return LimitValue.TryParseBytes(value, out var maxCommentBytes)
                        ? Set(current => current with { MaxCommentBytes = maxCommentBytes })
                        : ByteFault(option, value);

                case "--max-total-output-bytes":
                    return LimitValue.TryParseBytes(value, out var maxTotalOutputBytes)
                        ? Set(current => current with { MaxTotalOutputBytes = maxTotalOutputBytes })
                        : ByteFault(option, value);

                case "--max-depth":
                    if (!LimitValue.TryParseCount(value, out var maxDepth))
                    {
                        return CountFault(option, value);
                    }

                    // Section 6.2: "a value exceeding an implementation's documented hard safety
                    // ceiling is CLI001". Accepting a depth the pipeline cannot walk would trade
                    // this readable refusal for a stack overflow, which Section 6.3 does not
                    // define and which cannot carry a diagnostic.
                    return maxDepth > LimitValue.MaxDepthCeiling
                        ? $"'{option}' accepts at most {LimitValue.MaxDepthCeiling}, this build's "
                            + $"documented hard safety ceiling, but got '{value}'."
                        : Set(current => current with { MaxDepth = maxDepth });

                case "--max-nodes":
                    return LimitValue.TryParseCount(value, out var maxNodes)
                        ? Set(current => current with { MaxNodes = maxNodes })
                        : CountFault(option, value);

                case "--max-xml-attributes":
                    return LimitValue.TryParseCount(value, out var maxXmlAttributes)
                        ? Set(current => current with { MaxXmlAttributes = maxXmlAttributes })
                        : CountFault(option, value);

                case "--max-comments":
                    return LimitValue.TryParseCount(value, out var maxComments)
                        ? Set(current => current with { MaxComments = maxComments })
                        : CountFault(option, value);

                case "--max-wildcard-rules":
                    return LimitValue.TryParseCount(value, out var maxWildcardRules)
                        ? Set(current => current with { MaxWildcardRules = maxWildcardRules })
                        : CountFault(option, value);

                case "--max-wildcard-candidates":
                    return LimitValue.TryParseCount(value, out var maxWildcardCandidates)
                        ? Set(current => current with { MaxWildcardCandidates = maxWildcardCandidates })
                        : CountFault(option, value);

                case "--max-generated":
                    return LimitValue.TryParseCount(value, out var maxGenerated)
                        ? Set(current => current with { MaxGenerated = maxGenerated })
                        : CountFault(option, value);

                case "--max-wildcard-iterations":
                    return LimitValue.TryParseCount(value, out var maxWildcardIterations)
                        ? Set(current => current with { MaxWildcardIterations = maxWildcardIterations })
                        : CountFault(option, value);

                case "--max-reference-depth":
                    return LimitValue.TryParseCount(value, out var maxReferenceDepth)
                        ? Set(current => current with { MaxReferenceDepth = maxReferenceDepth })
                        : CountFault(option, value);

                case "--max-outputs":
                    return LimitValue.TryParseCount(value, out var maxOutputs)
                        ? Set(current => current with { MaxOutputs = maxOutputs })
                        : CountFault(option, value);

                default:
                    throw new InvalidOperationException($"No handler is declared for the option '{option}'.");
            }

            static string CountFault(string option, string value) =>
                $"'{option}' accepts a decimal count matching [1-9][0-9]*, but got '{value}'.";

            static string ByteFault(string option, string value) =>
                $"'{option}' accepts a decimal byte count matching [1-9][0-9]* with an optional KiB, MiB "
                + $"or GiB suffix, but got '{value}'.";
        }

        private string? Set(Func<ResourceLimits, ResourceLimits> update)
        {
            limits = update(limits);
            return null;
        }
    }
}
