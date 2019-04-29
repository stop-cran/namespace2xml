using Namespace2Xml.Syntax;

namespace Namespace2Xml.Semantics
{
    public sealed class ProfileTreeError : ProfileTree
    {
        public ProfileTreeError(NamePart name, string error, SourceMark sourceMark)
            : base(name)
        {
            Error = error;
            SourceMark = sourceMark;
        }

        public SourceMark SourceMark { get; }
        public string Error { get; }
    }
}
