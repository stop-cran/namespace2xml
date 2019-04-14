using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Syntax
{
    public sealed class Payload : NamedProfileEntry
    {
        public Payload(QualifiedName name, IEnumerable<IValueToken> value, int lineNumber)
            : base(name, lineNumber)
        {
            Value = value.ToList();
        }

        public List<IValueToken> Value { get; }

        public string ValueToString() => string.Join("", Value);

        public override string ToString() =>
            $"{Name}={ValueToString()}";
    }
}
