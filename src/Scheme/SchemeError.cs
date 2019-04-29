namespace Namespace2Xml.Scheme
{
    public sealed class SchemeError : ISchemeEntry
    {
        public SchemeError(string error, SourceMark sourceMark)
        {
            Error = error;
            SourceMark = sourceMark;
        }

        public string Error { get; }
        public SourceMark SourceMark { get; }
    }
}
