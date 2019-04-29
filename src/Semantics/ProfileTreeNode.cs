using Namespace2Xml.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Semantics
{
    public sealed class ProfileTreeNode : ProfileTree
    {
        public ProfileTreeNode(NamePart name, IEnumerable<ProfileTree> children)
            : base(name)
        {
            Children = children
                .OrderBy(child => child.GetFirstSourceMark())
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<ProfileTree> Children { get; }
    }
}
