namespace Namespace2Xml.Output;

/// <summary>The Section 16.9 <c>xmloutputoptions</c> flags.</summary>
[Flags]
public enum XmlOutputOptions
{
    /// <summary>No option selected, which Section 16.9 resolves to <see cref="XmlOutput.Default"/>.</summary>
    None = 0,

    /// <summary>Two ASCII spaces per element nesting level outside mixed content. The default.</summary>
    Indent = 1,

    /// <summary>No formatting whitespace at all.</summary>
    NoIndent = 2,

    /// <summary>Place every attribute, including the first, on its own line.</summary>
    NewLineOnAttributes = 4,

    /// <summary>Re-emit imported CDATA as CDATA. The default.</summary>
    PreserveCData = 8,

    /// <summary>Emit imported CDATA as ordinary text.</summary>
    CDataAsText = 16,

    /// <summary>Emit an XML declaration. The default.</summary>
    Declaration = 32,

    /// <summary>Omit the XML declaration.</summary>
    NoDeclaration = 64,
}

/// <summary>Reads the Section 16.9 XML options.</summary>
public static class XmlOutput
{
    /// <summary>Section 16.9's default, used when the directive is absent.</summary>
    public const XmlOutputOptions Default =
        XmlOutputOptions.Indent | XmlOutputOptions.PreserveCData | XmlOutputOptions.Declaration;

    /// <summary>Whether the writer indents outside mixed content.</summary>
    /// <param name="options">The selected options.</param>
    /// <remarks>
    /// Section 16.9: "When a replacement omits every flag from a mutually exclusive mode group,
    /// that group's documented default is reapplied." Each of the three XML groups is read as the
    /// absence of its non-default member, so a selection that names neither takes the default
    /// whether or not the compiler has normalized it.
    /// </remarks>
    public static bool Indents(this XmlOutputOptions options) =>
        !options.HasFlag(XmlOutputOptions.NoIndent);

    /// <summary>Whether each attribute goes on its own line.</summary>
    /// <param name="options">The selected options.</param>
    public static bool BreaksAttributeLines(this XmlOutputOptions options) =>
        options.HasFlag(XmlOutputOptions.NewLineOnAttributes);

    /// <summary>Whether imported CDATA is re-emitted as CDATA rather than as text.</summary>
    /// <param name="options">The selected options.</param>
    public static bool PreservesCData(this XmlOutputOptions options) =>
        !options.HasFlag(XmlOutputOptions.CDataAsText);

    /// <summary>Whether the document begins with an XML declaration.</summary>
    /// <param name="options">The selected options.</param>
    public static bool WritesDeclaration(this XmlOutputOptions options) =>
        !options.HasFlag(XmlOutputOptions.NoDeclaration);

    /// <summary>Reports the Section 16.9 contradiction two options make, if any.</summary>
    /// <param name="options">The selected options.</param>
    /// <param name="contradiction">The prose naming the two contradictory options.</param>
    /// <returns>Whether the combination is legal, and so not <c>SCHEME001</c>.</returns>
    public static bool TryValidate(this XmlOutputOptions options, out string? contradiction)
    {
        if (options.HasFlag(XmlOutputOptions.Indent) && options.HasFlag(XmlOutputOptions.NoIndent))
        {
            contradiction = Pair("Indent", "NoIndent", "layout modes");
            return false;
        }

        if (options.HasFlag(XmlOutputOptions.NoIndent)
            && options.HasFlag(XmlOutputOptions.NewLineOnAttributes))
        {
            contradiction = "'NoIndent' inserts no formatting whitespace and 'NewLineOnAttributes' "
                + "requires a line break and two spaces before every attribute, so Section 16.9 "
                + "makes them mutually exclusive.";
            return false;
        }

        if (options.HasFlag(XmlOutputOptions.PreserveCData)
            && options.HasFlag(XmlOutputOptions.CDataAsText))
        {
            contradiction = Pair("PreserveCData", "CDataAsText", "CDATA modes");
            return false;
        }

        if (options.HasFlag(XmlOutputOptions.Declaration)
            && options.HasFlag(XmlOutputOptions.NoDeclaration))
        {
            contradiction = Pair("Declaration", "NoDeclaration", "declaration modes");
            return false;
        }

        contradiction = null;
        return true;
    }

    private static string Pair(string first, string second, string group) =>
        $"'{first}' and '{second}' are the two XML {group}, "
        + "so Section 16.9 makes them mutually exclusive.";
}
