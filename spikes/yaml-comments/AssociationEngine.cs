using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace YamlCommentSpike;

internal static class AssociationEngine
{
    public static IReadOnlyDictionary<string, CommentAssociation> Associate(SpikeDocument document)
    {
        var anchors = BuildAnchors(document);
        var result = new Dictionary<string, CommentAssociation>(StringComparer.Ordinal);

        foreach (var comment in document.ScannerComments)
        {
            result.Add(comment.Id, AssociateOne(document, anchors, comment));
        }

        return result;
    }

    private static CommentAssociation AssociateOne(
        SpikeDocument document,
        IReadOnlyList<OwnerAnchor> anchors,
        SourceComment comment)
    {
        if (document.HasExplicitDocumentMarker)
        {
            return new CommentAssociation(CommentAssociationKind.RejectedSyntax, null);
        }

        if (document.Root is null)
        {
            return new CommentAssociation(CommentAssociationKind.DocumentOnly, null);
        }

        var sameLineAnchor = anchors
            .Where(anchor => anchor.Mark.Line == comment.Start.Line
                && anchor.Mark.Index <= comment.Start.Index)
            .OrderByDescending(static anchor => anchor.Mark.Index)
            .ThenByDescending(static anchor => anchor.EventOrdinal)
            .ThenByDescending(static anchor => anchor.Depth)
            .FirstOrDefault();

        if (sameLineAnchor is not null)
        {
            return new CommentAssociation(
                CommentAssociationKind.Inline,
                sameLineAnchor.OwnerPath);
        }

        if (comment.ScannerIsInline)
        {
            var dashOnlyItem = document.Entries
                .Where(entry => entry.Kind == EntryKind.SequenceItem
                    && entry.Value.StartRecord.Event.Start.Index > comment.Start.Index
                    && IsInside(comment.Start.Index, entry.ParentCollection))
                .OrderBy(static entry => entry.Value.StartRecord.Event.Start.Index)
                .FirstOrDefault();

            if (dashOnlyItem is not null)
            {
                return new CommentAssociation(
                    CommentAssociationKind.Inline,
                    dashOnlyItem.Path);
            }
        }

        if (comment.Start.Index < document.Root.StartRecord.Event.Start.Index)
        {
            return new CommentAssociation(CommentAssociationKind.DocumentLeading, null);
        }

        var nextEntry = document.Entries
            .Where(entry => entry.HeaderIndex > comment.End.Index
                && entry.HeaderColumn >= comment.Start.Column
                && IsPlausiblyInParent(comment, entry))
            .OrderBy(static entry => entry.HeaderIndex)
            .ThenBy(static entry => entry.Depth)
            .FirstOrDefault();

        if (nextEntry is not null)
        {
            return new CommentAssociation(CommentAssociationKind.Leading, nextEntry.Path);
        }

        var pendingValueEntry = document.Entries
            .Where(entry =>
                (entry.HeaderEndIndex < comment.Start.Index
                    || entry.Kind == EntryKind.SequenceItem)
                && entry.Value.StartRecord.Event.Start.Index > comment.End.Index
                && entry.Value.StartRecord.Event.Start.Column >= comment.Start.Column
                && (entry.Kind != EntryKind.SequenceItem
                    || IsInside(comment.Start.Index, entry.ParentCollection)))
            .OrderBy(static entry => entry.Value.StartRecord.Event.Start.Index)
            .FirstOrDefault();

        if (pendingValueEntry is not null)
        {
            return new CommentAssociation(
                CommentAssociationKind.Leading,
                pendingValueEntry.Path);
        }

        var containingCollection = document.Collections
            .Where(collection => IsInside(comment.Start.Index, collection)
                && collection.Entries.Any(entry => entry.HeaderColumn <= comment.Start.Column))
            .OrderByDescending(CollectionDepth)
            .FirstOrDefault();

        if (containingCollection is not null)
        {
            if (containingCollection.OwnerEntry is null)
            {
                return new CommentAssociation(CommentAssociationKind.DocumentTrailing, null);
            }

            var previousEntry = containingCollection.Entries
                .Where(entry => entry.HeaderColumn <= comment.Start.Column
                    && entry.HeaderIndex < comment.Start.Index)
                .OrderByDescending(static entry => entry.HeaderIndex)
                .FirstOrDefault();

            if (previousEntry is not null)
            {
                return new CommentAssociation(
                    CommentAssociationKind.Trailing,
                    previousEntry.Path);
            }
        }

        if (comment.Start.Index >= ContentEndIndex(document.Root))
        {
            return new CommentAssociation(CommentAssociationKind.DocumentTrailing, null);
        }

        return new CommentAssociation(CommentAssociationKind.Unresolved, null);
    }

    private static IReadOnlyList<OwnerAnchor> BuildAnchors(SpikeDocument document)
    {
        var anchors = new List<OwnerAnchor>();
        if (document.Root is not null)
        {
            AddNodeAnchors(anchors, document.Root, "$", -1);
        }

        foreach (var entry in document.Entries)
        {
            if (entry.Key is not null)
            {
                anchors.Add(
                    new OwnerAnchor(
                        entry.Path,
                        entry.Depth,
                        entry.Key.EndRecord.Event.End,
                        entry.Key.EndRecord.Ordinal));
            }

            AddNodeAnchors(anchors, entry.Value, entry.Path, entry.Depth);
        }

        return anchors;
    }

    private static void AddNodeAnchors(
        ICollection<OwnerAnchor> anchors,
        ParsedNode node,
        string ownerPath,
        int depth)
    {
        anchors.Add(
            new OwnerAnchor(
                ownerPath,
                depth,
                node.StartRecord.Event.Start,
                node.StartRecord.Ordinal));

        if (HasLexicalTerminal(node))
        {
            var terminalMark = node.IsCollection
                ? node.EndRecord.Event.Start
                : node.EndRecord.Event.End;
            anchors.Add(
                new OwnerAnchor(
                    ownerPath,
                    depth,
                    terminalMark,
                    node.EndRecord.Ordinal));
        }
    }

    private static bool HasLexicalTerminal(ParsedNode node)
    {
        if (node.Kind == NodeKind.Alias)
        {
            return true;
        }

        if (node.Kind == NodeKind.Scalar)
        {
            var start = node.StartRecord.Event.Start;
            var end = node.EndRecord.Event.End;
            return start.Line == end.Line || end.Column > 1;
        }

        return node.Style is MappingStyle.Flow or SequenceStyle.Flow;
    }

    private static bool IsPlausiblyInParent(
        SourceComment comment,
        AssociationEntry nextEntry)
    {
        var parent = nextEntry.ParentCollection;
        if (IsInside(comment.Start.Index, parent))
        {
            return true;
        }

        return ReferenceEquals(parent.Entries[0], nextEntry)
            && parent.OwnerEntry is not null
            && comment.Start.Index > parent.OwnerEntry.HeaderEndIndex
            && comment.Start.Index < nextEntry.HeaderIndex;
    }

    private static bool IsInside(long index, ParsedNode collection) =>
        collection.StartRecord.Event.Start.Index <= index
        && index < collection.EndRecord.Event.Start.Index;

    private static int CollectionDepth(ParsedNode collection) =>
        collection.Entries.Count == 0
            ? 0
            : collection.Entries.Min(static entry => entry.Depth);

    private static long ContentEndIndex(ParsedNode node)
    {
        if (!node.IsCollection)
        {
            return node.EndRecord.Event.End.Index;
        }

        return node.Entries.Count == 0
            ? node.StartRecord.Event.Start.Index
            : node.Entries.Max(static entry => ContentEndIndex(entry.Value));
    }
}

internal sealed record OwnerAnchor(
    string OwnerPath,
    int Depth,
    Mark Mark,
    int EventOrdinal);

internal sealed record CommentAssociation(
    CommentAssociationKind Kind,
    string? OwnerPath);

internal enum CommentAssociationKind
{
    Leading,
    Inline,
    Trailing,
    DocumentLeading,
    DocumentTrailing,
    DocumentOnly,
    RejectedSyntax,
    Unresolved,
}
