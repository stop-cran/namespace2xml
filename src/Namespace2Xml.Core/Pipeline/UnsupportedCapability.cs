namespace Namespace2Xml.Pipeline;

/// <summary>
/// A specification capability the invocation needs and this build does not have.
/// </summary>
/// <remarks>
/// <para>
/// This is scaffolding with a stated removal condition: when every Section 15.1 step is fully
/// implemented, no step can produce one of these and the type is deleted. Until then it exists so
/// that an unimplemented step <em>declines</em> rather than passes its input through unchanged.
/// </para>
/// <para>
/// Passing through is the failure mode worth spending a type to avoid. A step 11 that returns its
/// input unmodified turns <c>a.0=x</c> plus <c>a.5=y</c> into keys <c>a.0</c> and <c>a.5</c>, which
/// is a plausible file, a wrong file, and indistinguishable from a correct one without reading
/// Section 8.7. Declining produces no file and a message naming the clause.
/// </para>
/// <para>
/// A missing capability is not a diagnostic. Section 22 fixes the code set, and Section 6.3 fixes
/// <c>0</c> and <c>1</c> as the outcomes of a run that happened; a run that declined is neither, so
/// it reports the reserved preview exit code instead of claiming either.
/// </para>
/// </remarks>
/// <param name="Capability">Short name of the missing capability, for the operational message.</param>
/// <param name="Detail">What in the invocation needs it.</param>
/// <param name="Spec">The specification anchor that defines the capability, for example <c>§8.7</c>.</param>
public sealed record UnsupportedCapability(string Capability, string Detail, string Spec)
{
    /// <summary>The operational message a caller sees.</summary>
    public override string ToString() =>
        $"{Capability} ({Spec}) is not implemented in this preview: {Detail}";
}
