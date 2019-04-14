using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Scheme
{
    public static class Extensions
    {
        [return: NullGuard.AllowNull]
        public static string SingleOrDefaultValue(
            this IEnumerable<(Payload Payload, EntryType Type)> entries,
            QualifiedName name,
            EntryType type) =>
            entries.SingleOrDefault(entry => entry.Payload.Name.Equals(name) && entry.Type == type).Payload?.ValueToString();

        public static IReadOnlyList<QualifiedName> GetNamesOfType(
            this IEnumerable<(Payload Payload, EntryType Type)> entries,
            ValueType type) =>
            (from entry in entries
             where entry.Type == EntryType.type &&
                entry.Payload
                    .ValueToString()
                    .Split(",")
                    .Select(s => Enum.Parse<ValueType>(s))
                    .Contains(type)
             select entry.Payload.Name)
            .ToList()
            .AsReadOnly();

        public static IReadOnlyDictionary<QualifiedName, string> GetKeys(
            this IEnumerable<(Payload Payload, EntryType Type)> entries) =>
            entries
                .Where(entry => entry.Type == EntryType.key)
                .ToDictionary(
                    entry => entry.Payload.Name,
                    entry => entry.Payload.ValueToString());

        public static IReadOnlyList<QualifiedName> GetHiddenKeys(
            this IEnumerable<(Payload Payload, EntryType Type)> entries) =>
             (from entry in entries
              where entry.Type == EntryType.hasHiddenKey && bool.Parse(entry.Payload.ValueToString())
              select entry.Payload.Name)
            .ToList()
            .AsReadOnly();

        public static bool IsMatch(this IEnumerable<QualifiedName> patterns, string[] sequence) =>
            patterns.GetMatch(sequence) != null;

        [return: NullGuard.AllowNull]
        public static QualifiedName GetMatch(this IEnumerable<QualifiedName> patterns, string[] sequence)
        {
            var input = sequence.Select(part => new NamePart(new[] { new TextNameToken(part) })).ToList();

            return patterns.FirstOrDefault(pattern => input.GetMatchFull(pattern.Parts) != null);
        }

        [return: NullGuard.AllowNull]
        public static T GetMatch<T>(this IReadOnlyDictionary<QualifiedName, T> patterns, string[] sequence)
        {
            var key = patterns.Keys.GetMatch(sequence);

            return key == null ? default : patterns[key];
        }

        public static ProfileTree Ignore(this ProfileTree tree, IReadOnlyList<QualifiedName> ignore) =>
            tree.Ignore(ignore, new string[0]).Single();

        private static IEnumerable<ProfileTree> Ignore(this ProfileTree tree, IReadOnlyList<QualifiedName> ignore, string[] prefix)
        {
            var newPrefix = prefix.Concat(new[] { tree.Name }).ToArray();

            if (ignore.IsMatch(newPrefix))
                return Enumerable.Empty<ProfileTree>();

            if (tree is ProfileTreeNode node)
                return new[]
                {
                    new ProfileTreeNode(
                        node.Name,
                        node.Children
                            .SelectMany(child => child.Ignore(ignore, newPrefix)))
                };

            return new[] { tree };
        }
    }
}
