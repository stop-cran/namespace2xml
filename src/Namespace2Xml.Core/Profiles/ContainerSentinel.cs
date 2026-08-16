namespace Namespace2Xml.Profiles;

/// <summary>The Section 8.3 reading of a namespace-profile value that is a bracket pair.</summary>
public enum ContainerSentinel
{
    /// <summary>The value is ordinary text and reaches the Section 8.3 escape productions.</summary>
    None,

    /// <summary>The value is exactly <c>{}</c>: an explicit empty-mapping presence contribution.</summary>
    EmptyMapping,

    /// <summary>The value is exactly <c>[]</c>: an explicit empty-sequence contribution.</summary>
    EmptySequence,

    /// <summary>The value is exactly <c>\{}</c> or <c>\[]</c>: the two-character string it escapes.</summary>
    EscapedText,
}

/// <summary>
/// Section 8.3's whole-value container sentinels, and the Section 19.1 spelling that writes them
/// back.
/// </summary>
/// <remarks>
/// Section 8.3 classifies "on the raw value text, before any escape below is decoded, and only when
/// the sentinel is the entire value", so this is a comparison of four fixed strings rather than a
/// production of the value grammar. Appendix A.3 says the same from the other side: a value
/// matching none of the four is what reaches tokenization.
/// </remarks>
public static class ContainerSentinels
{
    /// <summary>The Section 8.3 spelling of an explicit empty mapping.</summary>
    public const string Mapping = "{}";

    /// <summary>The Section 8.3 spelling of an explicit empty sequence.</summary>
    public const string Sequence = "[]";

    private const string EscapedMapping = @"\{}";
    private const string EscapedSequence = @"\[]";

    /// <summary>Reads one raw namespace-profile value as a Section 8.3 container sentinel.</summary>
    /// <param name="rawValue">The value exactly as written, before escape decoding.</param>
    /// <returns>The reading, or <see cref="ContainerSentinel.None"/> for ordinary text.</returns>
    public static ContainerSentinel Classify(string rawValue) => rawValue switch
    {
        Mapping => ContainerSentinel.EmptyMapping,
        Sequence => ContainerSentinel.EmptySequence,
        EscapedMapping or EscapedSequence => ContainerSentinel.EscapedText,
        _ => ContainerSentinel.None,
    };

    /// <summary>The text a <see cref="ContainerSentinel.EscapedText"/> value denotes.</summary>
    /// <param name="rawValue">The value exactly as written.</param>
    /// <returns>The two-character string the leading backslash escapes.</returns>
    public static string Unescape(string rawValue) =>
        rawValue == EscapedMapping ? Mapping : Sequence;

    /// <summary>
    /// Section 19.1: escapes a scalar whose whole text would otherwise read back as a container.
    /// </summary>
    /// <param name="text">The scalar text the writer is about to emit.</param>
    /// <returns>The text to emit, prefixed with a backslash when it is a bare sentinel.</returns>
    public static string Spell(string text) =>
        text is Mapping or Sequence ? "\\" + text : text;
}
