using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Semantics
{
    public sealed class ProfileTreeLeaf : ProfileTree
    {
        public ProfileTreeLeaf(string name,
            IEnumerable<string> leadingComments,
            int lineNumber,
            string value,
            string originalValue)
            : base(name)
        {
            LeadingComments = leadingComments.ToList();
            LineNumber = lineNumber;
            Value = value;
            OriginalValue = originalValue;
        }

        public string Value { get; }

        public string OriginalValue { get; }

        public int LineNumber { get; }

        public IReadOnlyList<string> LeadingComments { get; }
    }
}
