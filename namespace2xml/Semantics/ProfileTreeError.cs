namespace Namespace2Xml.Semantics
{
    public sealed class ProfileTreeError : ProfileTree
    {
        public ProfileTreeError(string name, string error, int lineNumber)
            : base(name)
        {
            Error = error;
            LineNumber = lineNumber;
        }

        public int LineNumber { get; }
        public string Error { get; }
    }
}
