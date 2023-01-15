using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Scheme
{
    public static class SchemeNodeExtensions
    {
        public static IEnumerable<IProfileEntry> WithImplicitArrays(this SchemeNode node, ProfileTree tree)
        {
            var originalPayload = node.GetOriginalPayload().ToList();
            var implicitArrays = (from pair in tree.GetAllChildren()
                                  where IsArray(pair.tree)
                                  select new Payload(
                                      new QualifiedName(pair.prefix.Parts.Append(new NamePart(new[] { new TextNameToken("type") }))),
                                      new[] { new TextValueToken("array") }, pair.tree.GetFirstSourceMark())).ToList();

            return originalPayload.Concat(implicitArrays);
        }

        private static bool IsArray(ProfileTree tree)
        {
            if (!(tree is ProfileTreeNode node))
                return false;

            var cnt = node.Children.Count(child => child.Name.Tokens.Count == 1 && child.Name.Tokens[0] is TextNameToken token && token.Text.All(char.IsDigit));

            return cnt > 0 && cnt == node.Children
                .Count(child => !(child.Name.Tokens.Count == 1 && child.Name.Tokens[0] is SubstituteNameToken));
        }
    }
}
