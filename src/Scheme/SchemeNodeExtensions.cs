using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using System.Collections.Generic;
using System.Linq;
using Namespace2Xml.Formatters;

namespace Namespace2Xml.Scheme
{
    public static class SchemeNodeExtensions
    {
        public static IEnumerable<IProfileEntry> WithImplicitArrays(this SchemeNode schemeNode, ProfileTree profileTree)
        {
            var arrays = schemeNode.GetNamesOfType(Scheme.ValueType.array);
            var multiline = schemeNode.GetNamesOfType(Scheme.ValueType.multiline);

            var originalPayload = schemeNode.GetOriginalPayload().ToList();
            var implicitArrays = (from pair in profileTree.GetAllChildren()
                                  where !arrays.IsMatch(pair.prefix)
                                        && !multiline.IsMatch(pair.prefix)
                                        && IsArray(pair.tree)
                                  select new Payload(
                                      new QualifiedName(pair.prefix.Parts.Append(new NamePart(new[] { new TextNameToken("type") }))),
                                      new[] { new TextValueToken("array") }, pair.tree.GetFirstSourceMark())).ToList();

            return originalPayload.Concat(implicitArrays);
        }

        private static bool IsArray(ProfileTree tree)
        {
            if (!(tree is ProfileTreeNode node))
                return false;

            var cnt = node.Children.Count(
                child =>
                    child.Name.Tokens.Count == 1
                    && child.Name.Tokens[0] is TextNameToken token
                    && token.Text.All(char.IsDigit));

            return cnt > 0
                   && cnt == node.Children
                       .Count(child => !(child.Name.Tokens.Count == 1 && child.Name.Tokens[0] is SubstituteNameToken));
        }
    }
}
