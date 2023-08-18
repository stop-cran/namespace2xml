using System;

namespace Namespace2Xml.Syntax
{
    public abstract class NamedProfileEntry : IProfileEntry
    {
        private bool hasOutputRoot = false;

        public NamedProfileEntry(QualifiedName name, SourceMark sourceMark)
        {
            Name = name;
            SourceMark = sourceMark;
        }

        public QualifiedName Name { get; }

        public SourceMark SourceMark { get; set; }

        public void AddOutputRoot(string rootName)
        {
            if (hasOutputRoot)
                throw new InvalidOperationException("The output root element has already been added");

            this.Name.AddRootPart(rootName);
            hasOutputRoot = true;
        }
    }
}
