using Namespace2Xml.Scheme;
using Namespace2Xml.Syntax;
using System.Collections.Generic;

namespace Namespace2Xml.Formatters
{
    public interface IFormatterBuilder
    {
        IEnumerable<(QualifiedName prefix, IFormatter formatter)> Build(SchemeNode node);
    }
}
