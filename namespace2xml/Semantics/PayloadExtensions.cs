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
                    return new Payload(new QualifiedName(payload.Name.Parts.Skip(1)), payload.Value, payload.LineNumber);

                case ProfileError error:
                    return new ProfileError(new QualifiedName(error.Name.Parts.Skip(1)), error.Error, error.LineNumber);

                default:
                    throw new NotSupportedException();
            }
        }

        public static string GetLeftNamespace(this NamedProfileEntry payload) =>
            payload.Name.Parts.First().Tokens.Cast<TextNameToken>().Single().Text;

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

        [return: NullGuard.AllowNull]
        public static IReadOnlyList<string> GetMatchFull(this Payload input, Payload pattern) =>
            GetMatchFull(input.Name.Parts, pattern.Name.Parts);

        [return: NullGuard.AllowNull]
        public static IReadOnlyList<string> GetMatchFull(this IReadOnlyList<NamePart> input, IReadOnlyList<NamePart> pattern)
        {
            if (input.Count != pattern.Count)
                return null;

            var matches = new List<string>();

            foreach (var m in input.Zip(pattern, GetMatch))
                if (m == null)
                    return null;
                else
                    matches.AddRange(m);

            return matches.AsReadOnly();
        }

        [return: NullGuard.AllowNull]
        public static IReadOnlyList<string> GetMatchLeft(this Payload input, Payload pattern)
        {
            var leftParts = pattern.Name.Parts
                .AsEnumerable()
                .Reverse()
                .SkipWhile(part => part.Tokens.All(t => t is TextNameToken))
                .Reverse()
                .ToList();

            if (input.Name.Parts.Count < leftParts.Count)
                return null;

            var matches = new List<string>();

            foreach (var m in input.Name.Parts
                .Take(leftParts.Count)
                .Zip(pattern.Name.Parts, GetMatch))
                if (m == null)
                    return null;
                else
                    matches.AddRange(m);

            return matches.AsReadOnly();
        }

        [return: NullGuard.AllowNull]
        private static IEnumerable<string> GetMatch(NamePart input, NamePart pattern)
        {
            if (input.Tokens.Count == 1 &&
                input.Tokens[0] is TextNameToken textToken)
            {
                var match = Regex.Match(
                    textToken.Text,
                    "^" + string.Join("", pattern.Tokens.Select(p =>
                    {
                        switch (p)
                        {
                            case TextNameToken t:
                                return Regex.Escape(t.Text);

                            case SubstituteNameToken s:
                                return "(.+)";

                            default:
                                throw new NotSupportedException();
                        }
                    })) + "$");

                if (match.Success)
                    return match.Groups
                        .Skip(1)
                        .Select(group => group.Value);
            }

            return null;
        }

        public static Payload SubstituteMatch(this Payload pattern, IReadOnlyList<string> match) =>
            new Payload(
                new QualifiedName(
                    pattern.Name.Parts
                    .SubstituteMatch(match)
                    .ConcatTextNameTokens()),
                pattern.Value,
                pattern.LineNumber);

        public static IEnumerable<NamePart> ConcatTextNameTokens(this IEnumerable<NamePart> nameParts) =>
            nameParts.Select(part =>
                part.Tokens.All(t => t is TextNameToken)
                ? new NamePart(new[]
                {
                    new TextNameToken(
                        string.Join("",
                            part.Tokens
                                .Cast<TextNameToken>()
                            .Select(t => t.Text)))
                })
                : part);

        public static IEnumerable<NamePart> SubstituteMatch(this IEnumerable<NamePart> nameParts, IReadOnlyList<string> match)
        {
            int matchIndex = 0;

            foreach (var part in nameParts)
            {
                var tokens = new List<TextNameToken>();

                foreach (var token in part.Tokens)
                    switch (token)
                    {
                        case TextNameToken t:
                            tokens.Add(t);
                            break;

                        case SubstituteNameToken s:
                            tokens.Add(new TextNameToken(match[matchIndex]));
                            matchIndex++;
                            break;

                        default:
                            throw new NotSupportedException();
                    }

                yield return new NamePart(tokens);
            }
        }

        public static Func<IProfileEntry, IProfileEntry> PassthroughComments(Func<Payload, IProfileEntry> func) =>
            entry =>
            {
                switch (entry)
                {
                    case Payload payload:
                        return func(payload);

                    case Comment comment:
                        return comment;

                    default:
                        throw new NotSupportedException($"An entry {entry} has unsupported type {entry.GetType()}.");
                }
            };
    }
}
