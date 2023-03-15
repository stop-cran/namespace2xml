using Namespace2Xml.Scheme;
using Namespace2Xml.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Semantics
{
    public static class ProfileTreeExtensions
    {
        public static SourceMark GetFirstSourceMark(this ProfileTree tree)
        {
            switch (tree)
            {
                case ProfileTreeLeaf leaf:
                    return leaf.SourceMark;

                case ProfileTreeNode node:
                    return node.Children.Min(GetFirstSourceMark);

                case ProfileTreeError error:
                    return error.SourceMark;

                default:
                    throw new NotSupportedException();
            }
        }

        public static IEnumerable<ProfileTree> GetSubTrees(this ProfileTree tree, QualifiedName prefix) =>
            from tuple in tree.GetAllChildren()
            where tuple.prefix.Parts.GetFullMatch(prefix.Parts) != null
            select tuple.tree;

        public static IEnumerable<(QualifiedName prefix, ProfileTreeLeaf leaf)> GetLeafs(this ProfileTree tree) =>
            from tuple in tree.GetAllChildren()
            let leaf = tuple.tree as ProfileTreeLeaf
            where leaf != null
            select (tuple.prefix, leaf);

        public static IEnumerable<Payload> GetOriginalPayload(this SchemeNode node) =>
            node.GetAllChildren().OfType<SchemeLeaf>().Select(l => l.OriginalEntry);

        public static IEnumerable<ISchemeEntry> GetAllChildren(this ISchemeEntry tree) =>
            tree is SchemeNode node
                ? new [] { tree }
                    .Concat(node.Children.SelectMany(child =>
                        GetAllChildren(child)))
                : new [] { tree };

        public static IEnumerable<(QualifiedName prefix, ProfileTree tree)> GetAllChildren(this ProfileTree tree) =>
            tree is ProfileTreeNode node
                ? new (QualifiedName prefix, ProfileTree tree)[] { (new QualifiedName(new[] { tree.Name }), tree) }
                    .Concat(node.Children.SelectMany(child =>
                        GetAllChildren(child)
                            .Select(pair =>
                                (new QualifiedName(new[] { node.Name }.Concat(pair.prefix.Parts)), pair.tree))))
                : new (QualifiedName prefix, ProfileTree tree)[] { (new QualifiedName(new[] { tree.Name }), tree) };
    }
}
