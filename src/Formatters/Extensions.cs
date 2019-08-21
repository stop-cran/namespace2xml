using Namespace2Xml.Scheme;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Formatters
{
    public static class Extensions
    {
        public static IEnumerable<(QualifiedName prefix, ISchemeEntry entry)> GetAllChildrenAndSelf(this SchemeNode node) =>
            node.GetAllChildren().Concat(new[] { (new QualifiedName(new NamePart[0]), (ISchemeEntry)node) });

        public static IEnumerable<(QualifiedName prefix, ISchemeEntry entry)> GetAllChildren(this SchemeNode node) =>
            node.Children
                .OfType<SchemeLeaf>()
                .Select(leaf => (new QualifiedName(new[] { node.Name }), (ISchemeEntry)leaf))
            .Concat(node.Children
                .OfType<SchemeError>()
                .Select(error => (new QualifiedName(new[] { node.Name }), (ISchemeEntry)error)))
            .Concat(node.Children
                .OfType<SchemeNode>()
                .SelectMany(childNode =>
                new[] { (new QualifiedName(new[] { node.Name }), (ISchemeEntry)childNode) }
                    .Concat(childNode.GetAllChildren()
                        .Select(tuple =>
                            (new QualifiedName(new[] { node.Name }.Concat(tuple.prefix.Parts)), tuple.entry)))));

        [return: NullGuard.AllowNull]
        public static string SingleOrDefaultValue(
            this SchemeNode node,
            EntryType type) =>
            node.Children
                .OfType<SchemeLeaf>()
                .SingleOrDefault(leaf => leaf.Type == type)?.Value;

        public static IReadOnlyDictionary<QualifiedName, SubstituteType> GetSubstituteTypes(
            this SchemeNode node) =>
            (from tuple in node.GetAllChildren()
             let leaf = tuple.entry as SchemeLeaf
             where leaf?.Type == EntryType.substitute
             select new
             {
                 tuple.prefix,
                 type = Enum.TryParse<SubstituteType>(leaf.Value, true, out var substituteType)
                     ? substituteType
                     : throw new ArgumentException($"Unsupported substitute type {leaf.Value}.")
             })
            .ToDictionary(tuple => tuple.prefix, tuple => tuple.type);

        public static IReadOnlyList<QualifiedName> GetNamesOfType(
            this SchemeNode node,
            Scheme.ValueType type) =>
            (from tuple in node.GetAllChildren()
             let leaf = tuple.entry as SchemeLeaf
             where leaf?.Type == EntryType.type &&
                leaf.Value
                    .Split(",")
                    .Select(s => Enum.Parse<Scheme.ValueType>(s))
                    .Contains(type)
             select tuple.prefix)
            .ToList()
            .AsReadOnly();

        public static QualifiedName ToQualifiedName(this IEnumerable<string> sequence) =>
            new QualifiedName(sequence.Select(part => new NamePart(new[] { new TextNameToken(part) })));

        public static ProfileTree Ignore(this ProfileTree tree, HashSet<QualifiedName> ignore) =>
            tree.Ignore(ignore, new string[0]).Single();

        private static IEnumerable<ProfileTree> Ignore(this ProfileTree tree, HashSet<QualifiedName> ignore, IEnumerable<string> prefix)
        {
            var newPrefix = prefix.Concat(new[] { tree.NameString });

            if (tree is ProfileTreeNode node)
                yield return new ProfileTreeNode(
                    node.Name,
                    node.Children
                        .SelectMany(child => child.Ignore(ignore, newPrefix)));
            else if (!ignore.Contains(newPrefix.ToQualifiedName()))
                yield return tree;
        }
    }
}
