using Namespace2Xml.Semantics;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml.Formatters
{
    public interface IFormatter
    {
        Task Write(ProfileTree tree, CancellationToken cancellationToken);
    }
}
