using Namespace2Xml.Profiles;

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
    /// no-ops". It names a directive's value rather than its name.
    /// </summary>
    LegacyTypeValue,

    /// <summary>
    /// Section 15.3's <c>keyOnly</c> for Section 16.7 substitute mode <c>Key</c>. Like
    /// <see cref="LegacyTypeValue"/> it names a directive's value rather than its name.
    /// </summary>
    KeyOnly,
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

    private static readonly Dictionary<SchemeDirective, string> Canonical =
        Names.Where(entry => entry.Value.Alias == SchemeAlias.None)
            .ToDictionary(entry => entry.Value.Directive, entry => entry.Key);

    /// <summary>
    /// The Section 15 directive names an author may write, excluding the Section 15.3 deprecated
    /// aliases, which a refusal should not steer anyone towards.
    /// </summary>
    public static IEnumerable<string> Spellings => Canonical.Values;

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

    /// <summary>
    /// The Section 12.1 capture form a directive's value recognizes, given its selector's.
    /// </summary>
    /// <param name="directive">The directive whose value is about to be lexed.</param>
    /// <param name="selector">The form the owning name defines.</param>
    /// <returns>The form to lex the value with.</returns>
    /// <remarks>
    /// Section 12.1 excludes two directives from capture substitution: "Section 16.6 closes the
    /// type names and their legal combinations, and Section 16.1 closes the output formats, so a
    /// capture could complete either only by accident of the matched data. Capture recognition is
    /// therefore disabled in both values whatever the selector defines, and an unescaped <c>*</c>
    /// in a <c>type</c> or <c>output</c> value is literal text."
    /// <para>
    /// The rule is applied here, at the one place both readers ask what a value's captures are,
    /// because the clause makes the exclusion a property of the directive: "so <c>cfg.*.output=*</c>
    /// and <c>cfg.output=*</c> are the same error". Deciding it later, from the lexed value, would
    /// make the two declarations differ in the token stream and agree only by a second rule.
    /// </para>
    /// </remarks>
    public static WildcardSyntax CaptureForm(SchemeDirective directive, WildcardSyntax selector) =>
        directive is SchemeDirective.Type or SchemeDirective.Output
            ? WildcardSyntax.None
            : selector;

    /// <summary>Section 15's spelling of a directive, for a canonical directive path.</summary>
    /// <param name="directive">The directive.</param>
    /// <returns>The lowercase name Section 15's bullet list gives it.</returns>
    /// <remarks>
    /// Section 15 matches a directive name ASCII case-insensitively, so a written name is not a
    /// canonical one and two references that name one setting must spell it identically. The list
    /// is inverted from <see cref="Names"/> rather than restated so that a directive added to one
    /// cannot be missing from the other; the aliases are excluded because Section 15.3 deprecates
    /// them, and a canonical spelling that is deprecated would be a contradiction.
    /// </remarks>
    public static string CanonicalSpelling(SchemeDirective directive) =>
        Canonical.TryGetValue(directive, out var name)
            ? name
            : throw new ArgumentOutOfRangeException(
                nameof(directive),
                directive,
                "Section 15 lists every recognized directive, and none of them is this one.");

    /// <summary>The specification's spelling of an alias, for the deprecation warning.</summary>
    /// <param name="alias">The alias category.</param>
    public static string Spelling(SchemeAlias alias) => alias switch
    {
        SchemeAlias.NamespaceDelimiter => "namespacedelimiter",
        SchemeAlias.XmlOptions => "xmloptions",
        SchemeAlias.LegacyTypeValue => "xmlns/xmlnssuffix",
        SchemeAlias.KeyOnly => "keyOnly",
        _ => throw new ArgumentOutOfRangeException(
            nameof(alias),
            alias,
            "Section 15.3 lists four alias categories, and none of them is this one."),
    };

    /// <summary>The directive an alias is deprecated in favour of.</summary>
    /// <param name="alias">The alias category.</param>
    public static string Replacement(SchemeAlias alias) => alias switch
    {
        SchemeAlias.NamespaceDelimiter => "delimiter",
        SchemeAlias.XmlOptions => "xmloutputoptions",
        SchemeAlias.LegacyTypeValue => "nothing, because they are no-ops",
        SchemeAlias.KeyOnly => "substitute mode 'Key'",
        _ => throw new ArgumentOutOfRangeException(
            nameof(alias),
            alias,
            "Section 15.3 lists four alias categories, and none of them is this one."),
    };
}
