using System;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Semantics
{
    public static class ProfileTreeExtensions
    {
        public static int GetFirstLineNumber(this ProfileTree tree)
        {
            switch (tree)
            {
                case ProfileTreeLeaf leaf:
                    return leaf.LineNumber;

                case ProfileTreeNode node:
                    return node.Children.Min(GetFirstLineNumber);

                case ProfileTreeError error:
                    return error.LineNumber;

                default:
                    throw new NotSupportedException();
            }
        }

        [return: NullGuard.AllowNull]
        public static ProfileTree GetSubTree(this ProfileTree tree, IEnumerable<string> keys)
        {
            if (tree.Name != keys.First())
                return null;

            if (keys.Count() == 1)
                return tree;

            switch (tree)
            {
                case ProfileTreeNode node:
                    return node.Children.Select(child => child.GetSubTree(keys.Skip(1))).SingleOrDefault(child => child != null);

                case ProfileTreeLeaf leaf:
                    return null;

                default:
                    throw new NotSupportedException();
            }
        }

        public static IEnumerable<(string error, int lineNumber)> GetErrors(this ProfileTree tree)
        {
            switch (tree)
            {
                case ProfileTreeLeaf leaf:
                    return new (string error, int lineNumber)[0];

                case ProfileTreeNode node:
                    return node.Children.SelectMany(child => child.GetErrors());

                case ProfileTreeError error:
                    return new[] { (error.Error, error.LineNumber) };

                default:
                    throw new NotSupportedException();
            }
        }

        public static IEnumerable<(IReadOnlyList<string> prefix, ProfileTreeLeaf leaf)> GetLeafs(this ProfileTree tree) =>
            GetLeafsInternal(tree).Select(tuple =>
                ((IReadOnlyList<string>)tuple.prefix.Skip(1).ToList().AsReadOnly(), tuple.leaf));

        private static IEnumerable<(IEnumerable<string> prefix, ProfileTreeLeaf leaf)> GetLeafsInternal(ProfileTree tree)
        {
            switch (tree)
            {
                case ProfileTreeLeaf leaf:
                    return new[] { (Enumerable.Empty<string>(), leaf) };

                case ProfileTreeNode node:
                    return node.Children.SelectMany(child =>
                        GetLeafsInternal(child)
                            .Select(pair =>
                                (new[] { node.Name }.Concat(pair.prefix), pair.leaf)));

                default:
                    throw new NotSupportedException();
            }
        }
    }
}
