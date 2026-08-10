namespace Namespace2Xml.Overlay;

/// <summary>
/// One native JSON or YAML mapping contribution at a node, retained so that Section 3.2's
/// <c>WARN010</c> can name the source that wrote it.
/// </summary>
/// <param name="Key">The contribution's Section 4.7 ordering key.</param>
/// <param name="Source">How diagnostics name the source document.</param>
/// <remarks>
/// <para>
/// Section 3.2 owes "exactly one compatibility warning for each source contribution, canonical
/// mapping path, and output instance where a JSON or YAML mapping inferred at step 11 remains
/// projected as a sequence". <em>Each source contribution</em> is a count over a set, and the rest
/// of <see cref="NodeMarks"/> is deliberately not a set: every other mark is the latest surviving
/// contribution of its kind, because Section 4.4 settles shape by recency and never needs to know
/// how many contributions agreed. This one does, so it is a set and nothing else is.
/// </para>
/// <para>
/// The source identity travels with the key rather than being looked up at emission. The pipeline
/// is a data flow, and the profile source stops at step 8: after the merge the overlay is one tree
/// with no memory of which documents built it. Carrying the name here is what makes the warning actionable — a user told only that "a numeric mapping became a sequence" has to find
/// which of their files wrote it, and that is the whole question.
/// </para>
/// <para>
/// XML is excluded at the reader rather than filtered here, because Section 22 scopes the code to
/// JSON and YAML. An XML element name cannot begin with a digit, so an XML document cannot supply
/// a canonically numeric mapping key in the first place and the exclusion costs nothing; it is
/// stated so that a later reader change cannot widen the code by accident.
/// </para>
/// </remarks>
public readonly record struct NativeMappingOrigin(StableOrderingKey Key, string Source);
