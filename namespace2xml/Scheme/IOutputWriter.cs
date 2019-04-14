using Namespace2Xml.Semantics;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml.Scheme
{
    public interface IOutputWriter
    {
        Task Write(ProfileTree tree, CancellationToken cancellationToken);
    }
}
