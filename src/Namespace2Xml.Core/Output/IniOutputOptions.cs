namespace Namespace2Xml.Output;

/// <summary>The Section 16.9 <c>inioutputoptions</c> flags of the <c>PortableIni1</c> dialect.</summary>
/// <remarks>
/// Section 19.6 calls this dialect "a conservative interoperable subset", and every flag here widens
/// it. Consumers "must opt into <c>QuoteValues</c> or <c>EscapeMultiline</c> only when their parser
/// recognizes those escapes", which is why the widening options are off by default rather than on.
/// </remarks>
[Flags]
public enum IniOutputOptions
{
    /// <summary>No option selected, which Section 16.9 resolves to <see cref="RejectMultiline"/>.</summary>
    None = 0,

    /// <summary>Enable comment emission with <c>;</c> as the marker.</summary>
    SemicolonComments = 1,

    /// <summary>Enable comment emission with <c>#</c> as the marker.</summary>
    HashComments = 2,

    /// <summary>Reject NUL, CR, and LF in a value. The default.</summary>
    RejectMultiline = 4,

    /// <summary>Emit LF, CR, and tab in a value as <c>\n</c>, <c>\r</c>, and <c>\t</c>.</summary>
    EscapeMultiline = 8,

    /// <summary>Emit double-quoted values, escaping <c>\</c> and <c>"</c>.</summary>
    QuoteValues = 16,
}

/// <summary>Reads the Section 16.9 INI options.</summary>
public static class IniOutput
{
    /// <summary>Section 16.9's default, used when the directive is absent.</summary>
    public const IniOutputOptions Default = IniOutputOptions.RejectMultiline;

    /// <summary>
    /// The comment marker, or <see langword="null"/> when Section 20 discards comments because
    /// neither comment option was selected.
    /// </summary>
    /// <param name="options">The selected options.</param>
    public static char? CommentMarker(this IniOutputOptions options) =>
        options.HasFlag(IniOutputOptions.SemicolonComments) ? ';'
        : options.HasFlag(IniOutputOptions.HashComments) ? '#'
        : null;

    /// <summary>
    /// Whether a multiline value is escaped rather than rejected.
    /// </summary>
    /// <param name="options">The selected options.</param>
    /// <remarks>
    /// Section 19.6 rejects multiline values "by default unless an explicit strategy is selected",
    /// so the absence of <see cref="IniOutputOptions.EscapeMultiline"/> rejects, whether or not
    /// <see cref="IniOutputOptions.RejectMultiline"/> was named. Naming it selects the default.
    /// </remarks>
    public static bool EscapesMultiline(this IniOutputOptions options) =>
        options.HasFlag(IniOutputOptions.EscapeMultiline);

    /// <summary>Reports the Section 16.9 contradiction two options make, if any.</summary>
    /// <param name="options">The selected options.</param>
    /// <param name="contradiction">The prose naming the two contradictory options.</param>
    /// <returns>Whether the combination is legal, and so not <c>SCHEME001</c>.</returns>
    public static bool TryValidate(
        this IniOutputOptions options,
        out string? contradiction)
    {
        if (options.HasFlag(IniOutputOptions.SemicolonComments)
            && options.HasFlag(IniOutputOptions.HashComments))
        {
            contradiction =
                "'SemicolonComments' and 'HashComments' each select the comment marker, "
                + "so Section 16.9 makes them mutually exclusive.";
            return false;
        }

        if (options.HasFlag(IniOutputOptions.RejectMultiline)
            && options.HasFlag(IniOutputOptions.EscapeMultiline))
        {
            contradiction =
                "'RejectMultiline' and 'EscapeMultiline' are the two multiline strategies, "
                + "so Section 16.9 makes them mutually exclusive.";
            return false;
        }

        contradiction = null;
        return true;
    }
}
