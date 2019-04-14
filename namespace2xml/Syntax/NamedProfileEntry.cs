namespace Namespace2Xml.Syntax
{
    public abstract class NamedProfileEntry : IProfileEntry
    {
        public NamedProfileEntry(QualifiedName name, int lineNumber)
        {
            Name = name;
            LineNumber = lineNumber;
        }

        public QualifiedName Name { get; }

        public int LineNumber { get; set; }
    }
}
