using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Semantics
{
    public sealed class ProfileTreeNode : ProfileTree
    {
        public ProfileTreeNode(string name, IEnumerable<ProfileTree> children)
            : base(name)
        {
            Children = children
                .OrderBy(child => child.GetFirstLineNumber())
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<ProfileTree> Children { get; }
    }
}
