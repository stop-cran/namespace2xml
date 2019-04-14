namespace Namespace2Xml.Syntax
{
    public sealed class ProfileError : NamedProfileEntry
    {
        public ProfileError(QualifiedName name, string error, int lineNumber)
            : base(name, lineNumber)
        {
            Error = error;
        }

        public string Error { get; }

        public override string ToString() => Error;
    }
}
