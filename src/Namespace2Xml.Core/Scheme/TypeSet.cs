using Namespace2Xml.Diagnostics;

namespace Namespace2Xml.Scheme;

/// <summary>One Section 16.6 <c>type</c> value.</summary>
public enum TypeValue
{
    /// <summary>Restores format-default projection, clearing every earlier effective value.</summary>
    Default,

    /// <summary>Removes the matched subtree from one selected output view.</summary>
    Ignore,

    /// <summary>Forces the winning container projection to a sequence.</summary>
    Array,

    /// <summary>Forces the winning container projection to an ordered mapping.</summary>
    Mapping,

    /// <summary>Forces scalar rendering as a string in the selected output view.</summary>
    /// <remarks>
    /// Spelled <c>string</c> in a scheme. The member cannot be named for its spelling because
    /// CA1720 rejects a type name as a member name and this project treats an analyzer suggestion
    /// as a build failure; <see cref="TypeSet.Spell"/> is the single place the normative spelling
    /// lives.
    /// </remarks>
    Quoted,

    /// <summary>Renders a scalar as an XML element.</summary>
    Element,

    /// <summary>Renders a scalar as an XML attribute.</summary>
    Attribute,

    /// <summary>Renders a scalar as ordinary XML text content.</summary>
    Text,

    /// <summary>Renders a scalar as XML CDATA.</summary>
    Cdata,

    /// <summary>Combines a sequence of scalar lines using logical LF.</summary>
    Multiline,
}

/// <summary>
/// One complete Section 16.6 type set, as written by one <c>type</c> directive.
/// </summary>
/// <remarks>
/// <para>
/// Section 16.6 makes the set, not the value, the unit of override: "the later complete
/// <c>type</c> directive replaces the earlier complete type set; values from separate matching
/// directives do not accumulate". Modelling a set as an accumulating flag field would make
/// <c>type=array,multiline</c> followed by <c>type=array</c> keep the multiline, which is exactly
/// the accumulation that sentence forbids.
/// </para>
/// <para>
/// The legal combinations are enumerated rather than derived, because Section 16.6 enumerates them.
/// A rule reconstructed from the list — "multiline pairs with one scalar type" — is a summary that
/// happens to agree with the list today and would silently disagree after an amendment.
/// </para>
/// </remarks>
public readonly record struct TypeSet
{
    private readonly TypeValue[] values;

    private TypeSet(TypeValue[] values) => this.values = values;

    /// <summary>The values, in the order Section 16.6 lists its legal combinations.</summary>
    public IReadOnlyList<TypeValue> Values => values ?? [];

    /// <summary>Whether this set is <c>ignore</c>, which Section 16.6 requires to appear alone.</summary>
    public bool IsIgnore => Has(TypeValue.Ignore);

    /// <summary>Whether this set is <c>default</c>, which clears earlier effective values.</summary>
    public bool IsDefault => Has(TypeValue.Default);

    /// <summary>Whether this set forces a sequence projection.</summary>
    public bool IsArray => Has(TypeValue.Array);

    /// <summary>Whether this set forces a mapping projection.</summary>
    public bool IsMapping => Has(TypeValue.Mapping);

    /// <summary>Whether this set joins a scalar sequence with logical LF.</summary>
    public bool IsMultiline => Has(TypeValue.Multiline);

    /// <summary>Whether this set forces string rendering of a scalar.</summary>
    public bool IsString => Has(TypeValue.Quoted);

    /// <summary>
    /// The Section 16.6 XML node kind this set selects, or null when it selects none.
    /// </summary>
    /// <remarks>
    /// Section 16.6 makes <c>attribute</c>, <c>element</c>, <c>text</c> and <c>cdata</c> "mutually
    /// exclusive", so at most one can be present and a single value answers for the set.
    /// </remarks>
    public TypeValue? XmlKind
    {
        get
        {
            foreach (var value in Values)
            {
                if (value is TypeValue.Element
                    or TypeValue.Attribute
                    or TypeValue.Text
                    or TypeValue.Cdata)
                {
                    return value;
                }
            }

            return null;
        }
    }

    /// <summary>Whether the set contains one value.</summary>
    /// <param name="value">The value to look for.</param>
    public bool Has(TypeValue value) => values is not null && Array.IndexOf(values, value) >= 0;

    /// <inheritdoc/>
    /// <remarks>
    /// The default equality of a record struct holding an array compares the array by reference, so
    /// two identically written sets would be unequal. Every use here is about the values.
    /// </remarks>
    public bool Equals(TypeSet other) => Values.SequenceEqual(other.Values);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);

        foreach (var value in Values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() => string.Join(",", Values.Select(Spell));

    /// <summary>Parses a complete Section 16.6 directive value.</summary>
    /// <param name="written">The directive value as written.</param>
    /// <param name="set">The parsed set.</param>
    /// <param name="failure">Why the value is not a legal type set, when it is not.</param>
    /// <returns>Whether the value is a legal type set; anything else is <c>SCHEME001</c>.</returns>
    public static bool TryParse(string written, out TypeSet set, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(written);

        set = default;
        var parsed = new List<TypeValue>();

        foreach (var token in written.Split(',').Select(part => part.Trim()))
        {
            // Section 15.3: "legacy type values xmlns and xmlnssuffix, treated as no-ops". They are
            // dropped rather than represented, so nothing downstream can act on a value that has
            // no meaning; the deprecation warning is raised where the spelling is still visible.
            if (IsLegacyNoOp(token))
            {
                continue;
            }

            if (!TryRecognize(token, out var value))
            {
                failure = $"'{token}' is not one of the Section 16.6 type values."
                    + AcceptedValues.Sentence(Spellings);
                return false;
            }

            if (parsed.Contains(value))
            {
                failure = $"'{Spell(value)}' is written twice in one type set.";
                return false;
            }

            parsed.Add(value);
        }

        if (parsed.Count == 0)
        {
            // Section 15.3 accepts the legacy values "treated as no-ops", and a no-op is not an
            // error. The empty set is therefore legal but inert: Section 16.6 has no empty type
            // set, so nothing may act on one, and Section 15.3's wording forbids reading it as
            // 'default' — that would silently clear an earlier directive rather than do nothing.
            // A value that was empty before the legacy tokens were dropped cannot reach here,
            // because the reader rejects a directive with no value.
            set = new TypeSet([]);
            failure = null;
            return true;
        }

        if (!IsLegalCombination(parsed))
        {
            failure = $"'{string.Join(",", parsed.Select(Spell))}' is not one of the Section 16.6 "
                + "legal type combinations.";
            return false;
        }

        set = new TypeSet([.. parsed]);
        failure = null;
        return true;
    }

    /// <summary>
    /// The Section 16.6 type values an author may write, in the order Section 16.6 lists them.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="Spell"/>, which is the same function <c>TryRecognize</c> matches
    /// against, so a value this tool accepts cannot go unmentioned by the diagnostic that rejects a
    /// misspelling of it. The Section 15.3 legacy no-ops are absent: they are accepted and
    /// discarded, and a refusal should not offer them as a remedy.
    /// </remarks>
    public static IEnumerable<string> Spellings => Enum.GetValues<TypeValue>().Select(Spell);

    /// <summary>The Section 16.6 spelling of one value.</summary>
    /// <param name="value">The value to spell.</param>
    public static string Spell(TypeValue value) => value switch
    {
        TypeValue.Default => "default",
        TypeValue.Ignore => "ignore",
        TypeValue.Array => "array",
        TypeValue.Mapping => "mapping",
        TypeValue.Quoted => "string",
        TypeValue.Element => "element",
        TypeValue.Attribute => "attribute",
        TypeValue.Text => "text",
        TypeValue.Cdata => "cdata",
        TypeValue.Multiline => "multiline",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value),
            value,
            "Section 16.6 lists ten type values, and this is not one of them."),
    };

    /// <summary>Whether a token is one of the Section 15.3 legacy no-op type values.</summary>
    /// <param name="token">The written token, already trimmed.</param>
    public static bool IsLegacyNoOp(string token) =>
        string.Equals(token, "xmlns", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "xmlnssuffix", StringComparison.OrdinalIgnoreCase);

    private static bool TryRecognize(string token, out TypeValue value)
    {
        foreach (var candidate in Enum.GetValues<TypeValue>())
        {
            if (string.Equals(token, Spell(candidate), StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    // Section 16.6's own list, in its own order, plus the single values it admits implicitly by
    // constraining where each may appear. Membership is tested against the written order because a
    // set written 'string,multiline' is the same set as 'multiline,string' and Section 16.6 lists
    // only one spelling of each.
    private static readonly TypeValue[][] LegalCombinations =
    [
        [TypeValue.Default],
        [TypeValue.Ignore],
        [TypeValue.Array],
        [TypeValue.Mapping],
        [TypeValue.Quoted],
        [TypeValue.Element],
        [TypeValue.Attribute],
        [TypeValue.Text],
        [TypeValue.Cdata],
        [TypeValue.Multiline],
        [TypeValue.Multiline, TypeValue.Quoted],
        [TypeValue.Multiline, TypeValue.Text],
        [TypeValue.Multiline, TypeValue.Cdata],
        [TypeValue.Array, TypeValue.Multiline],
        [TypeValue.Array, TypeValue.Multiline, TypeValue.Quoted],
        [TypeValue.Array, TypeValue.Multiline, TypeValue.Text],
        [TypeValue.Array, TypeValue.Multiline, TypeValue.Cdata],
        [TypeValue.Quoted, TypeValue.Attribute],
        [TypeValue.Quoted, TypeValue.Element],
        [TypeValue.Quoted, TypeValue.Text],
        [TypeValue.Quoted, TypeValue.Cdata],
    ];

    private static bool IsLegalCombination(List<TypeValue> parsed) =>
        LegalCombinations.Any(legal =>
            legal.Length == parsed.Count && legal.All(parsed.Contains));
}
