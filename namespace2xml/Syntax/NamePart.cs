using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Syntax
{
    [Equals]
    public sealed class NamePart
    {
        public NamePart(IEnumerable<INameToken> tokens)
        {
            Tokens = tokens.ToList();
        }

        public List<INameToken> Tokens { get; }

        public override string ToString() =>
            string.Join("", Tokens);
    }
}
