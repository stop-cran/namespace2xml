namespace Namespace2Xml.Output;

/// <summary>The Section 16.9 <c>yamloutputoptions</c> flags.</summary>
[Flags]
public enum YamlOutputOptions
{
    /// <summary>No option selected, which Section 16.9 resolves to <see cref="PreserveComments"/>.</summary>
    None = 0,

    /// <summary>Emit retained comments in their normalized positions. The default.</summary>
    PreserveComments = 1,

    /// <summary>Emit no comments.</summary>
    DiscardComments = 2,
}

/// <summary>Reads the Section 16.9 YAML options.</summary>
public static class YamlOutput
{
    /// <summary>Section 16.9's default, used when the directive is absent.</summary>
    public const YamlOutputOptions Default = YamlOutputOptions.PreserveComments;

    /// <summary>Whether Section 20 comments are emitted.</summary>
    /// <param name="options">The selected options.</param>
    /// <remarks>
    /// Section 16.9: "When a replacement omits every flag from a mutually exclusive mode group,
    /// that group's documented default is reapplied." <see cref="YamlOutputOptions.PreserveComments"/>
    /// is that default, so only naming <see cref="YamlOutputOptions.DiscardComments"/> turns
    /// comments off.
    /// </remarks>
    public static bool PreservesComments(this YamlOutputOptions options) =>
        !options.HasFlag(YamlOutputOptions.DiscardComments);

    /// <summary>Reports the Section 16.9 contradiction two options make, if any.</summary>
    /// <param name="options">The selected options.</param>
    /// <param name="contradiction">The prose naming the two contradictory options.</param>
    /// <returns>Whether the combination is legal, and so not <c>SCHEME001</c>.</returns>
    public static bool TryValidate(
        this YamlOutputOptions options,
        out string? contradiction)
    {
        if (options.HasFlag(YamlOutputOptions.PreserveComments)
            && options.HasFlag(YamlOutputOptions.DiscardComments))
        {
            contradiction =
                "'PreserveComments' and 'DiscardComments' are the two YAML comment strategies, "
                + "so Section 16.9 makes them mutually exclusive.";
            return false;
        }

        contradiction = null;
        return true;
    }
}
