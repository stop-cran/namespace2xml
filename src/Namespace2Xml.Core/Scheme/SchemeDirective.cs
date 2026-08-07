namespace Namespace2Xml.Scheme;

/// <summary>The Section 15 recognized scheme directives, after alias resolution.</summary>
/// <remarks>
/// Section 15.3's aliases are not members: <c>namespacedelimiter</c> and <c>xmloptions</c> resolve
/// to the directive they name, so nothing downstream has to know which spelling was written. The
/// warning that records the spelling is raised where the alias is recognized.
/// </remarks>
public enum SchemeDirective
{
    /// <summary>Section 16.1.</summary>
    Output,

    /// <summary>Section 16.2.</summary>
    Filename,

    /// <summary>Section 16.3.</summary>
    Root,

    /// <summary>Section 16.4. Also written <c>namespacedelimiter</c>.</summary>
    Delimiter,

    /// <summary>Section 16.5.</summary>
    Key,

    /// <summary>Section 16.6.</summary>
    Type,

    /// <summary>Section 16.7.</summary>
    Substitute,

    /// <summary>Section 16.8.</summary>
    XmlInputOptions,

    /// <summary>Section 16.9. Also written <c>xmloptions</c>.</summary>
    XmlOutputOptions,

    /// <summary>Section 16.8.</summary>
    JsonInputOptions,

    /// <summary>Section 16.9.</summary>
    JsonOutputOptions,

    /// <summary>Section 16.8.</summary>
    YamlInputOptions,

    /// <summary>Section 16.9.</summary>
    YamlOutputOptions,

    /// <summary>Section 16.9.</summary>
    IniOutputOptions,

    /// <summary>Section 16.10.</summary>
    Merge,

    /// <summary>Section 16.11.</summary>
    FileMerge,
}

/// <summary>
/// The Section 15.3 alias categories. WARN002 is raised "once per alias category and scheme", so
/// the category, not the occurrence, is what a scheme's warning set is keyed by.
/// </summary>
public enum SchemeAlias
{
    /// <summary>Not an alias.</summary>
    None,

    /// <summary><c>namespacedelimiter</c> for <c>delimiter</c>.</summary>
    NamespaceDelimiter,

    /// <summary><c>xmloptions</c> for <c>xmloutputoptions</c>.</summary>
    XmlOptions,

    /// <summary>
    /// Section 15.3's legacy <c>type</c> values <c>xmlns</c> and <c>xmlnssuffix</c>, "treated as
    /// no-ops". It is the one alias category that names a directive's value rather than its name.
    /// </summary>
    LegacyTypeValue,
}

/// <summary>Recognizes a Section 15 directive name.</summary>
public static class SchemeDirectives
{
    // Section 15 matches directive names under ASCII case-insensitive comparison. The table is
    // written in the specification's own spelling and looked up with an ordinal ignore-case
    // comparer, so no name here changes meaning with the host locale.
    private static readonly Dictionary<string, (SchemeDirective Directive, SchemeAlias Alias)> Names =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["output"] = (SchemeDirective.Output, SchemeAlias.None),
            ["filename"] = (SchemeDirective.Filename, SchemeAlias.None),
            ["root"] = (SchemeDirective.Root, SchemeAlias.None),
            ["delimiter"] = (SchemeDirective.Delimiter, SchemeAlias.None),
            ["namespacedelimiter"] = (SchemeDirective.Delimiter, SchemeAlias.NamespaceDelimiter),
            ["key"] = (SchemeDirective.Key, SchemeAlias.None),
            ["type"] = (SchemeDirective.Type, SchemeAlias.None),
            ["substitute"] = (SchemeDirective.Substitute, SchemeAlias.None),
            ["xmloptions"] = (SchemeDirective.XmlOutputOptions, SchemeAlias.XmlOptions),
            ["xmlinputoptions"] = (SchemeDirective.XmlInputOptions, SchemeAlias.None),
            ["xmloutputoptions"] = (SchemeDirective.XmlOutputOptions, SchemeAlias.None),
            ["jsoninputoptions"] = (SchemeDirective.JsonInputOptions, SchemeAlias.None),
            ["jsonoutputoptions"] = (SchemeDirective.JsonOutputOptions, SchemeAlias.None),
            ["yamlinputoptions"] = (SchemeDirective.YamlInputOptions, SchemeAlias.None),
            ["yamloutputoptions"] = (SchemeDirective.YamlOutputOptions, SchemeAlias.None),
            ["inioutputoptions"] = (SchemeDirective.IniOutputOptions, SchemeAlias.None),
            ["merge"] = (SchemeDirective.Merge, SchemeAlias.None),
            ["filemerge"] = (SchemeDirective.FileMerge, SchemeAlias.None),
        };

    /// <summary>Recognizes one directive name.</summary>
    /// <param name="name">The final qualified-name part, already unescaped.</param>
    /// <param name="directive">The directive the name identifies.</param>
    /// <param name="alias">The Section 15.3 alias category the spelling belongs to.</param>
    /// <returns>Whether the name is recognized; an unrecognized one is <c>SCHEME001</c>.</returns>
    public static bool TryRecognize(string name, out SchemeDirective directive, out SchemeAlias alias)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Names.TryGetValue(name, out var found))
        {
            (directive, alias) = found;
            return true;
        }

        directive = default;
        alias = SchemeAlias.None;
        return false;
    }

    /// <summary>The specification's spelling of an alias, for the deprecation warning.</summary>
    /// <param name="alias">The alias category.</param>
    public static string Spelling(SchemeAlias alias) => alias switch
    {
        SchemeAlias.NamespaceDelimiter => "namespacedelimiter",
        SchemeAlias.XmlOptions => "xmloptions",
        SchemeAlias.LegacyTypeValue => "xmlns/xmlnssuffix",
        _ => throw new ArgumentOutOfRangeException(
            nameof(alias),
            alias,
            "Section 15.3 lists two directive aliases, and neither is this one."),
    };

    /// <summary>The directive an alias is deprecated in favour of.</summary>
    /// <param name="alias">The alias category.</param>
    public static string Replacement(SchemeAlias alias) => alias switch
    {
        SchemeAlias.NamespaceDelimiter => "delimiter",
        SchemeAlias.XmlOptions => "xmloutputoptions",
        SchemeAlias.LegacyTypeValue => "nothing, because they are no-ops",
        _ => throw new ArgumentOutOfRangeException(
            nameof(alias),
            alias,
            "Section 15.3 lists two directive aliases, and neither is this one."),
    };
}
