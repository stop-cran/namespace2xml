namespace Namespace2Xml.Inputs;

/// <summary>
/// What a namespace-profile contribution came from, as the Section 6.4.3 diagnostic members
/// describe it.
/// </summary>
/// <remarks>
/// <para>
/// A file and a command-line variable are both source contributions under Section 5.1, and they
/// report differently. Section 8.1 states that a diagnostic from a variable "omits <c>source</c>,
/// and therefore also omits <c>line</c> and <c>column</c>", because the <c>source</c> member names
/// a file and "a synthetic file name there would be indistinguishable from a real one".
/// </para>
/// <para>
/// The cardinality key is carried separately rather than derived from <see cref="File"/>. Section 22
/// scopes several codes once per source position, and two malformed variables share a null
/// <c>source</c>: keyed on the reported member they would collapse into one occurrence, and the
/// second variable's error would vanish from the stream.
/// </para>
/// </remarks>
/// <param name="File">The Section 6.4.3 <c>source</c> member, or <c>null</c> for a variable.</param>
/// <param name="Identity">The distinct name this source is keyed by, never <c>null</c>.</param>
public sealed record ProfileSource(string? File, string Identity)
{
    /// <summary>A source read from an input or scheme file.</summary>
    /// <param name="path">The path as diagnostics report it.</param>
    public static ProfileSource OfFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new ProfileSource(path, path);
    }

    /// <summary>A source supplied as one <c>--variables</c> argument.</summary>
    /// <param name="position">The variable's one-based position in <c>-v</c> token order.</param>
    public static ProfileSource OfVariable(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(position);

        return new ProfileSource(null, $"-v[{position}]");
    }

    /// <summary>The line to report, which is nothing when there is no file to report it against.</summary>
    /// <param name="line">The one-based line within this source.</param>
    public int? LineOf(int line) => File is null ? null : line;

    /// <summary>The column to report, which is nothing when there is no line to report it against.</summary>
    /// <param name="column">The one-based column within this source.</param>
    public int? ColumnOf(int column) => File is null ? null : column;

    /// <summary>The Section 22 cardinality key of a condition at one position in this source.</summary>
    /// <param name="line">The one-based line.</param>
    /// <param name="column">The one-based column.</param>
    public string Key(int line, int column) => $"{Identity}:{line}:{column}";

    /// <summary>The Section 22 cardinality key of a condition scoped to one record.</summary>
    /// <param name="line">The one-based line the record begins on.</param>
    /// <remarks>
    /// A namespace record occupies one logical line, so this is the key for the codes Section 22
    /// scopes "once per rule" or "once per reachable owning value". Adding the column would split
    /// one rule into as many occurrences as it has faulty positions.
    /// </remarks>
    public string RecordKey(int line) => $"{Identity}:{line}";

    /// <summary>
    /// The Section 22 cardinality key of a condition scoped to the whole source, which is what
    /// "once per failing source" means for <c>PARSE001</c> and <c>PARSE002</c>.
    /// </summary>
    public string SourceKey => Identity;

    /// <summary>The Section 22 cardinality key of a condition scoped to this source as a whole.</summary>
    /// <param name="aspect">What the code is emitted once per within one source.</param>
    public string Key(string aspect) => $"{Identity}:{aspect}";
}
