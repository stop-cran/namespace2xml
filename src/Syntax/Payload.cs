using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Syntax
{
    public sealed class Payload : NamedProfileEntry
    {
        public Payload(QualifiedName name, IEnumerable<IValueToken> value, SourceMark sourceMark, bool ignoreMissingReferences = false)
            : base(name, sourceMark)
        {
            Value = value.ToList();
            IgnoreMissingReferences = ignoreMissingReferences;
        }

        public List<IValueToken> Value { get; }

        public bool IgnoreMissingReferences { get; }

        public string ValueToString() => string.Join("", Value);

        public override string ToString() =>
            $"{Name}={ValueToString()}";
    }
}
