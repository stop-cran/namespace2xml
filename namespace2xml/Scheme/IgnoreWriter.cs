using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml.Scheme
{
    public class IgnoreWriter : IOutputWriter
    {
        private readonly IOutputWriter inner;
        private readonly IReadOnlyList<QualifiedName> ignore;

        public IgnoreWriter(IOutputWriter inner, IReadOnlyList<QualifiedName> ignore)
        {
            this.inner = inner;
            this.ignore = ignore;
        }

        public async Task Write(ProfileTree tree, CancellationToken cancellationToken) =>
            await inner.Write(tree.Ignore(ignore), cancellationToken);
    }
}
