namespace YamlCommentSpike;

internal static class Program
{
    public static int Main(string[] args)
    {
        var files = ResolveInputFiles(args);
        var failureCount = 0;
        var checkedCount = 0;

        foreach (var file in files)
        {
            var document = SpikeDocument.Load(file);
            var associations = AssociationEngine.Associate(document);
            var expectations = CorpusExpectations.ForFile(Path.GetFileName(file));

            Console.WriteLine($"=== {Path.GetFileName(file)} ===");
            Console.WriteLine("--- parser event stream ---");
            document.DumpParserEvents(Console.Out);
            Console.WriteLine("--- comment associations ---");

            foreach (var expectation in expectations)
            {
                checkedCount++;
                var passed = CheckExpectation(document, associations, expectation, out var actual);
                failureCount += passed ? 0 : 1;
                Console.WriteLine(
                    $"{expectation.Id,-4} {actual,-74} {(passed ? "PASS" : "FAIL")}");
            }

            var expectedCommentIds = expectations
                .Where(static expectation => expectation.IsComment)
                .Select(static expectation => expectation.Id)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var comment in document.ScannerComments)
            {
                if (!expectedCommentIds.Contains(comment.Id))
                {
                    failureCount++;
                    Console.WriteLine($"{comment.Id,-4} unexpected scanner comment".PadRight(80) + "FAIL");
                }

                if (!document.MarkPointsAtHash(comment.Start))
                {
                    failureCount++;
                    Console.WriteLine($"{comment.Id,-4} Start mark does not point at '#'."
                        .PadRight(80) + "FAIL");
                }
            }

            Console.WriteLine();
        }

        Console.WriteLine(
            $"Checked {checkedCount} expectations across {files.Count} documents: " +
            $"{(failureCount == 0 ? "PASS" : $"FAIL ({failureCount})")}.");

        return failureCount == 0 ? 0 : 1;
    }

    private static bool CheckExpectation(
        SpikeDocument document,
        IReadOnlyDictionary<string, CommentAssociation> associations,
        CommentExpectation expectation,
        out string actual)
    {
        var scannerComment = document.ScannerComments
            .SingleOrDefault(comment => string.Equals(comment.Id, expectation.Id, StringComparison.Ordinal));

        if (!expectation.IsComment)
        {
            actual = scannerComment is null
                ? "correctly remains scalar content"
                : "incorrectly surfaced as a comment";
            return scannerComment is null;
        }

        if (scannerComment is null)
        {
            actual = "missing from Scanner(skipComments:false)";
            return false;
        }

        var parserComment = document.FindParserComment(scannerComment.Start.Index);
        var parserReported = parserComment is not null;
        if (!associations.TryGetValue(expectation.Id, out var association))
        {
            actual = "reported by scanner but not associated";
            return false;
        }

        var parserText = parserReported
            ? $"parser=yes/inline={parserComment!.IsInline.ToString().ToLowerInvariant()}"
            : "parser=NO (scanner compensation)";
        var ownerText = association.OwnerPath is null ? string.Empty : $" {association.OwnerPath}";
        actual = $"{parserText}; {association.Kind}{ownerText}";

        return parserReported == expectation.ParserReported
            && association.Kind == expectation.Kind
            && string.Equals(association.OwnerPath, expectation.OwnerPath, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ResolveInputFiles(string[] args)
    {
        if (args.Length > 0)
        {
            return args.Select(Path.GetFullPath).ToArray();
        }

        var corpus = Path.Combine(AppContext.BaseDirectory, "Corpus");
        return Directory.GetFiles(corpus, "*.yaml")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
