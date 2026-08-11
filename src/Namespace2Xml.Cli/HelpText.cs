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
          Section 6.2 lets a build document a hard safety ceiling and reject a larger value
          as CLI001. This build imposes one: --max-depth accepts at most 4096, because
          several phases walk the document tree by recursion. Refusing a depth this build
          cannot walk is what keeps a too-deep request a readable error rather than a
          process crash carrying no diagnostic at all.

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

          When the input was formatted for a human to read, ask for the compatibility mode:

            xmlinputoptions=NormalizeFormattingWhitespace

          It discards whitespace-only text between element children, which makes those
          elements addressable by name. It warns once per document (WARN007) because
          section 11.7 says discarding that text weakens the same-format round-trip
          guarantee. See docs/format-xml.md and docs/usage-methodology.md.

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
