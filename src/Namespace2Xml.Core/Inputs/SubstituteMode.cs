using System.Collections.Immutable;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scheme;

namespace Namespace2Xml.Inputs;

/// <summary>
/// The Section 16.7 substitution modes.
/// </summary>
/// <remarks>
/// The four modes are the two independent questions of Section 16.7's table — whether names are
/// interpreted and whether values are — enumerated rather than combined into flags. Flags would
/// admit spellings the directive does not have: <c>Enum.TryParse</c> accepts a comma-separated
/// list for any enumeration, and on a <c>[Flags]</c> shape <c>substitute=Key,Value</c> would parse
/// to <c>All</c> and pass <c>Enum.IsDefined</c>. Section 16.7 offers exactly four names.
/// </remarks>
public enum SubstituteMode
{
    /// <summary>Names and values are both interpreted. The mode where nothing is declared.</summary>
    All,

    /// <summary>
    /// Section 13.4: names retain wildcard interpretation and values are interpreted in neither
    /// references nor wildcards. Also written <c>keyOnly</c>, the Section 15.3 deprecated alias.
    /// </summary>
    Key,

    /// <summary>Values are interpreted; names are not.</summary>
    Value,

    /// <summary>Section 13.4: interpretation is disabled in both names and values.</summary>
    None,
}

/// <summary>Recognizes a Section 16.7 mode name.</summary>
public static class SubstituteModes
{
    // Section 15 matches "every other name and value in the scheme language", substitute modes
    // included, under ASCII case-insensitive comparison. The table is written in the
    // specification's own spelling and looked up with an ordinal ignore-case comparer, exactly as
    // SchemeDirectives does for directive names, so no mode changes meaning with the host locale.
    private static readonly Dictionary<string, (SubstituteMode Mode, SchemeAlias Alias)> Names =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["all"] = (SubstituteMode.All, SchemeAlias.None),
            ["key"] = (SubstituteMode.Key, SchemeAlias.None),
            ["value"] = (SubstituteMode.Value, SchemeAlias.None),
            ["none"] = (SubstituteMode.None, SchemeAlias.None),
            ["keyonly"] = (SubstituteMode.Key, SchemeAlias.KeyOnly),
        };

    /// <summary>Recognizes one mode name.</summary>
    /// <param name="name">The written directive value, already trimmed.</param>
    /// <param name="mode">The mode the name identifies.</param>
    /// <param name="alias">The Section 15.3 alias category the spelling belongs to.</param>
    /// <returns>Whether the name is recognized; an unrecognized one is <c>SCHEME001</c>.</returns>
    public static bool TryRecognize(string name, out SubstituteMode mode, out SchemeAlias alias)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Names.TryGetValue(name, out var found))
        {
            (mode, alias) = found;
            return true;
        }

        mode = default;
        alias = SchemeAlias.None;
        return false;
    }

    /// <summary>Whether Section 16.7 interprets names under a mode.</summary>
    /// <param name="mode">The effective mode.</param>
    /// <remarks>
    /// Interpreting a name means recognizing its wildcard tokens, which is the only interpretation
    /// a name has: Section 8.4 references are value syntax and cannot appear in one.
    /// </remarks>
    public static bool InterpretsNames(this SubstituteMode mode) =>
        mode is SubstituteMode.All or SubstituteMode.Key;

    /// <summary>Whether Section 16.7 interprets values under a mode.</summary>
    /// <param name="mode">The effective mode.</param>
    public static bool InterpretsValues(this SubstituteMode mode) =>
        mode is SubstituteMode.All or SubstituteMode.Value;
}

/// <summary>
/// The effective Section 16.7 mode at each declared path.
/// </summary>
/// <remarks>
/// <para>
/// Section 15.1 step 6 resolves the mode against "an entry's declared pre-expansion path", and
/// Section 15.2 settles precedence: "All scheme directives follow source order only", "a later
/// matching directive overrides an earlier matching directive", and "pattern specificity does not
/// alter precedence". The declarations are therefore kept in source order and scanned from the end,
/// rather than indexed by specificity.
/// </para>
/// <para>
/// Matching is component-for-component over the whole declared path, so a directive at an ancestor
/// does not reach a descendant. Section 16.7 does not say so in the way Section 16.10 does for
/// <c>merge</c> — "a <c>merge</c> directive governs only the node it matches" — but three
/// independent readings agree. Sections 8.4, 10.1 and 10.2 each say a "<em>matching</em>
/// <c>substitute</c> directive" and never mention an ancestor's; Section 16.10 is the sibling
/// input-phase directive and is node-scoped explicitly; and the 2.x implementation this directive
/// is inherited from resolved it through a dictionary whose lookup consumed the whole name and
/// returned no match once the pattern ran out of components.
/// </para>
/// <para>
/// A declaration written with no path at all is kept as a null pattern that matches every path,
/// which is what makes Section 16.7's <c>[path.]</c> optionality mean anything. Under node scoping
/// the alternative reading — that it governs the overlay root alone — would be dead syntax, because
/// Appendix A.3 gives every value a name of at least one component and the root therefore never
/// carries one. Section 16.8 sets the precedent within the same section: a directive written
/// without a path configures the whole run.
/// </para>
/// </remarks>
public sealed class SubstituteModeMap
{
    private readonly ImmutableArray<(QualifiedName? Pattern, SubstituteMode Mode)> declarations;

    private SubstituteModeMap(
        ImmutableArray<(QualifiedName? Pattern, SubstituteMode Mode)> declarations) =>
        this.declarations = declarations;

    /// <summary>The map in which every path uses the Section 16.7 default.</summary>
    public static SubstituteModeMap Default { get; } = new([]);

    /// <summary>Whether no <c>substitute</c> directive was declared at all.</summary>
    /// <remarks>
    /// Every caller reads a mode per entry, so the common scheme that declares none is worth
    /// answering once rather than by scanning an empty array for every name in every input.
    /// </remarks>
    public bool IsEmpty => declarations.IsEmpty;

    /// <summary>Builds a map from compiled <c>substitute</c> directives.</summary>
    /// <param name="declarations">
    /// Each directive's path pattern and mode, in source order. A null pattern is a directive
    /// written with no path.
    /// </param>
    public static SubstituteModeMap Create(
        IEnumerable<(QualifiedName? Pattern, SubstituteMode Mode)> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var ordered = declarations.ToImmutableArray();

        return ordered.IsEmpty ? Default : new SubstituteModeMap(ordered);
    }

    /// <summary>The effective mode for one entry.</summary>
    /// <param name="declared">
    /// The entry's declared pre-expansion path, or <see langword="null"/> for a native document's
    /// root scalar, which has no path for a pattern to name.
    /// </param>
    /// <remarks>
    /// A declared path that is itself a Section 12 template is matched by its spelling: a wildcard
    /// component contributes the characters that wrote it, so <c>a.*.b.substitute=None</c> names the
    /// entry <c>a.*.b=1</c>. Any other reading makes the name column of Section 16.7's table
    /// unreachable — the only entries whose names have anything to interpret are exactly the ones a
    /// structural match would refuse — and it is also what the 2.x implementation did, matching a
    /// component's rendered text rather than its tokens.
    /// </remarks>
    public SubstituteMode For(QualifiedName? declared)
    {
        if (declarations.IsEmpty)
        {
            return SubstituteMode.All;
        }

        var subject = declared is not null && QualifiedNameLexer.ContainsWildcard(declared)
            ? QualifiedNameLexer.Literalize(declared)
            : declared;

        for (var i = declarations.Length - 1; i >= 0; i--)
        {
            var (pattern, mode) = declarations[i];

            if (pattern is null
                || (subject is not null && WildcardMatch.TryMatch(pattern, subject, out _)))
            {
                return mode;
            }
        }

        return SubstituteMode.All;
    }
}
