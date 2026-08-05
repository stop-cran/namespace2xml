using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using EventComment = YamlDotNet.Core.Events.Comment;

namespace YamlCommentSpike;

internal sealed class EventTreeBuilder
{
    private readonly IReadOnlyList<EventRecord> structuralEvents;
    private readonly List<AssociationEntry> entries = [];
    private readonly List<ParsedNode> collections = [];
    private int position;

    public EventTreeBuilder(IReadOnlyList<EventRecord> parserEvents)
    {
        structuralEvents = parserEvents
            .Where(static record => record.Event is not EventComment
                and not StreamStart
                and not StreamEnd
                and not DocumentStart
                and not DocumentEnd)
            .ToArray();
    }

    public IReadOnlyList<AssociationEntry> Entries => entries;

    public IReadOnlyList<ParsedNode> Collections => collections;

    public ParsedNode? Build()
    {
        if (structuralEvents.Count == 0)
        {
            return null;
        }

        return ParseNode("$", 0);
    }

    private ParsedNode ParseNode(string path, int childEntryDepth)
    {
        var current = structuralEvents[position];

        switch (current.Event)
        {
            case Scalar scalar:
                position++;
                return ParsedNode.CreateScalar(current, scalar.Style);

            case AnchorAlias:
                position++;
                return ParsedNode.CreateAlias(current);

            case MappingStart mappingStart:
                return ParseMapping(path, childEntryDepth, current, mappingStart.Style);

            case SequenceStart sequenceStart:
                return ParseSequence(path, childEntryDepth, current, sequenceStart.Style);

            default:
                throw new InvalidOperationException(
                    $"Unexpected {current.Event.GetType().Name} at structural event {position}.");
        }
    }

    private ParsedNode ParseMapping(
        string path,
        int childEntryDepth,
        EventRecord start,
        MappingStyle style)
    {
        position++;
        var mapping = ParsedNode.CreateCollection(NodeKind.Mapping, start, style);
        collections.Add(mapping);

        while (position < structuralEvents.Count
            && structuralEvents[position].Event is not MappingEnd)
        {
            var key = ParseNode(path, childEntryDepth + 1);
            var keyText = key.StartRecord.Event is Scalar scalar ? scalar.Value : "<complex-key>";
            var childPath = AppendKey(path, keyText);
            var value = ParseNode(childPath, childEntryDepth + 1);
            var entry = new AssociationEntry(
                childPath,
                EntryKind.MappingEntry,
                childEntryDepth,
                key,
                value,
                mapping,
                key.StartRecord.Event.Start.Index,
                key.StartRecord.Event.Start.Line,
                key.StartRecord.Event.Start.Column,
                key.EndRecord.Event.End.Index);

            value.OwnerEntry = entry;
            mapping.Entries.Add(entry);
            entries.Add(entry);
        }

        mapping.EndRecord = structuralEvents[position++];
        return mapping;
    }

    private ParsedNode ParseSequence(
        string path,
        int childEntryDepth,
        EventRecord start,
        SequenceStyle style)
    {
        position++;
        var sequence = ParsedNode.CreateCollection(NodeKind.Sequence, start, style);
        collections.Add(sequence);
        var itemIndex = 0;

        while (position < structuralEvents.Count
            && structuralEvents[position].Event is not SequenceEnd)
        {
            var itemPath = $"{path}[{itemIndex}]";
            var value = ParseNode(itemPath, childEntryDepth + 1);
            var isFlow = style == SequenceStyle.Flow;
            var entry = new AssociationEntry(
                itemPath,
                EntryKind.SequenceItem,
                childEntryDepth,
                null,
                value,
                sequence,
                value.StartRecord.Event.Start.Index,
                value.StartRecord.Event.Start.Line,
                isFlow ? value.StartRecord.Event.Start.Column : start.Event.Start.Column,
                value.StartRecord.Event.Start.Index);

            value.OwnerEntry = entry;
            sequence.Entries.Add(entry);
            entries.Add(entry);
            itemIndex++;
        }

        sequence.EndRecord = structuralEvents[position++];
        return sequence;
    }

    private static string AppendKey(string path, string key)
    {
        if (key.Length > 0 && key.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
        {
            return $"{path}.{key}";
        }

        return $"{path}['{key.Replace("'", "''", StringComparison.Ordinal)}']";
    }
}

internal enum NodeKind
{
    Scalar,
    Alias,
    Mapping,
    Sequence,
}

internal enum EntryKind
{
    MappingEntry,
    SequenceItem,
}

internal sealed class ParsedNode
{
    private ParsedNode(
        NodeKind kind,
        EventRecord startRecord,
        EventRecord endRecord,
        object? style)
    {
        Kind = kind;
        StartRecord = startRecord;
        EndRecord = endRecord;
        Style = style;
    }

    public NodeKind Kind { get; }

    public EventRecord StartRecord { get; }

    public EventRecord EndRecord { get; set; }

    public object? Style { get; }

    public AssociationEntry? OwnerEntry { get; set; }

    public List<AssociationEntry> Entries { get; } = [];

    public bool IsCollection => Kind is NodeKind.Mapping or NodeKind.Sequence;

    public static ParsedNode CreateScalar(EventRecord record, ScalarStyle style) =>
        new(NodeKind.Scalar, record, record, style);

    public static ParsedNode CreateAlias(EventRecord record) =>
        new(NodeKind.Alias, record, record, null);

    public static ParsedNode CreateCollection(
        NodeKind kind,
        EventRecord startRecord,
        object style) =>
        new(kind, startRecord, startRecord, style);
}

internal sealed record AssociationEntry(
    string Path,
    EntryKind Kind,
    int Depth,
    ParsedNode? Key,
    ParsedNode Value,
    ParsedNode ParentCollection,
    long HeaderIndex,
    long HeaderLine,
    long HeaderColumn,
    long HeaderEndIndex);
