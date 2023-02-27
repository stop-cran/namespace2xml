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
            IQualifiedNameMatchDictionary<SubstituteType> substituteTypes) =>
            from entry in entries
            select entry is Payload payload
                ? substituteTypes.TryMatch(payload.Name, out var substitute)
                ? new Payload(
                    (substitute & SubstituteType.Key) == SubstituteType.None
                    ? new QualifiedName(payload.Name.Parts.Select(part =>
                        new NamePart(part.Tokens.Select(token =>
                            token is SubstituteNameToken ? new TextNameToken("*") : token))))
                    : payload.Name,
                    (substitute & SubstituteType.Value) == SubstituteType.None
                    ? payload.Value.Select(value => (IValueToken)new TextValueToken(value.ToString()))
                    : payload.Value,
                    payload.SourceMark,
                    payload.IgnoreMissingReferences)
                : entry
                : entry;

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
                    EntryType.root or EntryType.filename or EntryType.output or EntryType.delimiter or EntryType.xmloptions
                        or EntryType.type or EntryType.substitute => true,
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
                                        ? (ISchemeEntry)new SchemeLeaf(type, leaf.Value, leaf.OriginalEntry)
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

            while (ApplySubstitutesStep(entriesList)) ;

            return entriesList.Where(entry => entry is not Payload p ||
                p.GetNameSubstitutesCount() == 0 &&
                p.GetValueRefSubstitutesCount() == 0);
        }

        private bool ApplySubstitutesStep(ProfileEntryList entries)
        {
            bool hasSubstitutes = false;

            hasSubstitutes |= ApplySubstitutesStepNonStrict(entries);
            hasSubstitutes |= ApplyStrictSubstitutesStep(entries);

            return hasSubstitutes;
        }

        private bool ApplySubstitutesStepNonStrict(ProfileEntryList entries)
        {
            bool hasSubstitutes = false;

            foreach (var tuple in (from pattern in entries.OfType<Payload>()
                                   let nameSubstituteCount = pattern.GetNameSubstitutesCount()
                                   let valueSubstituteCount = pattern.GetValueSubstitutesCount()
                                   let refSubstituteCount = pattern.GetValueRefSubstitutesCount()
                                   where valueSubstituteCount + refSubstituteCount > 0
                                   select new
                                   {
                                       pattern,
                                       nameSubstituteCount,
                                       valueSubstituteCount,
                                       refSubstituteCount
                                   })
                .Reverse()
                .ToList())
            {
                int nameCnt = tuple.nameSubstituteCount;
                int valCnt = tuple.valueSubstituteCount + tuple.refSubstituteCount;

                if (nameCnt == valCnt)
                {
                    if (tuple.valueSubstituteCount == 0)
                        foreach (var pair in tuple.pattern.Value
                            .GetFullMatchesByReferences(entries.ToList())
                            .Select(matches => new
                            {
                                MatchedPayload = new Payload(
                                    tuple.pattern.Name
                                        .ApplyFullMatch(matches
                                            .SelectMany(x => x.Match)
                                            .ToList()),
                                    tuple.pattern.Value
                                        .ApplyFullReferenceMatch(matches
                                            .Select(x => x.Payload.Name)
                                            .ToList()),
                                    tuple.pattern.SourceMark),
                                MatchInfo = matches.GetMatchSummary()
                            })
                            .Reverse())
                            if (entries.InsertAfterIfNotExists(tuple.pattern, pair.MatchedPayload))
                            {
                                logger.LogDebug("Substitute one-to-one by references, name: {name}, file: {file}, line: {line}, matches: {matches}",
                                    tuple.pattern.Name,
                                    tuple.pattern.SourceMark.FileName,
                                    tuple.pattern.SourceMark.LineNumber,
                                    pair.MatchInfo);
                                hasSubstitutes = true;
                            }

                    foreach (var pair in entries.ToList().GetLeftMatches(tuple.pattern)
                        .Select(p => new
                        {
                            MatchedPayload = new Payload(
                                tuple.pattern.Name.ApplyFullMatch(p.Match),
                                tuple.pattern.Value.ApplyFullMatch(p.Match),
                                tuple.pattern.SourceMark,
                                true),
                            MatchInfo = p.Payload.GetSummary()
                        })
                        .Reverse())
                        if (entries.InsertAfterIfNotExists(tuple.pattern, pair.MatchedPayload))
                        {
                            logger.LogDebug("Substitute one-to-one by name: {name}, file: {file}, line: {line}, matches: {matches}",
                                tuple.pattern.Name,
                                tuple.pattern.SourceMark.FileName,
                                tuple.pattern.SourceMark.LineNumber,
                                pair.MatchInfo);
                            hasSubstitutes = true;
                        }
                }
                else if (nameCnt > valCnt && nameCnt % valCnt == 0)
                {
                    if (tuple.valueSubstituteCount == 0)
                        foreach (var pair in tuple.pattern.Value
                            .GetFullMatchesByReferences(entries.ToList())
                            .Select(matches => new
                            {
                                MatchedPayload = new Payload(
                                    tuple.pattern.Name.ApplyFullMatch(
                                        Enumerable.Repeat(
                                            matches.SelectMany(x => x.Match),
                                            nameCnt / valCnt)
                                        .SelectMany(x => x)
                                        .ToList()),
                                    tuple.pattern.Value
                                        .ApplyFullReferenceMatch(matches
                                            .Select(x => x.Payload.Name)
                                            .ToList()),
                                    tuple.pattern.SourceMark),
                                MatchInfo = matches.GetMatchSummary()
                            })
                            .Reverse())
                            if (entries.InsertAfterIfNotExists(tuple.pattern, pair.MatchedPayload))
                            {
                                logger.LogDebug("Substitute many-to-one by references, name: {name}, file: {file}, line: {line}, matches: {matches}",
                                    tuple.pattern.Name,
                                    tuple.pattern.SourceMark.FileName,
                                    tuple.pattern.SourceMark.LineNumber,
                                    pair.MatchInfo);
                                hasSubstitutes = true;
                            }

                    foreach (var pair in (from p in entries.ToList().GetLeftMatches(tuple.pattern)
                                          where p.Match
                                             .Batch(valCnt)
                                             .SequenceDistinct()
                                             .Count() == 1
                                          select new
                                          {
                                              MatchedPayload = new Payload(
                                                  tuple.pattern.Name.ApplyFullMatch(p.Match),
                                                  tuple.pattern.Value.ApplyFullMatch(p.Match.Take(valCnt).ToList()),
                                                  tuple.pattern.SourceMark,
                                                  true),
                                              MatchInfo = p.Payload.GetSummary()
                                          }).Reverse())
                        if (entries.InsertAfterIfNotExists(tuple.pattern, pair.MatchedPayload))
                        {
                            logger.LogDebug("Substitute many-to-one by name, name: {name}, file: {file}, line: {line}, matches: {matches}",
                                tuple.pattern.Name,
                                tuple.pattern.SourceMark.FileName,
                                tuple.pattern.SourceMark.LineNumber,
                                pair.MatchInfo);
                            hasSubstitutes = true;
                        }
                }
                else if (nameCnt > 0 && nameCnt < valCnt && valCnt % nameCnt == 0)
                {
                    if (tuple.valueSubstituteCount == 0)
                        foreach (var pair in (from matches in tuple.pattern.Value
                                                .GetFullMatchesByReferences(entries.ToList())
                                              where matches
                                                  .Select(match => match.Match)
                                                  .SequenceDistinct()
                                                  .Count() == 1
                                              select new
                                              {
                                                  MatchedPayload = new Payload(
                                                      tuple.pattern.Name
                                                          .ApplyFullMatch(matches.First().Match),
                                                      tuple.pattern.Value
                                                          .ApplyFullReferenceMatch(matches
                                                              .Select(x => x.Payload.Name)
                                                              .ToList()),
                                                   tuple.pattern.SourceMark),
                                                  MatchInfo = matches.GetMatchSummary()
                                              }).Reverse())
                            if (entries.InsertAfterIfNotExists(tuple.pattern, pair.MatchedPayload))
                            {
                                logger.LogDebug("Substitute one-to-many by references, name: {name}, file: {file}, line: {line}, matches: {matches}",
                                    tuple.pattern.Name,
                                    tuple.pattern.SourceMark.FileName,
                                    tuple.pattern.SourceMark.LineNumber,
                                    pair.MatchInfo);
                                hasSubstitutes = true;
                            }

                    foreach (var pair in entries.ToList().GetLeftMatches(tuple.pattern)
                        .Select(p => new
                        {
                            MatchedPayload = new Payload(
                                tuple.pattern.Name.ApplyFullMatch(p.Match),
                                tuple.pattern.Value.ApplyFullMatch(
                                    Enumerable.Repeat(p.Match, valCnt / nameCnt)
                                        .SelectMany(matches => matches)
                                        .ToList()),
                                tuple.pattern.SourceMark,
                                true),
                            MatchInfo = p.Payload.GetSummary()
                        })
                        .Reverse())
                        if (entries.InsertAfterIfNotExists(tuple.pattern, pair.MatchedPayload))
                        {
                            logger.LogDebug("Substitute one-to-many by name, name: {name}, file: {file}, line: {line}, matches: {matches}",
                                tuple.pattern.Name,
                                tuple.pattern.SourceMark.FileName,
                                tuple.pattern.SourceMark.LineNumber,
                                pair.MatchInfo);
                            hasSubstitutes = true;
                        }
                }
                else if (nameCnt == 0 && tuple.refSubstituteCount == 0)
                {
                    // Flatten after applying other substitutes.
                }
                else
                    entries[entries.IndexOf(tuple.pattern)] =
                        new ProfileError(
                            tuple.pattern.Name,
                            $"Not supported substitute: {tuple.pattern} [{tuple.pattern.SourceMark.FileName}, line {tuple.pattern.SourceMark.LineNumber}].",
                            tuple.pattern.SourceMark);
            }

            return hasSubstitutes;
        }

        private static bool ApplyStrictSubstitutesStep(ProfileEntryList entries)
        {
            bool hasSubstitutes = false;

            foreach (var pattern in entries
                .OfType<Payload>()
                .Where(payload =>
                    payload.GetNameSubstitutesCount() > 0 &&
                    payload.GetValueSubstitutesCount() == 0 &&
                    payload.GetValueRefSubstitutesCount() == 0)
                .ToList())
                foreach (var match in (from payload in entries.OfType<Payload>()
                                       let match = payload.GetLeftMatch(pattern)
                                       where match != null
                                       select new Payload(
                                           pattern.Name.ApplyFullMatch(match),
                                           pattern.Value,
                                           pattern.SourceMark)).Reverse())
                    hasSubstitutes |= entries.InsertAfterIfNotExists(pattern, match);

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

                    default:
                        throw new NotSupportedException();
                }
        }
    }
}
