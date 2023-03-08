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

        public static IQualifiedNameMatchDictionary<SubstituteType> GetSubstituteTypes(
            this IEnumerable<SchemeNode> nodes) =>
            new QualifiedNameMatchDictionary<SubstituteType>(
                from node in nodes
                from tuple in node.GetAllChildren()
                let leaf = tuple.entry as SchemeLeaf
                where leaf?.Type == EntryType.substitute
                select new KeyValuePair<QualifiedName, SubstituteType>(tuple.prefix,
                    Enum.TryParse<SubstituteType>(leaf.Value, true, out var substituteType)
                        ? substituteType
                        : throw new ArgumentException($"Unsupported substitute type {leaf.Value}.")));

        public static IQualifiedNameMatchList GetNamesOfType(
            this SchemeNode node,
            Scheme.ValueType type) =>
            new QualifiedNameMatchList(from tuple in node.GetAllChildren()
                                       let leaf = tuple.entry as SchemeLeaf
                                       where leaf?.Type == EntryType.type &&
                                          leaf.Value
                                              .Split(",")
                                              .Select(s => Enum.Parse<Scheme.ValueType>(s))
                                              .Contains(type)
                                       select tuple.prefix);

        public static QualifiedName ToQualifiedName(this IEnumerable<string> sequence) =>
            new(sequence.Select(part => new NamePart(new[] { new TextNameToken(part) })));

        public static ProfileTree Ignore(this ProfileTree tree, IQualifiedNameMatchList ignore) =>
            tree.Ignore(ignore, Array.Empty<string>()).Single();

        private static IEnumerable<ProfileTree> Ignore(this ProfileTree tree, IQualifiedNameMatchList ignore, IEnumerable<string> prefix)
        {
            var newPrefix = prefix.Concat(new[] { tree.NameString });

            if (tree is ProfileTreeNode node)
            {
                var children = node.Children
                    .SelectMany(child => child.Ignore(ignore, newPrefix)).ToList();
                if (children.Any())
                {
                    yield return new ProfileTreeNode(
                        node.Name,
                        children);
                }
            }
            else if (!ignore.IsMatch(newPrefix.ToQualifiedName()))
                yield return tree;
        }
    }
}
