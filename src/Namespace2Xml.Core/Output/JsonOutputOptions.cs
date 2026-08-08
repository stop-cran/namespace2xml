namespace Namespace2Xml.Output;

/// <summary>The Section 16.9 <c>jsonoutputoptions</c> flags.</summary>
[Flags]
public enum JsonOutputOptions
{
    /// <summary>No option selected, which Section 16.9 resolves to <see cref="Indent"/>.</summary>
    None = 0,

    /// <summary>Two ASCII spaces per nesting level. The default.</summary>
    Indent = 1,

    /// <summary>No insignificant spaces or line breaks.</summary>
    Compact = 2,

    /// <summary>Emit every scalar above U+007F as an uppercase hexadecimal <c>\uXXXX</c> escape.</summary>
    EscapeNonAscii = 4,
}

/// <summary>Reads the Section 16.9 JSON options.</summary>
public static class JsonOutput
{
    /// <summary>Section 16.9's default, used when the directive is absent.</summary>
    public const JsonOutputOptions Default = JsonOutputOptions.Indent;

    /// <summary>Whether the writer indents rather than emitting compact text.</summary>
    /// <param name="options">The selected options.</param>
    /// <remarks>
    /// Section 16.9: "When a replacement omits every flag from a mutually exclusive mode group,
    /// that group's documented default is reapplied." <see cref="JsonOutputOptions.Indent"/> is
    /// that default, so only naming <see cref="JsonOutputOptions.Compact"/> turns indentation off.
    /// </remarks>
    public static bool Indents(this JsonOutputOptions options) =>
        !options.HasFlag(JsonOutputOptions.Compact);

    /// <summary>Whether non-ASCII text is escaped rather than emitted as literal UTF-8.</summary>
    /// <param name="options">The selected options.</param>
    public static bool EscapesNonAscii(this JsonOutputOptions options) =>
        options.HasFlag(JsonOutputOptions.EscapeNonAscii);

    /// <summary>Reports the Section 16.9 contradiction two options make, if any.</summary>
    /// <param name="options">The selected options.</param>
    /// <param name="contradiction">The prose naming the two contradictory options.</param>
    /// <returns>Whether the combination is legal, and so not <c>SCHEME001</c>.</returns>
    public static bool TryValidate(
        this JsonOutputOptions options,
        out string? contradiction)
    {
        if (options.HasFlag(JsonOutputOptions.Indent)
            && options.HasFlag(JsonOutputOptions.Compact))
        {
            contradiction =
                "'Indent' and 'Compact' are the two JSON layout modes, "
                + "so Section 16.9 makes them mutually exclusive.";
            return false;
        }

        contradiction = null;
        return true;
    }
}
