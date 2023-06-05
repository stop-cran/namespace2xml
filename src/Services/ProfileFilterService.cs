using System.Collections.Generic;
using System.Linq;
using Namespace2Xml.Syntax;

namespace Namespace2Xml.Services;

public class ProfileFilterService : IProfileFilterService
{
    public IReadOnlyCollection<IProfileEntry> FilterByOutput(IReadOnlyList<IProfileEntry> inputs, IReadOnlyList<IProfileEntry> schemes)
    {
        var filteredEntries = new List<IProfileEntry>();
        var notFilteredEntries = inputs.ToList();
        var outputNames = schemes
            .Where(x =>
                x is Payload payload && payload.Name.Parts.Last().Tokens[0] is TextNameToken { Text: "output" })
            .Select(x => ((Payload)x).Name);

        foreach (var outputName in outputNames)
        {
            foreach (var entry in notFilteredEntries.ToList())
            {
                switch (entry)
                {
                    case Comment:
                        filteredEntries.Add(entry);
                        notFilteredEntries.Remove(entry);
                        break;
                    case NamedProfileEntry namedProfileEntry:
                        if (IsMatch(
                                namedProfileEntry.Name.Parts /*.Skip(1)*/,
                                outputName.Parts /*.Skip(1)*/.SkipLast(1)))
                        {
                            filteredEntries.Add(entry);
                            notFilteredEntries.Remove(entry);
                        }
                        break;
                }
            }
        }

        filteredEntries.AddRange(
            FilterByReferences(
                notFilteredEntries.OfType<NamedProfileEntry>(),
                filteredEntries
                    .OfType<Payload>()
                    .SelectMany(x => x.Value.OfType<ReferenceValueToken>())
                    .Select(x => x.Name)
                    .ToList()));

        return filteredEntries;
    }

    private List<IProfileEntry> FilterByReferences(
        IEnumerable<NamedProfileEntry> entries,
        List<QualifiedName> namesToMatch)
    {
        var filteredEntries = new List<IProfileEntry>();
        var notFilteredEntries = entries.ToList();

        foreach (var name in namesToMatch)
        {
            foreach (var entry in notFilteredEntries.ToList())
            {
                if (IsMatch(entry.Name.Parts, name.Parts))
                {
                    filteredEntries.Add(entry);
                    notFilteredEntries.Remove(entry);
                }
            }
        }

        if (filteredEntries.Any() && notFilteredEntries.Any())
        {
            filteredEntries.AddRange(
                FilterByReferences(
                    notFilteredEntries,
                    filteredEntries
                        .OfType<Payload>()
                        .SelectMany(x => x.Value.OfType<ReferenceValueToken>())
                        .Select(x => x.Name)
                        .ToList()));
        }

        return filteredEntries;
    }

    private bool IsMatch(IEnumerable<NamePart> inputNameParts, IEnumerable<NamePart> namePartsToMatch)
    {
        return inputNameParts
            .Zip(
                namePartsToMatch,
                (profileNamePart, referenceNamePart) =>
                    profileNamePart.HasSubstitutes
                    || referenceNamePart.IsMatch(profileNamePart.ToString()))
            .All(y => y);
    }
}