namespace Namespace2Xml.Syntax
{
    public abstract class NamedProfileEntry : IProfileEntry
    {
        public NamedProfileEntry(QualifiedName name, SourceMark sourceMark)
        {
            Name = name;
            SourceMark = sourceMark;
        }

        public QualifiedName Name { get; }

        public SourceMark SourceMark { get; set; }
    }
}
