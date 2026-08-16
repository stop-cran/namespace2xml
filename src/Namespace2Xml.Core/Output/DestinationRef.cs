namespace Namespace2Xml.Output;

/// <summary>
/// The destination a diagnostic names, together with the specification Section 21.3 index that
/// orders it.
/// </summary>
/// <remarks>
/// <para>
/// One value rather than two parameters, because Section 24 needs both or neither. Its second
/// diagnostic group is "diagnostics carrying a destination but no source ordering key, in the
/// Section 21.3 destination order", so a component that knows the destination and not its index
/// cannot emit a correctly ordered diagnostic about it — and will emit a plausible one anyway,
/// since a stream with a single failing destination looks the same either way.
/// </para>
/// <para>
/// Every component that diagnoses against a destination therefore holds this, nullable as a whole:
/// the two members cannot drift, and a component that legitimately has no destination — a
/// projection run for a test, or the output root's own directory creation — carries no index
/// either, which is the third group.
/// </para>
/// </remarks>
/// <param name="Canonical">The destination's canonical relative path, as Section 21.1 spells it.</param>
/// <param name="Order">The destination's index in Section 21.3 publication order.</param>
public readonly record struct DestinationRef(string Canonical, int Order);
