namespace Namespace2Xml.Diagnostics;

/// <summary>
/// One diagnostic together with the key identifying the thing it is "once per".
/// </summary>
/// <remarks>
/// <para>
/// The key is emission-side bookkeeping and never reaches the stream: specification Section 6.4.3
/// closes the element's member set, so a cardinality key could not be carried there without a
/// specification change.
/// </para>
/// <para>
/// It exists because cardinality is a correctness property that nothing else can check. Section 22
/// says <c>REFERENCE002</c> occurs "once per reachable owning value"; an implementation that emits
/// it once per <em>reference</em> produces a diagnostic stream that is wrong in a way no schema
/// validator can see, and that differs between runs whenever traversal order differs. Making the
/// key explicit lets a single emitter enforce at-most-one-per-key deterministically, and lets a
/// reviewer compare the key expression against the registry phrase reproduced on the factory.
/// </para>
/// </remarks>
/// <param name="Diagnostic">The diagnostic occurrence.</param>
/// <param name="CardinalityKey">
/// Identity of the thing this diagnostic is emitted once per. <see cref="DiagnosticCodes.Invocation"/>
/// for codes the registry scopes to the whole invocation.
/// </param>
public sealed record DiagnosticOccurrence(Diagnostic Diagnostic, string CardinalityKey);
