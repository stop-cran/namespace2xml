using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Semantics
{
    public class TreeBuilder
    {
        public IEnumerable<ProfileTree> Build(IEnumerable<IProfileEntry> enties) =>
            BuildHelper(
                PrependComments(
                    Preprocess(enties)));

        public IEnumerable<IProfileEntry> Preprocess(IEnumerable<IProfileEntry> enties) =>
                    ApplyReferences(
                        ApplyOverrides(
                            ApplySubstitutes(enties)));

        private IEnumerable<ProfileTree> BuildHelper(IEnumerable<(NamedProfileEntry payload, IReadOnlyList<Comment> leadingComments)> enties) =>
            (from entry in enties
             where entry.payload.Name.Parts.Count == 1
             select entry.payload is Payload payload
                ? (ProfileTree)new ProfileTreeLeaf(
                 payload.GetLeftNamespace(),
                 entry.leadingComments.Select(c => c.Text),
                 payload.LineNumber,
                 string.Join("", payload.Value.Cast<TextValueToken>()),
                 "")
                : new ProfileTreeError(
                    entry.payload.GetLeftNamespace(),
                    ((ProfileError)entry.payload).Error,
                    entry.payload.LineNumber))
                .Concat(enties
                    .Where(entry => entry.payload.Name.Parts.Count > 1)
                    .GroupBy(entry => entry.payload.GetLeftNamespace())
                    .Select(group => new ProfileTreeNode(group.Key,
                        BuildHelper(group.Select(entry =>
                            (entry.payload.SkipLeftNamespace(), entry.leadingComments))))));

        private IEnumerable<IProfileEntry> ApplyOverrides(IEnumerable<IProfileEntry> entries) =>
                ApplyOverridesReversed(entries.Reverse()).Reverse();

        private IEnumerable<IProfileEntry> ApplyOverridesReversed(IEnumerable<IProfileEntry> entries)
        {
            var visitedNames = new Dictionary<QualifiedName, int>();

            foreach (var entry in entries)
                switch (entry)
                {
                    case Payload payload:
                        if (visitedNames.ContainsKey(payload.Name))
                        {
                            // RK TODO: Logging.
                        }
                        else
                        {
                            visitedNames.Add(payload.Name, payload.LineNumber);
                            yield return entry;
                        }
                        break;

                    case Comment comment:
                        yield return comment;
                        break;

                    default:
                        throw new NotSupportedException($"An entry {entry} has unsupported type {entry.GetType()}.");
                }
        }

        private IEnumerable<IProfileEntry> ApplySubstitutes(IEnumerable<IProfileEntry> entries)
        {
            var entriesList = entries.ToList();

            while (ApplySubstitutesStep(entriesList)) ;

            return entriesList.Where(entry => !(entry is Payload p) ||
                p.GetNameSubstitutesCount() == 0 &&
                p.GetValueSubstitutesCount() == 0 &&
                p.GetValueRefSubstitutesCount() == 0);
        }

        private bool ApplySubstitutesStep(List<IProfileEntry> entries)
        {
            bool hasSubstitutes = false;

            hasSubstitutes |= ApplySubstitutesStepNonStrict(entries);
            hasSubstitutes |= ApplyStrictSubstitutesStep(entries);

            return hasSubstitutes;
        }

        private bool ApplySubstitutesStepNonStrict(List<IProfileEntry> entries)
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
                .ToList())
            {
                int nameCnt = tuple.nameSubstituteCount;
                int valCnt = tuple.valueSubstituteCount + tuple.refSubstituteCount;

                if (nameCnt == valCnt)
                {
                    if (tuple.valueSubstituteCount == 0)
                    {
                        // RK TODO: substitute one-to-one, match references
                    }

                    // RK TODO: substitute one-to-one, match name
                }
                else if (nameCnt > valCnt &&
                    nameCnt % valCnt == 0)
                {
                    // RK TODO: substitute many-to-one
                }
                else if (nameCnt > 0 &&
                    nameCnt < valCnt &&
                    valCnt % nameCnt == 0)
                {
                    // RK TODO: substitute one-to-many
                }
                else if (nameCnt == 0 && tuple.refSubstituteCount == 0)
                {
                    entries[entries.IndexOf(tuple.pattern)] = // RK TODO:  move to the end of ApplySubstitutes
                        new Payload(
                            tuple.pattern.Name,
                            tuple.pattern.Value.Select(v => v is SubstituteValueToken ? new TextValueToken("*") : v),
                            tuple.pattern.LineNumber);
                }
                else
                    entries[entries.IndexOf(tuple.pattern)] = new ProfileError(tuple.pattern.Name,
                        $"Not supported substitute: {tuple.pattern}.", tuple.pattern.LineNumber);
            }

            return hasSubstitutes;
        }

        private bool ApplyStrictSubstitutesStep(List<IProfileEntry> entries)
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
                                       let match = payload.GetMatchLeft(pattern)
                                       where match != null
                                       select pattern.SubstituteMatch(match)).Reverse())
                    if (!entries.OfType<Payload>().Any(e => e.Name.Equals(match.Name)))
                    {
                        entries.Insert(entries.IndexOf(pattern) + 1, match);
                        hasSubstitutes = true;
                    }

            return hasSubstitutes;
        }

        private IEnumerable<IProfileEntry> ApplyReferences(IEnumerable<IProfileEntry> entries)
        {
            var valuesByName = entries
                .OfType<Payload>()
                .ToDictionary(
                    entry => entry.Name,
                    entry => entry.Value);

            return entries.Select(
                PayloadExtensions.PassthroughComments(payload =>
                {
                    try
                    {
                        return new Payload(
                            payload.Name,
                            ApplyReferences(payload.Value, valuesByName, new[] { payload.Name }),
                            payload.LineNumber);
                    }
                    catch (ArgumentException ex)
                    {
                        return new ProfileError(payload.Name, ex.Message, payload.LineNumber);
                    }
                }));
        }

        private IEnumerable<TextValueToken> ApplyReferences(
            IEnumerable<IValueToken> value,
            Dictionary<QualifiedName, List<IValueToken>> context,
            IEnumerable<QualifiedName> usedNames) =>
            value.Select(token =>
            {
                switch (token)
                {
                    case TextValueToken text:
                        return text;

                    case ReferenceValueToken reference:
                        var names = usedNames.ToList();

                        if (names.Contains(reference.Name))
                            throw new ArgumentException($"Cyclic reference: {string.Join(" > ", usedNames)} > {reference}.");

                        if (!context.TryGetValue(reference.Name, out var referredValue))
                            throw new ArgumentException($"Reference {reference.Name} was not found at {names.Last()}.");

                        names.Add(reference.Name);

                        return new TextValueToken(
                            string.Join("",
                                ApplyReferences(referredValue, context, names)
                                    .Select(t => t.Text)));

                    default:
                        throw new NotSupportedException();
                }
            });

        private IEnumerable<(NamedProfileEntry payload, IReadOnlyList<Comment> leadingComments)> PrependComments(IEnumerable<IProfileEntry> entries)
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

        private static Payload SkipLeftNamespace(Payload payload) =>
            new Payload(new QualifiedName(payload.Name.Parts.Skip(1)), payload.Value, payload.LineNumber);
    }
}
