namespace Namespace2Xml.Inputs;

/// <summary>The Section 16.8 <c>xmlinputoptions</c> values.</summary>
[Flags]
public enum XmlInputOptions
{
    /// <summary>No option named.</summary>
    None = 0,

    /// <summary>Section 11.7: "The option <c>PreserveWhitespace</c> retains every text node."</summary>
    PreserveWhitespace = 1,

    /// <summary>Section 11.7's "explicit opt-in compatibility mode".</summary>
    NormalizeFormattingWhitespace = 2,
}

/// <summary>Reads a Section 16.8 XML input-option set.</summary>
public static class XmlInput
{
    /// <summary>Section 16.8: "Default: <c>PreserveWhitespace</c>."</summary>
    public const XmlInputOptions Default = XmlInputOptions.PreserveWhitespace;

    /// <summary>Whether whitespace-only text between element children is discarded.</summary>
    /// <param name="options">The option set.</param>
    public static bool NormalizesWhitespace(this XmlInputOptions options) =>
        options.HasFlag(XmlInputOptions.NormalizeFormattingWhitespace);

    /// <summary>Whether the set names two members of a mutually exclusive group.</summary>
    /// <param name="options">The option set.</param>
    /// <param name="contradiction">What it named, when it named both.</param>
    public static bool TryValidate(this XmlInputOptions options, out string? contradiction)
    {
        if (options.HasFlag(XmlInputOptions.PreserveWhitespace)
            && options.HasFlag(XmlInputOptions.NormalizeFormattingWhitespace))
        {
            contradiction =
                "'PreserveWhitespace' and 'NormalizeFormattingWhitespace' are the two members of "
                + "one Section 16.8 mutually exclusive group, and this names both.";
            return false;
        }

        contradiction = null;
        return true;
    }
}

/// <summary>The Section 16.8 root-level input options an invocation reads its sources under.</summary>
/// <param name="Xml">The XML whitespace mode.</param>
/// <remarks>
/// Section 16.8's JSON and YAML sets each hold exactly one value, and each is "enabled by default",
/// so naming it changes nothing an input reader can observe. Their directives are still compiled,
/// because naming a value that is <em>not</em> in the set is a scheme error either way, and a
/// silently accepted <c>jsoninputoptions=Lenient</c> would read as a mode this version has.
/// </remarks>
public sealed record InputOptions(XmlInputOptions Xml)
{
    /// <summary>The options an invocation with no input-option directive runs under.</summary>
    public static InputOptions Default { get; } = new(XmlInput.Default);
}
