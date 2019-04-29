namespace Namespace2Xml.Scheme
{
    public sealed class SchemeLeaf : ISchemeEntry
    {
        public SchemeLeaf(EntryType type, string value)
        {
            Type = type;
            Value = value;
        }

        public EntryType Type { get; }
        public string Value { get; }
    }
}
