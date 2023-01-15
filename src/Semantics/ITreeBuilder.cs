using Namespace2Xml.Scheme;
using Namespace2Xml.Syntax;
using System.Collections.Generic;

namespace Namespace2Xml.Semantics
{
    public interface ITreeBuilder
    {
        IEnumerable<ProfileTree> Build(
            IEnumerable<IProfileEntry> entries,
            IQualifiedNameMatchDictionary<SubstituteType> substituteTypes);

        IEnumerable<SchemeNode> BuildScheme(
            IEnumerable<IProfileEntry> enties,
            IEnumerable<QualifiedName> profileNames);
    }
}
