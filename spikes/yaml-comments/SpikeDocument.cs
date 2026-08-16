using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using EventComment = YamlDotNet.Core.Events.Comment;
using TokenComment = YamlDotNet.Core.Tokens.Comment;

namespace YamlCommentSpike;

internal sealed class SpikeDocument
{
    private SpikeDocument(
        string path,
        string yaml,
        IReadOnlyList<EventRecord> parserEvents,
        IReadOnlyList<SourceComment> scannerComments,
        ParsedNode? root,
        IReadOnlyList<AssociationEntry> entries,
        IReadOnlyList<ParsedNode> collections,
        bool hasExplicitDocumentMarker)
    {
        Path = path;
        Yaml = yaml;
        ParserEvents = parserEvents;
        ScannerComments = scannerComments;
        Root = root;
        Entries = entries;
        Collections = collections;
        HasExplicitDocumentMarker = hasExplicitDocumentMarker;
    }

    public string Path { get; }

    public string Yaml { get; }

    public IReadOnlyList<EventRecord> ParserEvents { get; }

    public IReadOnlyList<SourceComment> ScannerComments { get; }

    public ParsedNode? Root { get; }

    public IReadOnlyList<AssociationEntry> Entries { get; }

    public IReadOnlyList<ParsedNode> Collections { get; }

    public bool HasExplicitDocumentMarker { get; }

    public static SpikeDocument Load(string path)
    {
        var yaml = File.ReadAllText(path);
        var parserEvents = ReadParserEvents(yaml);
        var scannerComments = ReadScannerComments(yaml);
        var builder = new EventTreeBuilder(parserEvents);
        var root = builder.Build();
        var hasExplicitDocumentMarker = parserEvents.Any(
            static record => record.Event is DocumentStart { IsImplicit: false }
                or DocumentEnd { IsImplicit: false });

        return new SpikeDocument(
            path,
            yaml,
            parserEvents,
            scannerComments,
            root,
            builder.Entries,
            builder.Collections,
            hasExplicitDocumentMarker);
    }

    public EventComment? FindParserComment(long startIndex) =>
        ParserEvents
            .Select(static record => record.Event)
            .OfType<EventComment>()
            .SingleOrDefault(comment => comment.Start.Index == startIndex);

    public bool MarkPointsAtHash(Mark mark)
    {
        if (mark.Index < 0 || mark.Index >= Yaml.Length || Yaml[(int)mark.Index] != '#')
        {
            return false;
        }

        var lineStart = (int)mark.Index;
        while (lineStart > 0 && Yaml[lineStart - 1] is not '\r' and not '\n')
        {
            lineStart--;
        }

        return mark.Column == mark.Index - lineStart + 1;
    }

    public void DumpParserEvents(TextWriter writer)
    {
        foreach (var record in ParserEvents)
        {
            var parsingEvent = record.Event;
            writer.WriteLine(
                $"{record.Ordinal,3} {parsingEvent.GetType().Name,-14} " +
                $"{FormatMark(parsingEvent.Start),-17}..{FormatMark(parsingEvent.End),-17} " +
                Describe(parsingEvent));
        }
    }

    private static IReadOnlyList<EventRecord> ReadParserEvents(string yaml)
    {
        var events = new List<EventRecord>();
        var parser = new Parser(new Scanner(new StringReader(yaml), skipComments: false));
        var ordinal = 0;

        while (parser.MoveNext())
        {
            events.Add(new EventRecord(ordinal++, parser.Current!));
        }

        return events;
    }

    private static IReadOnlyList<SourceComment> ReadScannerComments(string yaml)
    {
        var comments = new List<SourceComment>();
        var scanner = new Scanner(new StringReader(yaml), skipComments: false);

        while (scanner.MoveNext())
        {
            if (scanner.Current is TokenComment comment)
            {
                comments.Add(
                    new SourceComment(
                        ExtractId(comment.Value),
                        comment.Value,
                        comment.IsInline,
                        comment.Start,
                        comment.End));
            }
        }

        return comments;
    }

    private static string ExtractId(string value)
    {
        var end = value.IndexOf(" ", StringComparison.Ordinal);
        return end < 0 ? value : value[..end];
    }

    private static string FormatMark(Mark mark) =>
        $"L{mark.Line}:C{mark.Column}:I{mark.Index}";

    private static string Describe(ParsingEvent parsingEvent) =>
        parsingEvent switch
        {
            EventComment comment =>
                $"inline={comment.IsInline.ToString().ToLowerInvariant()} value={Quote(comment.Value)}",
            Scalar scalar =>
                $"value={Quote(scalar.Value)} style={scalar.Style} anchor={scalar.Anchor} tag={scalar.Tag}",
            AnchorAlias alias => $"alias={alias.Value}",
            MappingStart mapping =>
                $"style={mapping.Style} anchor={mapping.Anchor} tag={mapping.Tag}",
            SequenceStart sequence =>
                $"style={sequence.Style} anchor={sequence.Anchor} tag={sequence.Tag}",
            DocumentStart document => $"implicit={document.IsImplicit.ToString().ToLowerInvariant()}",
            DocumentEnd document => $"implicit={document.IsImplicit.ToString().ToLowerInvariant()}",
            _ => string.Empty,
        };

    private static string Quote(string value) =>
        "\"" +
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) +
        "\"";
}

internal sealed record EventRecord(int Ordinal, ParsingEvent Event);

internal sealed record SourceComment(
    string Id,
    string Value,
    bool ScannerIsInline,
    Mark Start,
    Mark End);
