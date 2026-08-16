using Namespace2Xml.Diagnostics;

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

/// <summary>What a Section 6.2 threshold admits.</summary>
public static class VerbosityThreshold
{
    /// <summary>
    /// Whether a diagnostic of the given severity is written at the given threshold.
    /// </summary>
    /// <param name="verbosity">The selected threshold.</param>
    /// <param name="severity">The severity of the diagnostic.</param>
    /// <returns><see langword="true"/> when the diagnostic is written.</returns>
    /// <remarks>
    /// <para>
    /// Section 6.2 lists what each threshold shows. Levels 1 and 2 show "all diagnostics";
    /// <c>information</c> shows "information, warning, error, and critical"; <c>warning</c> drops
    /// information; <c>error</c> drops warnings; <c>critical</c> shows "critical host/runtime
    /// failures only"; <c>none</c> shows nothing.
    /// </para>
    /// <para>
    /// Section 22 gives the registry two severities, <c>warning</c> and <c>error</c>, and no
    /// diagnostic carries <c>critical</c> — that level names host and runtime failures, which
    /// Section 22 places outside the registry. So <c>critical</c> admits no diagnostic at all,
    /// and the first four thresholds are indistinguishable over the severities that exist. Both
    /// facts are consequences of the two lists rather than choices made here, and both would
    /// change on their own if a third severity were ever added.
    /// </para>
    /// <para>
    /// Section 6.4.3 says <c>--verbosity</c> "filters array elements by severity exactly as it
    /// filters text lines", so this single function serves both encodings.
    /// </para>
    /// </remarks>
    public static bool Admits(this Verbosity verbosity, DiagnosticSeverity severity) =>
        verbosity switch
        {
            Verbosity.Trace or Verbosity.Debug or Verbosity.Information => true,
            Verbosity.Warning => severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error,
            Verbosity.Error => severity is DiagnosticSeverity.Error,
            _ => false,
        };

    /// <summary>
    /// Whether an operational message at the given level is written at the given threshold.
    /// </summary>
    /// <param name="verbosity">The selected threshold.</param>
    /// <param name="level">The level of the operational message.</param>
    /// <returns><see langword="true"/> when the message is written.</returns>
    /// <remarks>
    /// Operational messages are not diagnostics and are outside the Section 22 registry, so they
    /// are gated by their own level against the same ordered scale. Section 6.4.3 suppresses them
    /// entirely under <c>json</c> regardless of what this returns; that decision belongs to the
    /// encoding rather than to the threshold.
    /// </remarks>
    public static bool Admits(this Verbosity verbosity, OperationalLevel level) =>
        verbosity switch
        {
            Verbosity.Trace => true,
            Verbosity.Debug => level is OperationalLevel.Debug or OperationalLevel.Information,
            Verbosity.Information => level is OperationalLevel.Information,
            _ => false,
        };
}
