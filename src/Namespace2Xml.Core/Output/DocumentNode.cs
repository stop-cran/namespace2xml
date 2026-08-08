using System.Collections.Immutable;
using Namespace2Xml.Overlay;

namespace Namespace2Xml.Output;

/// <summary>
/// One node of the ordered document Section 19.3 and Section 19.4 render.
/// </summary>
/// <remarks>
/// <para>
/// Section 4.4 makes JSON and YAML destinations that "require one exclusive shape", so a document
/// node is a scalar, a mapping, or a sequence and never two of them at once. That is the difference
/// from <see cref="FlatEntry"/>, where a path may legitimately carry both a scalar and children.
/// </para>
/// <para>
/// These are classes rather than records deliberately. A record holding an
/// <see cref="ImmutableArray{T}"/> compares that member by reference, so two identically-populated
/// documents would compare unequal — an equality that is worse than none, because it looks
/// structural.
/// </para>
/// </remarks>
public abstract class DocumentNode
{
    private protected DocumentNode(ImmutableArray<BoundComment> comments) =>
        Comments = comments.IsDefault ? [] : comments;

    /// <summary>The node's comments, in Section 4.5 source order.</summary>
    public ImmutableArray<BoundComment> Comments { get; }
}

/// <summary>A scalar or null document node.</summary>
public sealed class DocumentScalar : DocumentNode
{
    /// <summary>Creates a scalar node.</summary>
    /// <param name="payload">The scalar being emitted.</param>
    /// <param name="comments">The node's comments, in Section 4.5 source order.</param>
    public DocumentScalar(ScalarPayload payload, ImmutableArray<BoundComment> comments)
        : base(comments) => Payload = payload;

    /// <summary>The scalar being emitted.</summary>
    public ScalarPayload Payload { get; }
}

/// <summary>One key and value of a <see cref="DocumentMapping"/>.</summary>
/// <param name="Key">The Section 19.3 literal key text.</param>
/// <param name="Value">The member's value.</param>
public readonly record struct DocumentMember(string Key, DocumentNode Value);

/// <summary>A mapping document node, in Section 5.2 order.</summary>
public sealed class DocumentMapping : DocumentNode
{
    /// <summary>Creates a mapping node.</summary>
    /// <param name="members">The members, in Section 5.2 order.</param>
    /// <param name="comments">The node's comments, in Section 4.5 source order.</param>
    public DocumentMapping(ImmutableArray<DocumentMember> members, ImmutableArray<BoundComment> comments)
        : base(comments) => Members = members.IsDefault ? [] : members;

    /// <summary>The members, in Section 5.2 order.</summary>
    public ImmutableArray<DocumentMember> Members { get; }
}

/// <summary>A sequence document node, in ascending Section 5.4 ordering value.</summary>
public sealed class DocumentSequence : DocumentNode
{
    /// <summary>Creates a sequence node.</summary>
    /// <param name="items">The items, in ascending ordering value.</param>
    /// <param name="comments">The node's comments, in Section 4.5 source order.</param>
    public DocumentSequence(ImmutableArray<DocumentNode> items, ImmutableArray<BoundComment> comments)
        : base(comments) => Items = items.IsDefault ? [] : items;

    /// <summary>The items, in ascending ordering value.</summary>
    public ImmutableArray<DocumentNode> Items { get; }
}
