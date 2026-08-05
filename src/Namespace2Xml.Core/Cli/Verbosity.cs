namespace Namespace2Xml.Cli;

/// <summary>
/// Output threshold of specification Section 6.2, ordered from most to least verbose.
/// </summary>
/// <remarks>
/// Verbosity filters what is written. It never changes exit codes, output files, resource
/// accounting, or the deterministic order in which diagnostics are produced: a warning hidden by
/// the threshold still occurred and still affected processing.
/// </remarks>
public enum Verbosity
{
    /// <summary>All diagnostics plus per-file parsing, wildcard, reference and publication detail.</summary>
    Trace,

    /// <summary>All diagnostics plus phase progress, merge decisions, counters and plan summaries.</summary>
    Debug,

    /// <summary>Information, warning, error and critical messages. The default.</summary>
    Information,

    /// <summary>Warning, error and critical messages.</summary>
    Warning,

    /// <summary>Error and critical messages.</summary>
    Error,

    /// <summary>Critical host or runtime failures only.</summary>
    Critical,

    /// <summary>
    /// No diagnostic or operational log output. Under <c>--diagnostics-format json</c> the empty
    /// array container is still written, per Section 6.4.3.
    /// </summary>
    None,
}
