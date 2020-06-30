using System;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Syntax
{
    [Equals]
    public sealed class QualifiedName
    {
        public QualifiedName(IEnumerable<NamePart> parts)
        {
            Parts = parts.ToList();
        }

        public List<NamePart> Parts { get; }

        public override string ToString() =>
            string.Join('.', Parts);

        public static readonly QualifiedName Empty = new QualifiedName(Array.Empty<NamePart>());
    }
}
