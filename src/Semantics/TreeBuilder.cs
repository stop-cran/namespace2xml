using Microsoft.Extensions.Logging;
using MoreLinq.Extensions;
using Namespace2Xml.Scheme;
using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Semantics
{
    public class TreeBuilder : ITreeBuilder
    {
        private readonly ILogger<TreeBuilder> logger;

        public TreeBuilder(ILogger<TreeBuilder> logger)
        {
            this.logger = logger;
        }

        public IEnumerable<ProfileTree> Build(
            IEnumerable<IProfileEntry> entries,
            IQualifiedNameMatchDictionary<SubstituteType> substituteTypes)
        {
            entries = TreatSubstitutesAsText(entries, substituteTypes).ToList();
            entries = ApplySubstitutes(entries).ToList();
            entries = FlattenUnmatchedRightSubstitutes(entries).ToList();
            entries = ConcatTextTokens(entries).ToList();
            entries = RemoveUnmatchedReferenceSubstitutes(entries).ToList();
            entries = ApplyOverrides(entries).ToList();
            entries = ApplyReferences(entries).ToList();
            entries = ConcatTextTokensStrict(entries).ToList();

            return ToTree(
                PrependComments(entries).ToList(),
                new QualifiedName(Array.Empty<NamePart>()));
        }

        private static IEnumerable<IProfileEntry> ConcatTextTokensStrict(IEnumerable<IProfileEntry> entries) =>
            entries.Select(entry => entry is Payload payload ? payload.ConcatTextTokensStrict() : entry);

        private static IEnumerable<IProfileEntry> ConcatTextTokens(IEnumerable<IProfileEntry> entries) =>
            entries.Select(entry => entry is Payload payload ? payload.ConcatTextTokens() : entry);

        private static IEnumerable<IProfileEntry> ConcatTextTokensScheme(IEnumerable<IProfileEntry> entries) =>
            entries.Select(entry => entry is Payload payload && ShouldApplySchemeSubstitute(payload) ? payload.ConcatTextTokens() : entry);

        private static IEnumerable<IProfileEntry> TreatSubstitutesAsText(
            IEnumerable<IProfileEntry> entries,
            IQualifiedNameMatchDictionary<SubstituteType> substituteTypes)
        {
            foreach (var entry in entries)
                if (entry is Payload payload)
                    if (substituteTypes.TryMatch(payload.Name, out var substitute))
                        yield return new Payload(
                            (substitute & SubstituteType.Key) == SubstituteType.None
                                ? new QualifiedName(payload.Name.Parts.Select(part =>
                                    new NamePart(part.Tokens.Select(token =>
                                        token is SubstituteNameToken ? new TextNameToken("*") : token))))
                                : payload.Name,
                            (substitute & SubstituteType.Value) == SubstituteType.None
                                ? payload.Value.Select(value => (IValueToken)new TextValueToken(value is ReferenceValueToken referenceValueToken
                                    ? new ReferenceValueToken(referenceValueToken.NameWithoutOutputRoot).ToString()
                                    : value.ToString()))
                                : payload.Value, payload.SourceMark, payload.IgnoreMissingReferences);
                    else
                        yield return entry;
                else
                    yield return entry;
        }

        public IEnumerable<SchemeNode> BuildScheme(IEnumerable<IProfileEntry> entries, IEnumerable<QualifiedName> profileNames)
        {
            entries = ApplySchemeSubstitutes(entries, profileNames.ToList().AsReadOnly());
            entries = ConcatTextTokensScheme(entries).ToList();
            entries = RemoveUnmatchedReferenceSubstitutes(entries).ToList();
            entries = ApplyOverrides(entries).ToList();
            entries = ApplyReferences(entries).ToList();
            entries = ConcatTextTokensScheme(entries).ToList();

            return ToTree(
                PrependComments(entries),
                new QualifiedName(Array.Empty<NamePart>()))
            .Select(ToScheme)
            .Cast<SchemeNode>();
        }

        private static bool ShouldApplySchemeSubstitute(Payload p) =>
            Enum.TryParse<EntryType>(p.Name.Parts.Last().ToString(), out var type) &&
                type switch
                {
                    EntryType.root or EntryType.filename or EntryType.output or EntryType.delimiter or EntryType.namespacedelimiter or EntryType.xmloptions
                        or EntryType.type or EntryType.substitute or EntryType.key => true,
                    _ => false,
                };

        private static IEnumerable<IProfileEntry> ApplySchemeSubstitutes(IEnumerable<IProfileEntry> entries, IReadOnlyList<QualifiedName> profileNames) =>
            entries.SelectMany(entry => entry is Payload p && ShouldApplySchemeSubstitute(p) ? ApplySchemeSubstitutes(p, profileNames) : new[] { entry })
                .Where(entry => entry is not Payload p || !ShouldApplySchemeSubstitute(p) ||
                    p.GetNameSubstitutesCount() + p.GetValueSubstitutesCount() + p.GetValueRefSubstitutesCount() == 0);

        private static IEnumerable<IProfileEntry> ApplySchemeSubstitutes(Payload p, IReadOnlyList<QualifiedName> profileNames)
        {
            var nameSubCount = p.GetNameSubstitutesCount();
            var valueSubCount = p.GetValueSubstitutesCount() + p.GetValueRefSubstitutesCount();

            if (nameSubCount > 0)
                if (valueSubCount == 0)
                    return from name in profileNames
                           let match = name.GetLeftMatch(p.Name)
                           where match != null
                           select new Payload(
                               p.Name.ApplyFullMatch(match),
                               p.Value,
                               p.SourceMark,
                               true);
                else if (nameSubCount == valueSubCount)
                    return from name in profileNames
                           let match = name.GetLeftMatch(p.Name)
                           where match != null
                           select new Payload(
                               p.Name.ApplyFullMatch(match),
                               p.Value.ApplyFullMatch(match),
                               p.SourceMark,
                               true);
                else if (nameSubCount > valueSubCount && nameSubCount % valueSubCount == 0)
                    return from name in profileNames
                           let match = name.GetLeftMatch(p.Name)
                           where match?
                                    .Batch(nameSubCount / valueSubCount)
                                    .SequenceDistinct()
                                    .Count() == 1
                           select new Payload(
                               p.Name.ApplyFullMatch(match),
                               p.Value.ApplyFullMatch(match.Take(valueSubCount).ToList()),
                               p.SourceMark,
                               true);
                else if (valueSubCount > nameSubCount && valueSubCount % nameSubCount == 0)
                    return from name in profileNames
                           let match = name.GetLeftMatch(p.Name)
                           where match != null
                           select new Payload(
                               p.Name.ApplyFullMatch(match),
                               p.Value.ApplyFullMatch(
                                   Enumerable.Repeat(match, valueSubCount / nameSubCount)
                                    .SelectMany(x => x)
                                    .ToList()),
                               p.SourceMark,
                               true);
                else
                    return new[] { new ProfileError(p.Name, "Unsupported substitutes.", p.SourceMark) };
            else
                return new[] { p };
        }

        private ISchemeEntry ToScheme(ProfileTree tree)
        {
            return tree switch
            {
                ProfileTreeLeaf leaf => Enum.TryParse<EntryType>(leaf.NameString, out var type)
                                        ? (ISchemeEntry)new SchemeLeaf(
                                            type == EntryType.namespacedelimiter
                                                ? EntryType.delimiter
                                                : type,
                                            type == EntryType.substitute && string.Equals(leaf.Value, "keyOnly", StringComparison.InvariantCultureIgnoreCase) // For "keyOnly" backward compatibility
                                                ? "Key"
                                                : leaf.Value,
                                            leaf.OriginalEntry)
                                        : new SchemeError($"Unsupported entry type: {leaf.NameString}.", leaf.SourceMark),
                ProfileTreeNode node => new SchemeNode(node.Name, node.Children.Select(ToScheme)),
                ProfileTreeError error => new SchemeError(error.Error, error.SourceMark),
                _ => throw new NotSupportedException(),
            };
        }

        private IEnumerable<ProfileTree> ToTree(IEnumerable<(NamedProfileEntry payload, IReadOnlyList<Comment> leadingComments)> enties,
            QualifiedName prefix) =>
            (from entry in enties
             where entry.payload.Name.Parts.Count == 1
             select entry.payload is Payload payload
                ? (ProfileTree)new ProfileTreeLeaf(
                 payload,
                 entry.leadingComments,
                 prefix)
                : entry.payload is ProfileError error
                    ? new ProfileTreeError(
                        entry.payload.Name.Parts.First(),
                        error.Error,
                        error.SourceMark)
                    : throw new NotSupportedException(
                        $"Entry {entry.payload} at {entry.payload.SourceMark.FileName} line {entry.payload.SourceMark.LineNumber} has an unexpected type {entry.payload.GetType()}."))
                .Concat(enties
                    .Where(entry => entry.payload.Name.Parts.Count > 1)
                    .GroupBy(entry => entry.payload.Name.Parts.First())
                    .Select(group => new ProfileTreeNode(group.Key,
                        ToTree(group.Select(entry =>
                            (entry.payload.SkipLeftNamespace(), entry.leadingComments)),
                            new QualifiedName(prefix.Parts.Concat(new[] { group.Key }))))));

        private IEnumerable<IProfileEntry> ApplyOverrides(IEnumerable<IProfileEntry> entries) =>
                ApplyOverridesReversed(entries.Reverse()).Reverse();

        private IEnumerable<IProfileEntry> ApplyOverridesReversed(IEnumerable<IProfileEntry> entries)
        {
            var visitedNames = new Dictionary<QualifiedName, SourceMark>();

            foreach (var entry in entries)
                switch (entry)
                {
                    case Payload payload:
                        if (visitedNames.TryGetValue(payload.Name, out var tuple))
                        {
                            logger.LogDebug("Entry has been overridden, name: {name}, file: {fileName}, line: {lineNumber}",
                                payload.Name,
                                tuple.FileName,
                                tuple.LineNumber);
                        }
                        else
                        {
                            visitedNames.Add(payload.Name, payload.SourceMark);
                            yield return entry;
                        }
                        break;

                    case ProfileError _:
                    case Comment _:
                    case PayloadIgnore _:
                        yield return entry;
                        break;

                    default:
                        throw new NotSupportedException($"An entry {entry} has unsupported type {entry.GetType()}.");
                }
        }

        private static IEnumerable<IProfileEntry> FlattenUnmatchedRightSubstitutes(IEnumerable<IProfileEntry> entries) =>
            from entry in entries
            let payload = entry as Payload
            select
                payload?.GetValueSubstitutesCount() > 0 &&
                payload.GetValueRefSubstitutesCount() == 0 &&
                payload.GetNameSubstitutesCount() == 0
            ? new Payload(
                payload.Name,
                payload.Value.Select(v => v is SubstituteValueToken ? new TextValueToken("*") : v),
                payload.SourceMark,
                payload.IgnoreMissingReferences)
            : entry;

        private IEnumerable<IProfileEntry> RemoveUnmatchedReferenceSubstitutes(IEnumerable<IProfileEntry> entries)
        {
            var names = entries.OfType<Payload>().Select(entry => entry.Name).ToList();
            var keys = new HashSet<QualifiedName>(names);

            return entries.Where(entry =>
            {
                if (entry is not Payload payload || !payload.IgnoreMissingReferences)
                    return true;

                if (payload.Value.OfType<ReferenceValueToken>()
                    .All(reference => keys.Contains(reference.Name)))
                    return true;

                logger.LogDebug("Substitute skipped, name: {name}, value: {value}, file: {file}, line: {line}",
                    payload.Name,
                    payload.ValueToString(),
                    payload.SourceMark.FileName,
                    payload.SourceMark.LineNumber);

                return false;
            });
        }

        private IEnumerable<IProfileEntry> ApplySubstitutes(IEnumerable<IProfileEntry> entries)
        {
            var entriesList = new ProfileEntryList(entries);

            while (ApplySubstitutesStep(entriesList)) { }

            return entriesList.Where(entry => entry is not Payload p ||
                                              p.GetNameSubstitutesCount() == 0 &&
                                              p.GetValueRefSubstitutesCount() == 0);
        }

        private bool ApplySubstitutesStep(ProfileEntryList entries)
        {
            bool hasSubstitutes = false;

            hasSubstitutes |= ApplyNonStrictSubstitutesStep(entries);
            hasSubstitutes |= ApplyStrictSubstitutesStep(entries);

            return hasSubstitutes;
        }

        private bool ApplyNonStrictSubstitutesStep(ProfileEntryList entries)
        {
            bool hasSubstitutes = false;

            var entriesToProcess = entries.ToList();

            var patternsInfo = (from pattern in entriesToProcess.OfType<Payload>()
                    let nameSubstituteCount = pattern.GetNameSubstitutesCount()
                    let valueSubstituteCount = pattern.GetValueSubstitutesCount()
                    let refSubstituteCount = pattern.GetValueRefSubstitutesCount()
                    where nameSubstituteCount > 0 && valueSubstituteCount + refSubstituteCount > 0
                    select new
                    {
                        pattern,
                        nameSubstituteCount,
                        valueSubstituteCount,
                        refSubstituteCount
                    })
                .Reverse();

            foreach (var patternInfo in patternsInfo)
            {
                var matchesByName = entriesToProcess.GetLeftMatches(patternInfo.pattern).ToList();

                if (!matchesByName.Any()) continue;

                if (patternInfo.refSubstituteCount > 0)
                {
                    // Process substitutes in name, value and references

                    var matchesByReferences = patternInfo.pattern.Value
                        .GetFullMatchesByReferences(entriesToProcess).ToList();

                    var correspondingMatchesToProcess =
                        (from matchByName in matchesByName
                            from matchByReferences in matchesByReferences
                            let shouldProcess = matchByReferences
                                .Select(matchByReference => matchByName.Match
                                    .Zip(
                                        matchByReference.Match,
                                        (nameSubstitute, refSubstitute) =>
                                            string.Equals(nameSubstitute, refSubstitute))
                                    .All(x => x))
                                .All(x => x)
                            where shouldProcess
                            select (matchByName, matchByReferences))
                        .ToList();

                    var substituteResults = correspondingMatchesToProcess
                        .Select(matches =>
                        {
                            var qualifiedName = patternInfo.pattern.Name.ApplyFullMatch(matches.matchByName.Match);

                            var value =
                                patternInfo.valueSubstituteCount == 0
                                    ? patternInfo.pattern.Value
                                        .ApplyFullReferenceMatch(matches.matchByReferences
                                            .Select(x => x.Payload.Name)
                                            .ToList())
                                    : patternInfo.pattern.Value
                                        .ApplyFullReferenceMatch(matches.matchByReferences
                                            .Select(x => x.Payload.Name)
                                            .ToList()).ApplyFullMatch(matches.matchByName.Match);

                            return new
                            {
                                MatchedPayload = new Payload(
                                    qualifiedName,
                                    value,
                                    patternInfo.pattern.SourceMark),
                                MatchInfo = matches.matchByReferences.GetMatchSummary()
                            };
                        })
                        .Reverse();

                    foreach (var result in substituteResults)
                    {
                        if (entries.InsertAfterIfNotExists(patternInfo.pattern, result.MatchedPayload))
                        {
                            logger.LogDebug(
                                "Substitute by references, reference name: {name}, file: {file}, line: {line}, matches: {matches}",
                                patternInfo.pattern.ValueToString(),
                                patternInfo.pattern.SourceMark.FileName,
                                patternInfo.pattern.SourceMark.LineNumber,
                                result.MatchInfo);
                            hasSubstitutes = true;
                        }
                    }
                }
                else if (patternInfo.valueSubstituteCount > 0)
                {
                    // Process substitutions in name and value

                    var substituteResults = matchesByName
                        .Select(p => new
                        {
                            MatchedPayload = new Payload(
                                patternInfo.pattern.Name.ApplyFullMatch(p.Match),
                                patternInfo.pattern.Value.ApplyFullMatch(p.Match),
                                patternInfo.pattern.SourceMark,
                                true),
                            MatchInfo = p.Payload.GetSummary()
                        })
                        .Reverse()
                        .ToList();

                    foreach (var result in substituteResults)
                    {
                        if (entries.InsertAfterIfNotExists(patternInfo.pattern, result.MatchedPayload))
                        {
                            logger.LogDebug(
                                "Substitute by name: {name}, file: {file}, line: {line}, matches: {matches}",
                                patternInfo.pattern.Name,
                                patternInfo.pattern.SourceMark.FileName,
                                patternInfo.pattern.SourceMark.LineNumber,
                                result.MatchInfo);
                            hasSubstitutes = true;
                        }
                    }
                }
            }

            return hasSubstitutes;
        }

        private bool ApplyStrictSubstitutesStep(ProfileEntryList entries)
        {
            bool hasSubstitutes = false;

            var entriesToProcess = entries.ToList();

            var patterns = entriesToProcess
                .OfType<Payload>()
                .Where(payload =>
                    payload.GetNameSubstitutesCount() > 0 &&
                    payload.GetValueSubstitutesCount() == 0 &&
                    payload.GetValueRefSubstitutesCount() == 0);

            foreach (var pattern in patterns)
            {
                var matchesByName = entriesToProcess.GetLeftMatches(pattern);

                var substituteResults = matchesByName
                    .Select(match => new
                    {
                        MatchedPayload = new Payload(
                            pattern.Name.ApplyFullMatch(match.Match),
                            pattern.Value,
                            pattern.SourceMark),
                        MatchInfo = match.Payload.GetSummary()
                    })
                    .Reverse();

                foreach (var result in substituteResults)
                {
                    if (entries.InsertAfterIfNotExists(pattern, result.MatchedPayload))
                    {
                        logger.LogDebug(
                            "Substitute one-to-one by name: {name}, file: {file}, line: {line}, matches: {matches}",
                            pattern.Name,
                            pattern.SourceMark.FileName,
                            pattern.SourceMark.LineNumber,
                            result.MatchInfo);
                        hasSubstitutes = true;
                    }
                }
            }

            return hasSubstitutes;
        }

        public IEnumerable<IProfileEntry> ApplyNameSubstitutesLoop(IEnumerable<IProfileEntry> entries)
        {
            var entriesList = new ProfileEntryList(entries);

            while (ApplyNameSubstitutes(entriesList)) { }

            return entriesList.Where(entry =>
                entry is not Payload p
                || p.GetNameSubstitutesCount() == 0);
        }

        private bool ApplyNameSubstitutes(ProfileEntryList entries)
        {
            bool hasSubstitutes = false;

            var entriesToProcess = entries.ToList();

            var patterns = entriesToProcess
                .OfType<Payload>()
                .Where(payload => payload.GetNameSubstitutesCount() > 0);

            foreach (var pattern in patterns)
            {
                var matchesByName = entriesToProcess.GetLeftMatches(pattern);

                var substituteResults = matchesByName
                    .Select(match => new
                    {
                        MatchedPayload = new Payload(
                            pattern.Name.ApplyFullMatch(match.Match),
                            pattern.Value,
                            pattern.SourceMark),
                        MatchInfo = match.Payload.GetSummary()
                    })
                    .Reverse();

                foreach (var result in substituteResults)
                {
                    if (entries.InsertAfterIfNotExists(pattern, result.MatchedPayload))
                    {
                        hasSubstitutes = true;
                    }
                }
            }

            return hasSubstitutes;
        }

        private IEnumerable<IProfileEntry> ApplyReferences(IEnumerable<IProfileEntry> entries)
        {
            var valuesByName = entries
                .OfType<Payload>()
                .ToDictionary(entry => entry.Name);

            return entries.SelectMany(entry =>
            {
                switch (entry)
                {
                    case Payload payload:
                        try
                        {
                            return new[]
                            {
                                new Payload(
                                    payload.Name,
                                    ApplyReferences(payload.Value, valuesByName, new[] { (payload.Name, payload.SourceMark) }),
                                    payload.SourceMark,
                                    payload.IgnoreMissingReferences)
                            };
                        }
                        catch (ArgumentException ex)
                        {
                            return new[]
                            {
                                new ProfileError(
                                    payload.Name,
                                    ex.Message,
                                    payload.SourceMark)
                            };
                        }

                    case Comment _:
                    case ProfileError _:
                    case PayloadIgnore _:
                        return new[] { entry };

                    default:
                        throw new NotSupportedException($"An entry {entry} has unsupported type {entry.GetType()}.");
                }
            }).ToList();
        }

        private IEnumerable<TextValueToken> ApplyReferences(
            IEnumerable<IValueToken> value,
            Dictionary<QualifiedName, Payload> context,
            IEnumerable<(QualifiedName name, SourceMark mark)> usedNames) =>
            value.SelectMany(token =>
            {
                switch (token)
                {
                    case TextValueToken text:
                        return new[] { text };

                    case ReferenceValueToken reference:
                        var names = usedNames.ToList();
                        var cyclic = names.FirstOrDefault(name => name.name == reference.Name);

                        if (cyclic.name != null)
                            throw new ArgumentException($"Cyclic reference: {string.Join(" > ", names.Select(tuple => $"{tuple.name} [file: {tuple.mark.FileName}, line: {tuple.mark.LineNumber}]"))}.");

                        if (!context.TryGetValue(reference.Name, out var payload))
                            throw new ArgumentException($"Reference {reference.Name} was not found at {names.Last().name} [file: {names.Last().mark.FileName}, line: {names.Last().mark.LineNumber}].");

                        names.Add((reference.Name, payload.SourceMark));

                        return new[]
                        {
                            new TextValueToken(
                                string.Join("",
                                    ApplyReferences(payload.Value, context, names)
                                        .Select(t => t.Text)))
                        };

                    default:
                        throw new NotSupportedException();
                }
            });

        private static IEnumerable<(NamedProfileEntry payload, IReadOnlyList<Comment> leadingComments)> PrependComments(IEnumerable<IProfileEntry> entries)
        {
            var leadingComments = new List<Comment>();

            foreach (var entry in entries)
                switch (entry)
                {
                    case Payload payload:
                        yield return (payload, leadingComments.AsReadOnly());
                        leadingComments = new List<Comment>();
                        break;

                    case ProfileError error:
                        yield return (error, leadingComments.AsReadOnly());
                        leadingComments = new List<Comment>();
                        break;

                    case Comment comment:
                        leadingComments.Add(comment);
                        break;

                    case PayloadIgnore _:
                        break;

                    default:
                        throw new NotSupportedException();
                }
        }
    }
}
