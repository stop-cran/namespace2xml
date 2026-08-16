using System.Collections.Immutable;

namespace Namespace2Xml.Diagnostics;

/// <summary>
/// Registry facts about one diagnostic code, projected from <c>spec/diagnostics.registry.json</c>.
/// The registry declares itself authoritative for <c>code</c>, <c>severity</c>, <c>cardinality</c>
/// and <c>fields</c>; those four are therefore reproduced here rather than restated at call sites.
/// </summary>
/// <param name="Code">Stable diagnostic code, for example <c>TYPE001</c>.</param>
/// <param name="Severity">Severity fixed by the Section 22 registry.</param>
/// <param name="Cardinality">
/// The registry's cardinality phrase, verbatim. Deliberately prose: Section 22 spells cardinality in
/// twenty-four distinct phrasings ("once per rule", "once per reachable owning value", "once per
/// projected key and output instance"), and only the emitting code knows what the phrase denotes.
/// The emitter supplies the corresponding key; this string is what a reviewer checks it against.
/// </param>
/// <param name="Condition">The registry's condition column, verbatim.</param>
/// <param name="Fields">
/// The members this code may carry, beyond the five required of every element. A permitted set, not
/// a mandatory one: Section 22 requires each member "where applicable", and Section 6.4.3 adds that
/// the optional members "are present exactly when the condition supplies them".
/// </param>
public sealed record DiagnosticCodeInfo(
    string Code,
    DiagnosticSeverity Severity,
    string Cardinality,
    string Condition,
    ImmutableArray<string> Fields);
