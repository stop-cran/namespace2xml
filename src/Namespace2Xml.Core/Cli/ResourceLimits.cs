namespace Namespace2Xml.Cli;

/// <summary>
/// The resource bounds of specification Section 6.2, with the defaults that section fixes.
/// </summary>
/// <remarks>
/// <para>
/// Every bound is an <see cref="long"/> because Section 6.2 admits values up to the signed 64-bit
/// range and makes anything beyond it <c>CLI001</c>. Byte bounds are semantic byte counts: the
/// defaults are written here as products so that the IEC spelling in the specification table stays
/// legible, and so that "256 MiB" cannot silently become 256,000,000.
/// </para>
/// <para>
/// Exceeding a bound at run time is <c>LIMIT001</c>, not <c>CLI001</c>. A malformed value on the
/// command line is <c>CLI001</c>. Section 22 states that split explicitly.
/// </para>
/// </remarks>
public sealed record ResourceLimits
{
    /// <summary>The Section 6.2 defaults, used for every bound the caller does not override.</summary>
    public static ResourceLimits Defaults { get; } = new();

    /// <summary>Maximum bytes per input file. Default 256 MiB.</summary>
    public long MaxInputBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>Maximum total input bytes. Default 1 GiB.</summary>
    public long MaxTotalInputBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>Maximum document or qualified-path depth. Default 512.</summary>
    public long MaxDepth { get; init; } = 512;

    /// <summary>Maximum parsed nodes before generated entries. Default 10,000,000.</summary>
    public long MaxNodes { get; init; } = 10_000_000;

    /// <summary>Maximum attributes on one XML element. Default 4,096.</summary>
    public long MaxXmlAttributes { get; init; } = 4_096;

    /// <summary>Maximum retained comments. Default 1,000,000.</summary>
    public long MaxComments { get; init; } = 1_000_000;

    /// <summary>Maximum total decoded comment bytes. Default 256 MiB.</summary>
    public long MaxCommentBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>Maximum wildcard rules. Default 100,000.</summary>
    public long MaxWildcardRules { get; init; } = 100_000;

    /// <summary>Maximum <c>(rule,item)</c> candidate checks. Default 100,000,000.</summary>
    public long MaxWildcardCandidates { get; init; } = 100_000_000;

    /// <summary>Maximum wildcard-generated nodes. Default 10,000,000.</summary>
    public long MaxGenerated { get; init; } = 10_000_000;

    /// <summary>Maximum fixed-point iterations. Default 1,024.</summary>
    public long MaxWildcardIterations { get; init; } = 1_024;

    /// <summary>Maximum reference recursion depth. Default 4,096.</summary>
    public long MaxReferenceDepth { get; init; } = 4_096;

    /// <summary>Maximum planned destination files. Default 100,000.</summary>
    public long MaxOutputs { get; init; } = 100_000;

    /// <summary>Maximum total staged output bytes. Default 4 GiB.</summary>
    public long MaxTotalOutputBytes { get; init; } = 4L * 1024 * 1024 * 1024;
}
