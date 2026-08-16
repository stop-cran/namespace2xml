namespace Namespace2Xml.Budgets;

/// <summary>
/// The Section 23 configurable bounds, named so that a crossing can be reported and ordered.
/// </summary>
/// <remarks>
/// Section 11.1 breaks a tie between two bounds crossed at one position by "the bound name compared as
/// unsigned UTF-8 bytes", so the spelling in <see cref="ResourceBoundNames.Spelling"/> is normative
/// for ordering and not merely cosmetic.
/// </remarks>
public enum ResourceBound
{
    /// <summary>Per-source: bytes in one input file, scheme file, or command-line variable.</summary>
    MaxInputBytes,

    /// <summary>Per-source: document or qualified-path nesting depth.</summary>
    MaxDepth,

    /// <summary>Per-source: attributes on one XML element.</summary>
    MaxXmlAttributes,

    /// <summary>Global: total input bytes across the whole ordered input stream.</summary>
    MaxTotalInputBytes,

    /// <summary>Global: parsed nodes before generated entries.</summary>
    MaxNodes,

    /// <summary>Global: retained comments.</summary>
    MaxComments,

    /// <summary>Global: total decoded comment bytes.</summary>
    MaxCommentBytes,

    /// <summary>Pipeline: wildcard rules.</summary>
    MaxWildcardRules,

    /// <summary>Pipeline: <c>(rule,item)</c> candidate checks.</summary>
    MaxWildcardCandidates,

    /// <summary>Pipeline: wildcard-generated nodes.</summary>
    MaxGenerated,

    /// <summary>Pipeline: fixed-point iterations.</summary>
    MaxWildcardIterations,

    /// <summary>Pipeline: reference recursion depth.</summary>
    MaxReferenceDepth,

    /// <summary>Pipeline: planned destination files.</summary>
    MaxOutputs,

    /// <summary>Pipeline: total staged output bytes.</summary>
    MaxTotalOutputBytes,
}

/// <summary>The Section 6.2 command-line spelling of each bound.</summary>
public static class ResourceBoundNames
{
    /// <summary>The option spelling Section 6.2 gives a bound, including its leading hyphens.</summary>
    /// <param name="bound">The bound to name.</param>
    /// <returns>The spelling, which Section 11.1 compares as unsigned UTF-8 bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The bound is not a declared member.</exception>
    public static string Spelling(ResourceBound bound) => bound switch
    {
        ResourceBound.MaxInputBytes => "--max-input-bytes",
        ResourceBound.MaxDepth => "--max-depth",
        ResourceBound.MaxXmlAttributes => "--max-xml-attributes",
        ResourceBound.MaxTotalInputBytes => "--max-total-input-bytes",
        ResourceBound.MaxNodes => "--max-nodes",
        ResourceBound.MaxComments => "--max-comments",
        ResourceBound.MaxCommentBytes => "--max-comment-bytes",
        ResourceBound.MaxWildcardRules => "--max-wildcard-rules",
        ResourceBound.MaxWildcardCandidates => "--max-wildcard-candidates",
        ResourceBound.MaxGenerated => "--max-generated",
        ResourceBound.MaxWildcardIterations => "--max-wildcard-iterations",
        ResourceBound.MaxReferenceDepth => "--max-reference-depth",
        ResourceBound.MaxOutputs => "--max-outputs",
        ResourceBound.MaxTotalOutputBytes => "--max-total-output-bytes",
        _ => throw new ArgumentOutOfRangeException(nameof(bound)),
    };
}
