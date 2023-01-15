using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Namespace2Xml.Semantics
{
    public static class PayloadExtensions
    {
        public static NamedProfileEntry SkipLeftNamespace(this NamedProfileEntry entry)
        {
            switch (entry)
            {
                case Payload payload:
                    return new Payload(
                        new QualifiedName(payload.Name.Parts.Skip(1)),
                        payload.Value,
                        payload.SourceMark,
                        payload.IgnoreMissingReferences);

                case ProfileError error:
                    return new ProfileError(
                        new QualifiedName(
                            error.Name.Parts.Skip(1)),
                            error.Error,
                            error.SourceMark);

                default:
                    throw new NotSupportedException();
            }
        }

        public static Payload ConcatTextTokens(this Payload payload) =>
            new Payload(
                new QualifiedName(payload.Name.Parts.Select(ConcatTextTokensStrict)),
                ConcatTextTokens(payload.Value),
                payload.SourceMark,
                payload.IgnoreMissingReferences);

        private static IEnumerable<IValueToken> ConcatTextTokens(List<IValueToken> tokens)
        {
            var texts = new List<string>();

            foreach (var token in tokens)
                if (token is TextValueToken text)
                    texts.Add(text.Text);
                else
                {
                    if (texts.Count > 0)
                    {
                        yield return new TextValueToken(string.Join("", texts));

                        texts = new List<string>();
                    }

                    if (token is ReferenceValueToken reference && reference.Name.Parts.Any(p => p.Tokens.Count > 1))
                        yield return new ReferenceValueToken(new QualifiedName(reference.Name.Parts.Select(ConcatTextTokensStrict)));
                    else
                        yield return token;
                }

            if (texts.Count > 0)
                yield return new TextValueToken(string.Join("", texts));
        }

        public static Payload ConcatTextTokensStrict(this Payload payload) =>
            new Payload(
                new QualifiedName(payload.Name.Parts.Select(ConcatTextTokensStrict)),
                new[] { new TextValueToken(string.Join("", payload.Value.Cast<TextValueToken>())) },
                payload.SourceMark,
                payload.IgnoreMissingReferences);

        public static NamePart ConcatTextTokensStrict(this NamePart part) =>
            new NamePart(new[] { new TextNameToken(string.Join("", part.Tokens.Cast<TextNameToken>())) });

        public static int GetNameSubstitutesCount(this Payload payload) =>
            payload.Name.GetNameSubstitutesCount();

        public static int GetNameSubstitutesCount(this QualifiedName name) =>
            name.Parts.Sum(part => part.Tokens.OfType<SubstituteNameToken>().Count());

        public static int GetValueSubstitutesCount(this Payload payload) =>
            payload.Value.OfType<SubstituteValueToken>().Count();

        public static int GetValueRefSubstitutesCount(this Payload payload) =>
            payload.Value
                .OfType<ReferenceValueToken>()
                .Sum(token => token.Name.GetNameSubstitutesCount());

        public static IEnumerable<(IReadOnlyList<string> Match, Payload Payload)> GetLeftMatches(
            this IEnumerable<IProfileEntry> entries, Payload pattern) =>
            from payload in entries.OfType<Payload>()
            let match = payload.GetLeftMatch(pattern)
            where match != null
            select (match, payload);

        public static IEnumerable<IEnumerable<(IReadOnlyList<string> Match, Payload Payload)>> GetFullMatchesByReferences(
            this IEnumerable<IValueToken> values, IEnumerable<IProfileEntry> entries) =>
            values
                .OfType<ReferenceValueToken>()
                .Select(token => entries
                    .OfType<Payload>()
                    .Select<Payload, (IReadOnlyList<string> Match, Payload Payload)>(payload =>
                        (payload.Name.Parts.GetFullMatch(token.Name.Parts), payload))
                    .Where(match => match.Match != null)
                    .ToList())
                .ToList()
                .Cartesian();

        public static LazyString GetMatchSummary(this IEnumerable<(IReadOnlyList<string> Match, Payload Payload)> matches) =>
            LazyString.Join(", ",
                matches.Select((m, index) =>
                    $"[{index + 1}] {m.Payload.GetSummary()}"));

        public static FormattableString GetSummary(this Payload payload) =>
            $"name: {payload.Name}, file: {payload.SourceMark.FileName}, line: {payload.SourceMark.LineNumber}";

        public static bool IsTextOnly(this NamePart namePart) =>
            namePart.Tokens.All(token => token is TextNameToken);

        public static bool IsTextOnly(this QualifiedName qualifiedName) =>
            qualifiedName.Parts.All(part => part.IsTextOnly());

        public static QualifiedName ApplyFullMatch(this QualifiedName name, IReadOnlyList<string> match)
        {
            using (var enumerator = match.GetEnumerator())
            {
                var result = name.ApplyMatch(enumerator, string.Join(", ", match));

                if (enumerator.MoveNext())
                    throw new ArgumentException($"Match count too big at {name}: {string.Join(", ", match)}.");

                return result;
            }
        }

        private static QualifiedName ApplyMatch(this QualifiedName name, IEnumerator<string> matchEnumerator, string comment)
        {
            var result = new QualifiedName(name.Parts.Select(part =>
                  new NamePart(part.Tokens.Select(token =>
                  {
                      if (token is SubstituteNameToken)
                      {
                          if (!matchEnumerator.MoveNext())
                              throw new ArgumentException($"Match count too small at {name}: {comment}.");
                          return new TextNameToken(matchEnumerator.Current);
                      }

                      return token;
                  }))));

            return result;
        }

        public static IReadOnlyList<IValueToken> ApplyFullMatch(this IReadOnlyList<IValueToken> value, IReadOnlyList<string> match)
        {
            using (var enumerator = match.GetEnumerator())
            {
                var result = value.Select(token =>
                {
                    switch (token)
                    {
                        case TextValueToken _:
                            return token;
                        case SubstituteValueToken _:
                            if (!enumerator.MoveNext())
                                throw new ArgumentException($"Match count too small at {string.Join("", value)}: {string.Join(", ", match)}.");
                            return new TextValueToken(enumerator.Current);
                        case ReferenceValueToken reference:
                            return new ReferenceValueToken(reference.Name.ApplyMatch(enumerator, string.Join(", ", match)));
                        default:
                            throw new NotSupportedException();
                    }
                }).ToList();

                if (enumerator.MoveNext())
                    throw new ArgumentException($"Match count too big at {string.Join("", value)}: {string.Join(", ", match)}.");

                return result;
            }
        }

        public static IReadOnlyList<IValueToken> ApplyFullReferenceMatch(this IReadOnlyList<IValueToken> value, IReadOnlyList<QualifiedName> referenceMatch)
        {
            using var enumerator = referenceMatch.GetEnumerator();
            var result = value.Select(token =>
            {
                if (token is ReferenceValueToken reference)
                {
                    if (!enumerator.MoveNext())
                        throw new ArgumentException($"Match count too small at {string.Join("", value)}: {string.Join(", ", referenceMatch)}.");
                    if (enumerator.Current.Parts.GetFullMatch(reference.Name.Parts) == null)
                        throw new ArgumentException($"{enumerator.Current} does not match {reference.Name}.");
                    return new ReferenceValueToken(enumerator.Current);
                }

                return token;
            }).ToList();

            if (enumerator.MoveNext())
                throw new ArgumentException($"Match count too big at {string.Join("", value)}: {string.Join(", ", referenceMatch)}.");

            return result;
        }

        [return: NullGuard.AllowNull]
        public static IReadOnlyList<string> GetFullMatch(this IReadOnlyList<NamePart> input, IReadOnlyList<NamePart> pattern)
        {
            if (input.Count != pattern.Count)
                return null;

            var matches = new List<string>();

            foreach (var m in input.Zip(pattern, (i,p) => p.GetMatch(i)))
                if (m == null)
                    return null;
                else
                    matches.AddRange(m);

            return matches.AsReadOnly();
        }

        [return: NullGuard.AllowNull]
        public static IReadOnlyList<string> GetLeftMatch(this Payload input, Payload pattern) =>
            input.Name.GetLeftMatch(pattern.Name);

        [return: NullGuard.AllowNull]
        public static IReadOnlyList<string> GetLeftMatch(this QualifiedName input, QualifiedName pattern)
        {
            int leftPartsCount = pattern.Parts.Count;

            while (leftPartsCount >= 0 && pattern.Parts[leftPartsCount - 1].Tokens.All(t => t is TextNameToken))
                leftPartsCount--;

            return input.Parts.Count >= leftPartsCount
                ? input.Parts
                .Take(leftPartsCount)
                .ToList()
                .GetFullMatch(pattern.Parts.Take(leftPartsCount).ToList())
                : null;
        }
    }
}
