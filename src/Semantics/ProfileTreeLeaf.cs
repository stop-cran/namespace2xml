using Namespace2Xml.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Semantics
{
    public sealed class ProfileTreeLeaf : ProfileTree
    {
        public ProfileTreeLeaf(NamePart name,
            IEnumerable<Comment> leadingComments,
            SourceMark sourceMark,
            string value)
            : base(name)
        {
            LeadingComments = leadingComments.ToList();
            SourceMark = sourceMark;
            Value = value;
        }

        public string Value { get; }

        public SourceMark SourceMark { get; }

        public IReadOnlyList<Comment> LeadingComments { get; }
    }
}
