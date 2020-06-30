using Namespace2Xml.Syntax;

namespace Namespace2Xml.Scheme
{
    public sealed class SchemeLeaf : ISchemeEntry
    {
        public SchemeLeaf(EntryType type, string value, Payload originalEntry)
        {
            Type = type;
            Value = value;
            OriginalEntry = originalEntry;
        }

        public EntryType Type { get; }
        public string Value { get; }
        public Payload OriginalEntry { get; }
    }
}
