using Namespace2Xml.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Semantics
{
    public sealed class ProfileTreeLeaf : ProfileTree
    {
        public ProfileTreeLeaf(Payload payload,
            IEnumerable<Comment> leadingComments,
            QualifiedName prefix)
            : base(payload.Name.Parts.First())
        {
            OriginalEntry = new Payload(new QualifiedName(prefix.Parts.Concat(payload.Name.Parts)), payload.Value, payload.SourceMark, payload.IgnoreMissingReferences);
            LeadingComments = leadingComments.ToList();
            SourceMark = payload.SourceMark;
            Value = string.Join("", payload.Value.Cast<TextValueToken>());
        }

        public string Value { get; }

        public SourceMark SourceMark { get; }

        public IReadOnlyList<Comment> LeadingComments { get; }

        public Payload OriginalEntry { get; }
    }
}
