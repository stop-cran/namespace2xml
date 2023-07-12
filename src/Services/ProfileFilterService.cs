using System.Collections.Generic;
using System.Linq;
using Namespace2Xml.Syntax;

namespace Namespace2Xml.Services;

public class ProfileFilterService : IProfileFilterService
{
    public IReadOnlyCollection<IProfileEntry> FilterByOutput(IReadOnlyList<IProfileEntry> inputs, IReadOnlyList<IProfileEntry> schemes)
    {
        var notFilteredEntries = new LinkedList<IProfileEntry>(inputs);
        var outputNames = schemes
            .Where(x =>
                x is Payload payload && payload.Name.Parts.Last().Tokens[0] is TextNameToken { Text: "output" })
            .Select(x => ((Payload)x).Name);

        var filteredEntries = FilterEntries(notFilteredEntries, outputNames.ToList(), true);

        filteredEntries.AddRange(
            FilterByReferences(
                notFilteredEntries,
                filteredEntries
                    .OfType<Payload>()
                    .SelectMany(x => x.Value.OfType<ReferenceValueToken>())
                    .Select(x => x.Name)
                    .ToList()));

        return filteredEntries;
    }

    private List<IProfileEntry> FilterByReferences(
        LinkedList<IProfileEntry> entries,
        List<QualifiedName> namesToMatch)
    {
        var filteredEntries = FilterEntries(entries, namesToMatch, false);

        if (filteredEntries.Count > 0 && entries.First != null)
        {
            filteredEntries.AddRange(
                FilterByReferences(
                    entries,
                    filteredEntries
                        .OfType<Payload>()
                        .SelectMany(x => x.Value.OfType<ReferenceValueToken>())
                        .Select(x => x.Name)
                        .ToList()));
        }

        return filteredEntries;
    }

    private List<IProfileEntry> FilterEntries(LinkedList<IProfileEntry> entries, List<QualifiedName> namesToMatch, bool skipLastNamePart)
    {
        var filteredEntries = new List<IProfileEntry>();

        foreach (var name in namesToMatch)
        {
            var currentNode = entries.First;
            while (currentNode != null)
            {
                var nextNode = currentNode.Next;
                var entry = currentNode.Value;

                if (entry is NamedProfileEntry namedProfileEntry
                    && IsMatch(
                        namedProfileEntry.Name.Parts,
                        name.Parts.SkipLast(skipLastNamePart ? 1 : 0)))
                {
                    var comments = new List<IProfileEntry>();
                    while (currentNode.Previous is { Value: Comment })
                    {
                        comments.Add(currentNode.Previous.Value);
                        entries.Remove(currentNode.Previous);
                    }

                    comments.Reverse();
                    filteredEntries.AddRange(comments);

                    filteredEntries.Add(entry);
                    entries.Remove(currentNode);
                }

                currentNode = nextNode;
            }
        }

        return filteredEntries;
    }

    private bool IsMatch(IEnumerable<NamePart> inputNameParts, IEnumerable<NamePart> namePartsToMatch)
    {
        return inputNameParts
            .Zip(
                namePartsToMatch,
                (profileNamePart, referenceNamePart) =>
                    (profileNamePart.HasSubstitutes && referenceNamePart.HasSubstitutes)
                    || profileNamePart.IsMatch(referenceNamePart.ToString())
                    || referenceNamePart.IsMatch(profileNamePart.ToString()))
            .All(y => y);
    }
}