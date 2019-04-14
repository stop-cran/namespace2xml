using Namespace2Xml.Semantics;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml.Scheme
{
    public class NullWriter : IOutputWriter
    {
        public Task Write(ProfileTree tree, CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;
    }
}
