using System.Collections.Immutable;
using Namespace2Xml.Output;

namespace Namespace2Xml.Scheme;

/// <summary>The Section 16.1 output formats. <c>ignore</c> is not one: it is the absence of all.</summary>
public enum OutputFormat
{
    /// <summary>Section 19.1.</summary>
    Namespace,

    /// <summary>Section 19.2.</summary>
    QuotedNamespace,

    /// <summary>Section 19.3.</summary>
    Json,

    /// <summary>Section 19.4.</summary>
    Yaml,

    /// <summary>Section 19.5.</summary>
    Xml,

    /// <summary>Section 19.6.</summary>
    Ini,
}

/// <summary>Reads and describes the Section 16.1 formats.</summary>
public static class OutputFormats
{
    /// <summary>The text Section 16.1 gives the negative declaration.</summary>
    public const string Ignore = "ignore";

    private static readonly Dictionary<string, OutputFormat> Names =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["namespace"] = OutputFormat.Namespace,
            ["quotednamespace"] = OutputFormat.QuotedNamespace,
            ["json"] = OutputFormat.Json,
            ["yaml"] = OutputFormat.Yaml,
            ["xml"] = OutputFormat.Xml,
            ["ini"] = OutputFormat.Ini,
        };

    /// <summary>
    /// The Section 16.1 format names an author may write, in the order Section 16.1 lists them,
    /// including the negative declaration.
    /// </summary>
    /// <remarks>
    /// Written out rather than projected from <see cref="Names"/> because a dictionary's
    /// enumeration order is an implementation detail and this list is read by humans, who should
    /// meet it in the order the clause presents it. <c>TheSpellingsAreExactlyTheNamesTheParserKnows</c>
    /// holds the two in agreement, so the ordering choice cannot become a membership difference.
    /// </remarks>
    public static ImmutableArray<string> Spellings { get; } =
    [
        "namespace",
        "quotednamespace",
        "json",
        "yaml",
        "xml",
        "ini",
        Ignore,
    ];

    /// <summary>The spellings <see cref="TryParse"/> matches, excluding the negative declaration.</summary>
    internal static IEnumerable<string> ParsedNames => Names.Keys;

    /// <summary>Recognizes one format name.</summary>
    /// <param name="name">The written name, already trimmed.</param>
    /// <param name="format">The format the name identifies.</param>
    public static bool TryParse(string name, out OutputFormat format)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Names.TryGetValue(name, out format);
    }

    /// <summary>
    /// The Section 16.2 default extension, which applies "only when no effective <c>filename</c>
    /// directive exists".
    /// </summary>
    /// <param name="format">The format.</param>
    public static string DefaultExtension(this OutputFormat format) => format switch
    {
        OutputFormat.Namespace => ".properties",
        OutputFormat.QuotedNamespace => ".sh",
        OutputFormat.Json => ".json",
        OutputFormat.Yaml => ".yaml",
        OutputFormat.Xml => ".xml",
        OutputFormat.Ini => ".ini",
        _ => throw new ArgumentOutOfRangeException(
            nameof(format), format, "Section 16.1 lists six formats, and this is not one of them."),
    };

    /// <summary>
    /// The Section 19 flat format this one renders as, when it is one of the three that spell a
    /// path as flat key text.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <param name="flat">The flat format.</param>
    public static bool TryAsFlat(this OutputFormat format, out FlatFormat flat)
    {
        switch (format)
        {
            case OutputFormat.Namespace:
                flat = FlatFormat.Namespace;
                return true;
            case OutputFormat.QuotedNamespace:
                flat = FlatFormat.QuotedNamespace;
                return true;
            case OutputFormat.Ini:
                flat = FlatFormat.Ini;
                return true;
            default:
                flat = default;
                return false;
        }
    }
}
