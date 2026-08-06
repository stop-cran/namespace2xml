namespace Namespace2Xml.Profiles;

/// <summary>
/// One classified namespace-profile record: its Section 8.1 kind and the text that kind carries.
/// </summary>
/// <remarks>
/// The payload members are populated only for the kind that defines them, so reading
/// <see cref="Name"/> on a comment yields <see langword="null"/> rather than misleading text.
/// </remarks>
public sealed class NamespaceRecord
{
    private NamespaceRecord(NamespaceRecordKind kind, int line, int column)
    {
        Kind = kind;
        Line = line;
        Column = column;
    }

    /// <summary>The Section 8.1 classification.</summary>
    public NamespaceRecordKind Kind { get; }

    /// <summary>One-based line number under Section 22.</summary>
    public int Line { get; }

    /// <summary>
    /// One-based column where this record's content begins: the <c>#</c> of a comment, the first
    /// scalar of a mask pattern, or the first scalar of an entry name.
    /// </summary>
    /// <remarks>
    /// Section 8.1 excludes the leading spaces and tabs of a comment or mask from its text, so the
    /// text alone cannot say where it started. Section 22 requires a column, and a column measured
    /// from the wrong origin points a reader at the wrong character — worse than omitting it.
    /// </remarks>
    public int Column { get; }

    /// <summary>
    /// The name text of an entry: every scalar before the separating <c>=</c>, untrimmed. Still
    /// escaped; Section 8.2 lexing has not run.
    /// </summary>
    public string? Name { get; private init; }

    /// <summary>
    /// The value text of an entry: every scalar after the separating <c>=</c>, untrimmed. Still
    /// escaped; Section 8.3 interpretation has not run.
    /// </summary>
    public string? Value { get; private init; }

    /// <summary>
    /// A comment's text, beginning at its <c>#</c>. Section 8.1 excludes the preceding spaces and
    /// tabs, which are not comment text.
    /// </summary>
    public string? Comment { get; private init; }

    /// <summary>
    /// A mask's pattern, beginning after its <c>!</c>. Section 8.1 excludes the preceding spaces
    /// and tabs, which are not part of the pattern.
    /// </summary>
    public string? Pattern { get; private init; }

    internal static NamespaceRecord Ignored(int line) =>
        new(NamespaceRecordKind.Ignored, line, 1);

    internal static NamespaceRecord Malformed(int line) =>
        new(NamespaceRecordKind.Malformed, line, 1);

    internal static NamespaceRecord OfComment(string comment, int line, int column) =>
        new(NamespaceRecordKind.Comment, line, column) { Comment = comment };

    internal static NamespaceRecord OfMask(string pattern, int line, int column) =>
        new(NamespaceRecordKind.Mask, line, column) { Pattern = pattern };

    internal static NamespaceRecord OfEntry(string name, string value, int line) =>
        new(NamespaceRecordKind.Entry, line, 1) { Name = name, Value = value };
}
