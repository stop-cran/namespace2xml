using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Namespace2Xml.Formatters
{
    public class IgnoreFormatter : IFormatter
    {
        private readonly IFormatter inner;
        private readonly HashSet<QualifiedName> ignore;

        public IgnoreFormatter(IFormatter inner, IReadOnlyList<QualifiedName> ignore)
        {
            this.inner = inner;
            this.ignore = new HashSet<QualifiedName>(ignore);
        }

        public async Task Write(ProfileTree tree, CancellationToken cancellationToken) =>
            await inner.Write(tree.Ignore(ignore), cancellationToken);
    }
}
