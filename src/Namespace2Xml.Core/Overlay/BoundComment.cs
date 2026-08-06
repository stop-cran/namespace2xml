namespace Namespace2Xml.Overlay;

/// <summary>
/// Where a comment sat relative to the value it belongs to, for the source formats that
/// distinguish the positions (Section 4.5).
/// </summary>
public enum CommentPlacement
{
    /// <summary>Bound during parsing to the entry or item that immediately follows it.</summary>
    Leading,

    /// <summary>On the same logical line as the entry or item it belongs to.</summary>
    Inline,

    /// <summary>Bound to the entry or item that immediately precedes it.</summary>
    Trailing,
}

/// <summary>
/// A Section 4.5 comment bound to a logical path or sequence item.
/// </summary>
/// <param name="Text">The comment text. Section 4.5 does not require surrounding whitespace to survive.</param>
/// <param name="Placement">Where the comment sat relative to its owner.</param>
/// <param name="Order">
/// The Section 4.7 key of the comment itself, which is how comments contributed to one surviving
/// path "accumulate in source order".
/// </param>
/// <remarks>
/// A comment does not record the path it is bound to, because it is stored on the node that owns
/// it. Section 4.5 requires that overriding a payload "does not detach comments already bound to
/// that logical path", and that the winning contribution's position carries its comments with it —
/// both of which hold structurally when the comment lives on the node, and would need enforcing if
/// it carried its own copy of an address that merging can change.
/// </remarks>
public sealed record BoundComment(string Text, CommentPlacement Placement, StableOrderingKey Order)
{
    /// <summary>
    /// Orders comments bound to one owner. Section 4.5 accumulates them in source order; the text
    /// breaks a tie so that the order is total for the same reason Section 5.2 needs one.
    /// </summary>
    public static IComparer<BoundComment> SourceOrder { get; } = new Comparer();

    private sealed class Comparer : IComparer<BoundComment>
    {
        public int Compare(BoundComment? x, BoundComment? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var result = x.Order.CompareTo(y.Order);

            if (result != 0)
            {
                return result;
            }

            result = x.Placement.CompareTo(y.Placement);

            return result != 0 ? result : Utf8Order.Compare(x.Text, y.Text);
        }
    }
}
