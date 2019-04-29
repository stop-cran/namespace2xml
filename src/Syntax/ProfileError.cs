namespace Namespace2Xml.Syntax
{
    public sealed class ProfileError : NamedProfileEntry
    {
        public ProfileError(QualifiedName name, string error, SourceMark sourceMark)
            : base(name, sourceMark)
        {
            Error = error;
        }

        public string Error { get; }
    }
}
