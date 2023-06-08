using System;
using System.Linq;

namespace Namespace2Xml.Syntax
{
    [Equals(DoNotAddEqualityOperators = true)]
    public sealed class ReferenceValueToken : IValueToken
    {
        private bool hasOutputRoot = false;

        public ReferenceValueToken(QualifiedName name)
        {
            Name = name;
        }

        public QualifiedName Name { get; }

        public QualifiedName NameWithoutOutputRoot => hasOutputRoot ? new QualifiedName(Name.Parts.Skip(1)) : Name;

        public override string ToString() => $"${{{Name}}}";

        public void AddOutputRoot(string rootName)
        {
            if (hasOutputRoot)
                throw new InvalidOperationException("The output root element has already been added");

            this.Name.AddRootPart(rootName);
            hasOutputRoot = true;
        }
    }
}
