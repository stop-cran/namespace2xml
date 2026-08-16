using System.Collections.Immutable;

namespace Namespace2Xml.Diagnostics;

/// <summary>
/// The sanctioned way to produce a diagnostic. One factory per Section 22 code lives in the
/// generated half of this class; this half holds what the generator does not need to repeat.
/// </summary>
/// <remarks>
/// <para>
/// Emitting through a per-code factory rather than through <see cref="Diagnostic"/> directly buys
/// three invariants at compile time rather than at review time:
/// </para>
/// <list type="bullet">
/// <item>
/// severity cannot be chosen at the call site, because the factory does not accept one; the registry
/// declares itself authoritative for severity and this is what that means mechanically;
/// </item>
/// <item>
/// a code cannot carry a member outside its registry field set, because the factory has no parameter
/// for it — passing a <c>rule</c> to <c>TYPE001</c> does not compile;
/// </item>
/// <item>
/// a code cannot silently lose its cardinality scope, because every factory whose registry phrase is
/// narrower than the invocation demands a key.
/// </item>
/// </list>
/// <para>
/// What the factories deliberately do not constrain is <c>phase</c>. The registry lists itself as not
/// authoritative for phase, and Section 6.4.3 states phase is a property of the occurrence rather
/// than of the code, so restricting it here would encode an invention rather than the contract.
/// </para>
/// </remarks>
public static partial class DiagnosticCodes
{
    /// <summary>
    /// Cardinality key for codes the registry scopes to the whole invocation. Every occurrence of
    /// such a code shares one key, so a second occurrence is a duplicate by construction.
    /// </summary>
    public const string Invocation = "";

    private static DiagnosticOccurrence Create(
        string code,
        DiagnosticSeverity severity,
        DiagnosticPhase phase,
        string spec,
        string message,
        string cardinalityKey,
        string? source = null,
        int? line = null,
        int? column = null,
        string? path = null,
        string? declaration = null,
        ImmutableArray<string> rule = default,
        string? destination = null)
    {
        ArgumentNullException.ThrowIfNull(cardinalityKey);

        return new DiagnosticOccurrence(
            new Diagnostic(code, severity, phase, spec, message, source, line, column, path, declaration, rule, destination),
            cardinalityKey);
    }
}
