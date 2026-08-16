using System.Collections.Immutable;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Output;

/// <summary>
/// The two identities a flat-output diagnostic needs: the Appendix A spelling of a path, and the
/// cardinality slot a code scoped to a path or key "and output instance" occupies.
/// </summary>
internal static class FlatIdentity
{
    /// <summary>
    /// The Appendix A.2 spelling of a path for a diagnostic's <c>path</c> member, or
    /// <see langword="null"/> at the view root, which has no spelling.
    /// </summary>
    /// <remarks>
    /// The default delimiter is used rather than the configured one, so that the same path is named
    /// the same way in every diagnostic regardless of which output instance reported it. When the
    /// name has no Section 19.1 spelling, <see cref="CanonicalPath"/>'s approximation stands in: it
    /// writes the unspellable scalars as <c>\u{HEX}</c>, which is total and injective, so it still
    /// distinguishes the path from every other. A cardinality key needs exactly that — two paths
    /// that spell the same occupy one slot, and the second diagnostic is dropped as a duplicate.
    /// </remarks>
    public static string? PathText(ImmutableArray<NamePart> path) => CanonicalPath.Of(path);

    /// <summary>Combines a destination with a path or projected key into one cardinality slot.</summary>
    /// <param name="destination">The output instance, or <see langword="null"/> when there is none.</param>
    /// <param name="identity">The path or projected key.</param>
    /// <remarks>
    /// NUL separates the two because both halves are free text. A punctuation separator that either
    /// half may contain would let one destination's key collide with another's, and a suppressed
    /// diagnostic is exactly the failure the buffer's cardinality enforcement exists to prevent.
    /// </remarks>
    public static string Key(string? destination, string? identity) =>
        $"{destination}\u0000{identity}";
}
