using System.Globalization;
using System.Text;
using Namespace2Xml.Contract;
using Namespace2Xml.Scheme;

namespace Namespace2Xml.Cli;

/// <summary>
/// Informational output. Written to standard output and never encoded as JSON
/// (specification Section 6.4.1), but laid out so an automated caller can parse it.
/// </summary>
internal static class HelpText
{
    private const int Width = 80;
    private const int OptionDescriptionColumn = 38;

    internal const string RepositoryUrl = "https://github.com/stop-cran/namespace2xml";
    internal static readonly string SpecificationUrl = DocumentUrl("docs/specification.md");
    internal static readonly string DiagnosticsUrl = DocumentUrl("docs/diagnostics.md");
    internal const string ReportUrl = RepositoryUrl + "/issues/new/choose";
    internal static readonly string AgentGuideUrl = DocumentUrl("AGENTS.md");
    internal static readonly string DocumentIndexUrl = DocumentUrl("llms.txt");
    internal static readonly string KnownLimitsUrl = DocumentUrl("KNOWN-LIMITS.md");
    internal static readonly string ReportingGuideUrl = DocumentUrl("CONTRIBUTING.md");

    /// <summary>
    /// A link to a document as it stood in the release being run, rather than on a branch.
    /// </summary>
    /// <remarks>
    /// <c>--version</c> reports <c>specification-sha256</c> so a report can name the contract it
    /// was filed against. A link to a moving branch defeats that: the reader follows it and gets
    /// whatever the specification says today, which may not be the bytes this binary implements
    /// or hashes to. Releases are tagged <c>v&lt;version&gt;</c> and the release workflow refuses
    /// a tag that disagrees with the built version, so this URL resolves to exactly those bytes.
    /// A build from an untagged working tree has no such tag and its links will not resolve; that
    /// build is not published, and pointing it at a branch instead would only hide the difference.
    /// </remarks>
    private static string DocumentUrl(string path) =>
        $"{RepositoryUrl}/blob/v{ContractBundle.ProductVersion}/{path}";

    /// <summary>
    /// The Section 16.1 output formats, laid out to sit inside the help text's indent.
    /// </summary>
    /// <remarks>
    /// Read from the same table the scheme compiler parses against rather than typed out here. The
    /// help text previously described the formats in English prose, and a reader took the
    /// hyphenated <c>quoted-namespace</c> for the token it names; the token is
    /// <c>quotednamespace</c>. Prose that has to agree with a parser eventually will not.
    /// </remarks>
    private static readonly string Formats = string.Join(", ", OutputFormats.Spellings);

    /// <summary>
    /// The Section 15 directive names other than <c>output</c>, which the help text introduces on
    /// its own, excluding the Section 15.3 deprecated aliases.
    /// </summary>
    private static readonly string Directives = Wrap(
        SchemeDirectives.Spellings.Where(name => name != "output"),
        "  ");

    /// <summary>
    /// Joins names into comma-separated lines that fit the help text's fixed width.
    /// </summary>
    /// <param name="names">The names to lay out.</param>
    /// <param name="indent">
    /// The leading whitespace every continuation line carries. The raw string literal below is
    /// dedented by the compiler and an interpolated value is not, so this is the indent as it
    /// appears in the rendered help rather than as it appears in this file.
    /// </param>
    private static string Wrap(IEnumerable<string> names, string indent)
    {
        var listed = names.ToList();
        var lines = new List<string>();
        var line = new StringBuilder(indent);

        for (var i = 0; i < listed.Count; i++)
        {
            // The separator travels with the name it follows, so a wrap can never leave a comma
            // stranded at the end of one line or start the next one with a bare name.
            var chunk = i == listed.Count - 1 ? listed[i] + "." : listed[i] + ",";

            if (line.Length > indent.Length && line.Length + 1 + chunk.Length > Width)
            {
                lines.Add(line.ToString());
                line.Clear().Append(indent);
            }

            if (line.Length > indent.Length)
            {
                line.Append(' ');
            }

            line.Append(chunk);
        }

        lines.Add(line.ToString());

        return string.Join(Environment.NewLine, lines).TrimStart();
    }

    /// <summary>Renders every cataloged option in one structured help row.</summary>
    private static string RenderOptionGroup(CommandLineHelpGroup group) =>
        string.Join(
            Environment.NewLine,
            CommandLineOptions.All
                .Where(option => option.HelpGroup == group)
                .Select(RenderOption));

    private static string RenderOption(CommandLineOption option)
    {
        var spelling = option.Alias is null
            ? option.Name
            : $"{option.Alias}, {option.Name}";

        if (option.ValueLabel is not null)
        {
            spelling += $" <{option.ValueLabel}>";

            if (option.Arity == CommandLineOptionArity.List)
            {
                spelling += "...";
            }
        }

        var description = option.Limit is null
            ? option.Description
            : $"{option.Description.TrimEnd('.')}. Default: "
                + $"{option.Limit.FormatValue(ResourceLimits.Defaults)}.";

        return RenderOptionColumns(spelling, description);
    }

    private static string RenderOptionColumns(string spelling, string description)
    {
        var left = "  " + spelling;
        var descriptions = WrapText(description, Width - OptionDescriptionColumn);
        var rendered = new StringBuilder();

        if (left.Length >= OptionDescriptionColumn)
        {
            rendered.AppendLine(left);
            rendered.Append(' ', OptionDescriptionColumn).Append(descriptions[0]);
        }
        else
        {
            rendered.Append(left.PadRight(OptionDescriptionColumn)).Append(descriptions[0]);
        }

        foreach (var continuation in descriptions.Skip(1))
        {
            rendered.AppendLine();
            rendered.Append(' ', OptionDescriptionColumn).Append(continuation);
        }

        return rendered.ToString();
    }

    private static List<string> WrapText(string text, int width)
    {
        var lines = new List<string>();
        var line = new StringBuilder();

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];

            // A human-readable IEC value is one unit of meaning. Moving the complete value to the
            // next line is clearer than leaving "256" at the end of one line and "MiB" at the
            // start of the next.
            if (index + 1 < words.Length
                && words[index + 1].TrimEnd('.') is "KiB" or "MiB" or "GiB" or "bytes"
                && word.All(scalar => char.IsAsciiDigit(scalar) || scalar == ','))
            {
                word += " " + words[++index];
            }

            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        lines.Add(line.ToString());
        return lines;
    }

    internal static string Render()
    {
        var requiredOptions = RenderOptionGroup(CommandLineHelpGroup.Required);
        var commonOptions = RenderOptionGroup(CommandLineHelpGroup.Common);
        var limitOptions = RenderOptionGroup(CommandLineHelpGroup.Limits);
        var maxDepthCeiling = LimitValue.MaxDepthCeiling.ToString("N0", CultureInfo.InvariantCulture);

        return $"""
        namespace2xml - deterministic configuration transformer.

        Reads ordered namespace profiles, JSON, YAML and XML inputs, applies scheme
        directives, and renders namespace, shell-quoted namespace, JSON, YAML, XML and
        INI outputs. Identical inputs always produce byte-identical outputs.

        USAGE
          namespace2xml -i <input files> -s <scheme files> [options]

        REQUIRED
        {requiredOptions}

        COMMON
        {commonOptions}

        SCHEME BASICS
          A scheme file is a namespace profile whose last name part is a directive. Every
          run needs an 'output' declaration, which selects the formats a subtree renders
          as:

            app.output=json,yaml

          'app' there is a path in the data, not a file name. See SELECTORS NAME THE DATA
          below: getting this one wrong drops data at exit 0.

          Formats: {Formats}. Names are
          case-insensitive; 'ignore' must appear alone and suppresses output entirely.

          The other directives are
          {Directives}
          Each refuses an unrecognized value by naming the values it accepts, so the error
          text is the reference. The document index below carries one guide per format.

        LIMITS
          Every resource bound is a --max-* option. See the specification, section 6.2.
          Count and depth values use [1-9][0-9]*. Byte values use [1-9][0-9]* with
          an optional case-insensitive KiB, MiB or GiB suffix. Effective defaults:

        {limitOptions}

          Section 6.2 lets a build document a hard safety ceiling and reject a larger
          value as CLI001. This build imposes one: the --max-depth ceiling is
          {maxDepthCeiling}, because several phases walk the document tree by recursion.
          Refusing a depth this build cannot walk keeps a too-deep request a readable
          error rather than a process crash carrying no diagnostic at all.

        SELECTORS NAME THE DATA, NOT THE FILE
          A selector is a path in the model. An input file's name never becomes part of
          the namespace. Mapping members and XML roots expose names; root sequences expose
          decimal positions. A base.json whose top-level keys are 'server' and 'logging'
          gives the paths server.host, server.port and logging.level. There is no 'base',
          and the scheme is 'server.output=json' whatever the file happens to be called.

          Getting this wrong is quiet. On its own, 'base.output=json' warns that 'base'
          selects nothing (WARN009). But add an override on the same wrong path — say
          'base.server.host=prod' — and the selector now matches the override's own nodes,
          the warning stops, and the run writes a well-formed file assembled from the
          overrides alone. Every key the input supplied and no override named is gone, at
          exit 0. The warning cannot reach this case, because writing the override is what
          silences it.

          To see the real paths when XML formatting whitespace is not data, render the
          inputs with wildcards before writing anything:

            xmlinputoptions=NormalizeFormattingWhitespace
            *.output=namespace
            *.root=*

          That writes one file per matched top-level part, each holding fully qualified
          paths in the exact form an override is written in. Root sequences use positions
          such as 0 and 1. A bare JSON or YAML root scalar has no part for '*' to match:
          it emits two WARN009 occurrences and WARN008, exits 0, and writes no file.

          The first line is inert for JSON, YAML and properties input. On XML a human
          formatted, it lets the render run when that formatting whitespace is disposable
          — see the next section. It takes no selector, because inputs are parsed before
          output instances exist: '*.xmlinputoptions=...' is a blocking SCHEME001. Keep
          normalization in the real scheme only when that is the same trade you intend.

          When XML whitespace is data, keep the default PreserveWhitespace mode and use:

            *.namespaceoutputoptions=AllowTrailingWhitespace
            *.output=namespace
            *.root=*

          This emits WARN013 for trailing-space values and exposes content positions such
          as server.#1.host. Copy those exact paths and keep preservation in the real run.
          Discovery and production must use the same input mode. See
          docs/usage-methodology.md.

        READING XML THAT WAS FORMATTED FOR HUMANS
          Indented XML holds whitespace-only text between element children, and the default
          'xmlinputoptions=PreserveWhitespace' keeps every text node. Those become content
          components, so this input

            <r>
              <b>1</b>
            </r>

          is the model r.#0, r.#1.b, r.#2 — and NOT r.b. Nothing warns about this: an
          override written r.b=2 is a new node beside r.#1.b rather than a replacement of
          it, and the run still exits 0.

          When the input was formatted for a human to read and that whitespace is not data,
          ask for the compatibility mode:

            xmlinputoptions=NormalizeFormattingWhitespace

          It discards whitespace-only text between element children, which makes those
          elements addressable by name. It warns once per document (WARN007) because
          section 11.7 says discarding that text weakens the same-format round-trip
          guarantee. If whitespace is data, keep PreserveWhitespace and use the
          preservation-aware discovery recipe above. See docs/format-xml.md and
          docs/usage-methodology.md. Because WARN007 is a warning, --fail-on-warning
          refuses publication when normalization is enabled.

        EXIT CODES
          0  Success, including success with warnings unless --fail-on-warning is set.
          1  Invalid CLI, input, scheme, reference, rendering, path or publication failure;
             also a completed render that emitted any warning under --fail-on-warning.

        FOR AUTOMATION AND AI AGENTS
          This tool is specified before it is implemented. The specification is the single
          source of truth for every behaviour, and every diagnostic carries a stable code
          plus the specification anchor it enforces, so a disagreement can be reported
          precisely rather than described. Run with --diagnostics-format json for
          machine-readable diagnostics. For automation that must reject every warning, add
          --fail-on-warning: the run still completes serialization and reports the original
          diagnostic stream, but exits 1 without publishing any file. Lowering verbosity
          cannot bypass the policy.

          Every link below is pinned to this release, so it describes this binary rather
          than a later branch. The document index lists these and the rest, including one
          guide per format. The diagnostic codes page lists every error and warning this
          build can emit, each with the clause it enforces.

          Document index    {DocumentIndexUrl}
          Specification     {SpecificationUrl}
          Diagnostic codes  {DiagnosticsUrl}
          Known limits      {KnownLimitsUrl}
          Reporting guide   {ReportingGuideUrl}
          Agent guide       {AgentGuideUrl}
          Report a defect   {ReportUrl}

          Read the known limits before reporting: a gap documented there is known, and the
          entry says whether this build has it. The reporting guide routes a finding to the
          right form — code defect, specification ambiguity, usage gap, or feature request —
          and misrouting is the main way a real finding gets lost. When reporting, include
          the contract-bundle revision printed by --version.

        """.ReplaceLineEndings("\n");
    }

    internal static string RenderVersion() =>
        // One "<field>: <value>" line per field, so a script can read it without a parser.
        // Section 6.4.1 fixes a minimum of 'version' and 'contract-bundle'; the rest exist so
        // that a caller holding only this output can reach the contract and the report form
        // without first guessing a URL.
        $"""
        name: namespace2xml
        version: {ContractBundle.ProductVersion}
        contract-bundle: {ContractBundle.Current.Revision}
        specification-sha256: {ContractBundle.Current.SpecificationSha256}
        registry-sha256: {ContractBundle.Current.RegistrySha256}
        specification: {SpecificationUrl}
        diagnostics: {DiagnosticsUrl}
        known-limits: {KnownLimitsUrl}
        documentation: {DocumentIndexUrl}
        repository: {RepositoryUrl}
        report: {ReportUrl}

        """.ReplaceLineEndings("\n");
}
