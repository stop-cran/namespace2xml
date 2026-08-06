using Namespace2Xml.Contract;

namespace Namespace2Xml.Cli;

/// <summary>
/// Informational output. Written to standard output and never encoded as JSON
/// (specification Section 6.4.1), but laid out so an automated caller can parse it.
/// </summary>
internal static class HelpText
{
    internal const string RepositoryUrl = "https://github.com/stop-cran/namespace2xml";
    internal static readonly string SpecificationUrl = DocumentUrl("docs/specification.md");
    internal static readonly string DiagnosticsUrl = DocumentUrl("docs/diagnostics.md");
    internal const string ReportUrl = RepositoryUrl + "/issues/new/choose";
    internal static readonly string AgentGuideUrl = DocumentUrl("AGENTS.md");

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
          70 Preview only: this build has not implemented the requested work. Nothing was
             written and nothing about your input was judged. Not a failure of your
             configuration. Released builds return only 0 or 1.

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
