using Namespace2Xml.Contract;

namespace Namespace2Xml.Cli;

/// <summary>
/// Informational output. Written to standard output and never encoded as JSON
/// (specification Section 6.4.1), but laid out so an automated caller can parse it.
/// </summary>
internal static class HelpText
{
    internal const string RepositoryUrl = "https://github.com/stop-cran/namespace2xml";
    internal const string SpecificationUrl = RepositoryUrl + "/blob/master/docs/specification.md";
    internal const string DiagnosticsUrl = RepositoryUrl + "/blob/master/docs/diagnostics.md";
    internal const string ReportUrl = RepositoryUrl + "/issues/new/choose";
    internal const string AgentGuideUrl = RepositoryUrl + "/blob/master/AGENTS.md";

    internal static string Render() =>
        $"""
        namespace2xml - deterministic configuration transformer.

        Reads ordered namespace profiles, JSON, YAML and XML inputs, applies scheme
        directives, and renders namespace, quoted-namespace, JSON, YAML, XML and INI
        outputs. Identical inputs always produce byte-identical outputs.

        USAGE
          namespace2xml -i <input files> -s <scheme files> [options]

        REQUIRED
          -i, --input <path>...        Ordered input file paths.
          -s, --scheme <path>...       Ordered scheme file paths.

        COMMON
          -o, --output <dir>           Output root directory. Default: current directory.
          -v, --variables <entry>...   Namespace entries applied after all input files.
              --verbosity <level>      trace|debug|information|warning|error|critical|none.
                                       Default: information.
              --diagnostics-format <f> text|json. Default: text. 'json' writes the whole
                                       diagnostic stream to standard error as one canonical
                                       JSON array and suppresses operational messages.
              --help                   Print this help and exit successfully.
              --version                Print version information and exit successfully.

        LIMITS
          Every resource bound is a --max-* option. See the specification, section 6.2.

        EXIT CODES
          0  Success, including success with warnings.
          1  Invalid CLI, input, scheme, reference, rendering, path or publication failure.

        FOR AUTOMATION AND AI AGENTS
          This tool is specified before it is implemented. The specification is the single
          source of truth for every behaviour, and every diagnostic carries a stable code
          plus the specification anchor it enforces, so a disagreement can be reported
          precisely rather than described. Run with --diagnostics-format json for
          machine-readable diagnostics.

          Specification     {SpecificationUrl}
          Diagnostic codes  {DiagnosticsUrl}
          Agent guide       {AgentGuideUrl}
          Report a defect   {ReportUrl}

          When reporting, include the contract-bundle revision printed by --version.

        """.ReplaceLineEndings("\n");

    internal static string RenderVersion() =>
        // One "<field>: <value>" line per field, so a script can read it without a parser.
        $"""
        name: namespace2xml
        version: {ContractBundle.ProductVersion}
        contract-bundle: {ContractBundle.Current.Revision}
        specification-sha256: {ContractBundle.Current.SpecificationSha256}
        registry-sha256: {ContractBundle.Current.RegistrySha256}
        specification: {SpecificationUrl}
        repository: {RepositoryUrl}
        report: {ReportUrl}

        """.ReplaceLineEndings("\n");
}
