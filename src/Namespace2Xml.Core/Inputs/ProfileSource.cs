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
/// <param name="VariablePosition">
/// The one-based <c>-v</c> position when this source is a command-line variable, otherwise
/// <c>null</c>.
/// </param>
/// <param name="Ordinal">This source's Section 4.7 CLI source ordinal.</param>
public sealed record ProfileSource(
    string? File, string Identity, int? VariablePosition = null, long Ordinal = 0)
{
    /// <summary>A source read from an input or scheme file.</summary>
    /// <param name="path">The path as diagnostics report it.</param>
    /// <param name="ordinal">The Section 4.7 CLI source ordinal of this occurrence.</param>
    public static ProfileSource OfFile(string path, long ordinal = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new ProfileSource(path, path, null, ordinal);
    }

    /// <summary>A source supplied as one <c>--variables</c> argument.</summary>
    /// <param name="position">The variable's one-based position in <c>-v</c> token order.</param>
    /// <param name="ordinal">The Section 4.7 CLI source ordinal of this occurrence.</param>
    public static ProfileSource OfVariable(int position, long ordinal = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(position);

        return new ProfileSource(null, $"-v[{position}]", position, ordinal);
    }

    /// <summary>
    /// The message a diagnostic from this source carries, identified as Section 8.1 requires.
    /// </summary>
    /// <param name="message">The condition's own prose.</param>
    /// <remarks>
    /// A variable "omits <c>source</c>, and therefore also omits <c>line</c> and <c>column</c>",
    /// so the members that would locate a file's diagnostic are all absent and the message is the
    /// only place left to say which argument failed. Section 8.1 identifies it "by its one-based
    /// position in <c>-v</c> token order", spelled here as the <see cref="Identity"/> the whole
    /// tool uses. A file needs no prefix: its members already say where it is, and prefixing them
    /// would restate what the reader can see.
    /// </remarks>
    public string Say(string message) =>
        VariablePosition is null ? message : $"{Identity}: {message}";

    /// <summary>The line to report, which is nothing when there is no file to report it against.</summary>
    /// <param name="line">The one-based line within this source.</param>
    public int? LineOf(int line) => File is null ? null : line;

    /// <summary>The column to report, which is nothing when there is no line to report it against.</summary>
    /// <param name="column">The one-based column within this source.</param>
    public int? ColumnOf(int column) => File is null ? null : column;

    /// <summary>
    /// What every Section 22 cardinality key of this source is built on: the occurrence, not the
    /// path.
    /// </summary>
    /// <remarks>
    /// A path written twice on the command line is two sources, not one. Section 4.7 gives each
    /// occurrence its own ordinal, and the two contribute separately -- one file of
    /// <c>{"list":["a"]}</c> supplied twice yields <c>["a","a"]</c>. A key built on the path alone
    /// would therefore let one source's diagnostic evict another's, and the evicted one may differ
    /// in <c>phase</c>: <c>-i gone.txt -s gone.txt</c> is one missing input and one missing scheme,
    /// and reporting only the scheme leaves the missing input unmentioned.
    /// </remarks>
    private string KeyBase => $"{Identity}#{Ordinal}";

    /// <summary>The Section 22 cardinality key of a condition at one position in this source.</summary>
    /// <param name="line">The one-based line.</param>
    /// <param name="column">The one-based column.</param>
    public string Key(int line, int column) => $"{KeyBase}:{line}:{column}";

    /// <summary>The Section 22 cardinality key of a condition scoped to one record.</summary>
    /// <param name="line">The one-based line the record begins on.</param>
    /// <remarks>
    /// A namespace record occupies one logical line, so this is the key for the codes Section 22
    /// scopes "once per rule" or "once per reachable owning value". Adding the column would split
    /// one rule into as many occurrences as it has faulty positions.
    /// </remarks>
    public string RecordKey(int line) => $"{KeyBase}:{line}";

    /// <summary>
    /// The Section 22 cardinality key of a condition scoped to the whole source, which is what
    /// "once per failing source" means for <c>PARSE001</c> and <c>PARSE002</c>.
    /// </summary>
    public string SourceKey => KeyBase;

    /// <summary>The Section 22 cardinality key of a condition scoped to this source as a whole.</summary>
    /// <param name="aspect">What the code is emitted once per within one source.</param>
    public string Key(string aspect) => $"{KeyBase}:{aspect}";
}
