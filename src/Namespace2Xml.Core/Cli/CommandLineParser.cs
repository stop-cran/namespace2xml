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
/// Value-bearing long options accept <c>--name=value</c>, and the parser resolves the inline form
/// once, before dispatching on the option name. Valueless operational flags reject every inline
/// value, including the empty one, while Section 6.1 informational options are recognized by
/// presence before argument validation.
/// </para>
/// <para>
/// Values are validated where they are accepted rather than after the whole vector has been read,
/// so the reported fault is the first one in command-line token order. Validating afterwards would
/// make the reported fault depend on the order a dictionary happened to enumerate.
/// </para>
/// </remarks>
public static class CommandLineParser
{
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

        CommandLineOption? current = null;
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
                var pending = current!;
                return Failure(pending.DiagnosticAnchor, token == "--"
                    ? $"'{pending.Name}' requires a value, but the next token is the end-of-options marker '--'."
                    : $"'{pending.Name}' requires a value, but the next token is the option '{token}'.");
            }

            if (!optionsEnded && token == "--")
            {
                // Section 6.2: every following token is a value of the immediately preceding
                // list-valued option, and there must be one.
                if (current is not { Arity: CommandLineOptionArity.List })
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

                var spec = CommandLineOptions.Find(name);
                if (spec is null)
                {
                    var alias = CommandLineOptions.ShortAliasPrefixOf(name);
                    return Failure("§6.2", alias is null
                        ? $"'{name}' is not a recognized option. Run 'namespace2xml --help' for "
                            + "the complete list."
                        : $"'{name}' is not a recognized option. A short option has no '=value' form; "
                            + $"write '{alias} <value>'.");
                }

                if (spec.Arity is CommandLineOptionArity.Informational or CommandLineOptionArity.Flag)
                {
                    if (spec.Arity == CommandLineOptionArity.Flag)
                    {
                        if (inline is not null)
                        {
                            return Failure(spec.DiagnosticAnchor,
                                $"'{spec.Name}' takes no value; write the option without '=...'.");
                        }

                        state.AcceptFlag(spec);
                    }

                    // Section 6.1 resolves informational options from presence alone, before any
                    // argument is validated, so their inline values are ignored if parsing is
                    // reached. Operational flags instead reject inline values above.
                    current = null;
                    currentSatisfied = true;
                    continue;
                }

                current = spec;
                currentSatisfied = spec.Arity == CommandLineOptionArity.List;

                if (inline is null)
                {
                    continue;
                }

                if (state.Accept(spec, inline) is { } inlineFault)
                {
                    return Failure(spec.DiagnosticAnchor, inlineFault);
                }

                currentSatisfied = true;

                // A single-valued option is complete; a list option stays current and keeps
                // consuming, exactly as though the inline value had been a separate token.
                if (spec.Arity == CommandLineOptionArity.Single)
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
                return Failure(current.DiagnosticAnchor, fault);
            }

            currentSatisfied = true;

            if (current.Arity == CommandLineOptionArity.Single)
            {
                current = null;
            }
        }

        if (!currentSatisfied)
        {
            return Failure(current!.DiagnosticAnchor,
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
        private bool failOnWarning;

        /// <summary>Accepts one valueless operational flag.</summary>
        public void AcceptFlag(CommandLineOption spec)
        {
            if (spec.Name != "--fail-on-warning")
            {
                throw new InvalidOperationException($"'{spec.Name}' is not an operational flag.");
            }

            failOnWarning = true;
        }

        /// <summary>Accepts one value, returning the fault message when it is not well formed.</summary>
        public string? Accept(CommandLineOption spec, string value)
        {
            switch (spec.Name)
            {
                case "--input":
                    return Path(spec, value, () => inputs.Add(value));

                case "--scheme":
                    return Path(spec, value, () => schemes.Add(value));

                case "--variables":
                    return Path(spec, value, () => variables.Add(value));

                case "--output":
                    return Path(spec, value, () => outputRoot = value);

                case "--verbosity":
                    return AcceptVerbosity(value);

                case "--diagnostics-format":
                    return AcceptDiagnosticsFormat(value);

                default:
                    if (spec.Limit is null)
                    {
                        throw new InvalidOperationException(
                            $"No value handler is declared for the option '{spec.Name}'.");
                    }

                    return AcceptLimit(spec.Name, value);
            }
        }

        /// <summary>
        /// Section 7.2: accepts a path-valued option, rejecting an empty token.
        /// </summary>
        /// <param name="spec">The option the value was given to.</param>
        /// <param name="value">The token supplied as the path.</param>
        /// <param name="accept">Records the path once it is known to be non-empty.</param>
        /// <returns>A fault message, or <see langword="null" /> when the value was accepted.</returns>
        /// <remarks>
        /// Section 7.2's warn-and-ignore behaviour is for "a path that does not exist", which
        /// presumes a path was named. The empty token names nothing, so it cannot be resolved,
        /// cannot be reported as missing, and cannot be distinguished from an option whose value
        /// the caller's own quoting dropped. It is rejected here, before any file access, so the
        /// failure carries the option that caused it.
        /// </remarks>
        private static string? Path(CommandLineOption spec, string value, Action accept)
        {
            if (value.Length == 0)
            {
                return $"'{spec.Name}' requires a path, and the value given is empty.";
            }

            accept();
            return null;
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
            limits,
            failOnWarning);

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
