namespace Namespace2Xml.Profiles;

/// <summary>
/// The Section 8.1 classification of one physical record, in the order that section fixes.
/// </summary>
public enum NamespaceRecordKind
{
    /// <summary>Empty or spaces and tabs only. Contributes nothing.</summary>
    Ignored,

    /// <summary>First non-space/tab scalar is <c>#</c>. Section 8.5.</summary>
    Comment,

    /// <summary>First non-space/tab scalar is <c>!</c>. Section 8.6.</summary>
    Mask,

    /// <summary>Contains a separating <c>=</c>. Section 8.1.</summary>
    Entry,

    /// <summary>None of the above: a <c>PARSE001</c> record.</summary>
    Malformed,
}
