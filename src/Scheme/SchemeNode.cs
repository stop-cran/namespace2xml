using Namespace2Xml.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Scheme
{
    public sealed class SchemeNode : ISchemeEntry
    {
        public SchemeNode(NamePart name, IEnumerable<ISchemeEntry> children)
        {
            Name = name;
            Children = children.ToList().AsReadOnly();
        }

        public NamePart Name { get; }
        public IReadOnlyList<ISchemeEntry> Children { get; }
    }
}
