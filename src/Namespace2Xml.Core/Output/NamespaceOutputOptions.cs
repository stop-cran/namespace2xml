namespace Namespace2Xml.Output;

/// <summary>The Section 16.9 <c>namespaceoutputoptions</c> flags of the namespace destination.</summary>
/// <remarks>
/// <para>
/// Section 16.9 gives this directive one flag, and it relaxes the Section 24 byte rule rather than
/// widening a dialect. It is the only option in this specification that does, which is why the flag
/// is off by default and why every value it admits is reported.
/// </para>
/// <para>
/// The directive governs the <c>namespace</c> destination alone. Section 19.2 writes every
/// quoted-namespace value inside single quotes, so no such line ends in a value's own whitespace
/// and the decision this flag makes does not arise there.
/// </para>
/// </remarks>
[Flags]
public enum NamespaceOutputOptions
{
    /// <summary>
    /// No option selected, under which Section 19.1 refuses an entry whose value ends in a space.
    /// </summary>
    None = 0,

    /// <summary>
    /// Write an entry whose value ends in a space, reporting <c>WARN013</c> for each one.
    /// </summary>
    AllowTrailingWhitespace = 1,
}

/// <summary>Reads the Section 16.9 namespace options.</summary>
public static class NamespaceOutput
{
    /// <summary>Section 16.9's default, used when the directive is absent.</summary>
    public const NamespaceOutputOptions Default = NamespaceOutputOptions.None;

    /// <summary>
    /// Whether an entry whose value ends in a space is written rather than refused.
    /// </summary>
    /// <param name="options">The selected options.</param>
    public static bool AllowsTrailingWhitespace(this NamespaceOutputOptions options) =>
        options.HasFlag(NamespaceOutputOptions.AllowTrailingWhitespace);

    /// <summary>
    /// Whether the emitted text would end a physical line in whitespace Section 24 forbids.
    /// </summary>
    /// <param name="emitted">The value text as Section 19.1 spells it, after escaping.</param>
    /// <remarks>
    /// Section 24 names a space and a TAB and no other character carrying the Unicode
    /// <c>White_Space</c> property, because the hazard is what a consumer strips rather than what is
    /// invisible, and because Section 8.1's own notion of whitespace is these two. The TAB arm
    /// cannot fire on a value — Section 19.1 emits a TAB as <c>\t</c> — and is kept because the rule
    /// it enforces names both, so a future escape-table change reaches this check rather than
    /// slipping past it.
    /// </remarks>
    public static bool EndsInForbiddenWhitespace(string emitted)
    {
        ArgumentNullException.ThrowIfNull(emitted);

        return emitted.Length > 0 && emitted[^1] is ' ' or '\t';
    }
}
