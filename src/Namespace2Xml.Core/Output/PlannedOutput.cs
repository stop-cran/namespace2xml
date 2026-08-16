using System.Collections.Immutable;
using Namespace2Xml.Overlay;

namespace Namespace2Xml.Output;

/// <summary>
/// The Section 17.5 fold key of an output contribution: "output-declaration source order, format
/// ordinal, wildcard match order, and concrete-selector UTF-8 byte order".
/// </summary>
/// <param name="DeclarationOrder">Source order of the <c>output</c> declaration.</param>
/// <param name="FormatOrdinal">Position within one comma-separated <c>output</c> value.</param>
/// <param name="WildcardMatchOrder">Match order of the wildcard rule that produced the contribution.</param>
/// <param name="Selector">The concrete selector, compared as unsigned UTF-8 bytes.</param>
/// <remarks>
/// The four components are ordered from coarsest to finest, and the last is a total order over
/// distinct selectors, so the whole key is total: two distinct contributions can never tie and
/// leave the fold to decide by arrival.
/// </remarks>
public readonly record struct FoldKey(
    long DeclarationOrder,
    int FormatOrdinal,
    long WildcardMatchOrder,
    string Selector) : IComparable<FoldKey>
{
    /// <inheritdoc/>
    public int CompareTo(FoldKey other)
    {
        var byDeclaration = DeclarationOrder.CompareTo(other.DeclarationOrder);

        if (byDeclaration != 0)
        {
            return byDeclaration;
        }

        var byFormat = FormatOrdinal.CompareTo(other.FormatOrdinal);

        if (byFormat != 0)
        {
            return byFormat;
        }

        var byWildcard = WildcardMatchOrder.CompareTo(other.WildcardMatchOrder);

        return byWildcard != 0
            ? byWildcard
            : Utf8Order.Compare(Selector, other.Selector);
    }

    /// <summary>Orders two keys.</summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    public static bool operator <(FoldKey left, FoldKey right) => left.CompareTo(right) < 0;

    /// <summary>Orders two keys.</summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    public static bool operator >(FoldKey left, FoldKey right) => left.CompareTo(right) > 0;

    /// <summary>Orders two keys.</summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    public static bool operator <=(FoldKey left, FoldKey right) => left.CompareTo(right) <= 0;

    /// <summary>Orders two keys.</summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    public static bool operator >=(FoldKey left, FoldKey right) => left.CompareTo(right) >= 0;
}

/// <summary>One destination's complete serialized bytes and its Section 21.3 publication key.</summary>
/// <param name="Path">The Section 17.5 canonical destination path.</param>
/// <param name="PublicationKey">
/// The minimum Section 17.5 fold key among the contributions that survive into the final folded
/// plan for this destination.
/// </param>
/// <param name="Buffer">The complete byte buffer, which Section 21.2 requires to exist before publication.</param>
public sealed record PlannedOutput(
    DestinationPath Path,
    FoldKey PublicationKey,
    OutputBuffer Buffer)
{
    /// <summary>
    /// Section 21.3's destination order: by publication key, then by canonical relative path
    /// compared as unsigned UTF-8 bytes.
    /// </summary>
    /// <param name="outputs">The planned outputs, in any order.</param>
    /// <remarks>
    /// The path tie-break is not decoration. Two destinations can share a publication key when one
    /// declaration writes both, and without a total order the write order would depend on the
    /// dictionary that happened to hold them.
    /// </remarks>
    public static ImmutableArray<PlannedOutput> InPublicationOrder(
        IEnumerable<PlannedOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);

        return
        [
            .. outputs
                .OrderBy(output => output.PublicationKey)
                .ThenBy(output => output.Path, DestinationPath.Utf8Bytes)
        ];
    }
}
