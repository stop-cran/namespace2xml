using Namespace2Xml.Syntax;
using System.Linq;

namespace Namespace2Xml.Semantics
{
    public abstract class ProfileTree
    {
        public ProfileTree(NamePart name)
        {
            Name = name;
        }

        public NamePart Name { get; }

        public string NameString => Name.Tokens.OfType<TextNameToken>().Single().Text;
    }
}
