using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namespace2Xml.Syntax;

namespace Namespace2Xml
{
    public interface IProfileReader
    {
        Task<IReadOnlyList<IProfileEntry>> ReadFiles(IEnumerable<string> files, CancellationToken cancellationToken);
        IReadOnlyList<IProfileEntry> ReadVariables(IEnumerable<string> variables);
    }
}