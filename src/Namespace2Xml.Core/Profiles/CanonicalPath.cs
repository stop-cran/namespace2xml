using System.Collections.Immutable;

namespace Namespace2Xml.Profiles;

/// <summary>
/// Spells a qualified name the one way the specification recognizes outside a namespace record.
/// </summary>
/// <remarks>
/// Section 6.4.3 requires a diagnostic's <c>path</c> member to be "a canonical qualified path under
/// Appendix A", and Section 17.5 takes the UTF-8 bytes of an encoded selector as the fold order's
/// final tie-breaker. Both are the Section 19.1 encoding, and Section 19.1 is total and injective;
/// joining part texts with a delimiter is neither, because the one-part name <c>a\.b</c> and the
/// two-part name <c>a.b</c> would join to the same text. Sharing one entry point keeps a caller
/// from reinventing the cheaper spelling and losing the property its clause depends on.
///
/// Neither position is a physical record, so Section 19.1's record-leading escape does not apply.
/// </remarks>
public static class CanonicalPath
{
    /// <summary>Spells a name, or returns null when it has no spelling.</summary>
    /// <param name="name">The name, or null for the empty path.</param>
    /// <returns>
    /// The canonical text, or <see langword="null"/> when <paramref name="name"/> is null or empty.
    /// </returns>
    /// <remarks>
    /// Encoding fails only for a name holding an unpaired surrogate or a URI carrying a forbidden
    /// scalar, and Appendix A.2 admits neither in a lexed name, so no name that reached this point
    /// from source can take the fallback. It exists because a name assembled in memory can, and a
    /// diagnostic that says nothing about where it applies is worse than one that says it
    /// approximately.
    /// </remarks>
    public static string? Of(QualifiedName? name) =>
        name is null || name.Parts.IsEmpty
            ? null
            : NamespaceEncoder.TryEncodeName(
                name,
                NamespaceEncoder.DefaultDelimiter,
                recordLeading: false,
                out var text,
                out _)
                ? text!
                : name.ToString();

    /// <summary>Spells a path, or returns null when it is empty.</summary>
    /// <param name="path">The name parts.</param>
    /// <returns>The canonical text, or <see langword="null"/> when the path is empty.</returns>
    public static string? Of(ImmutableArray<NamePart> path) =>
        path.IsEmpty ? null : Of(new QualifiedName(path));
}
