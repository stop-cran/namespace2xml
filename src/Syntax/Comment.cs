namespace Namespace2Xml.Syntax
{
    public sealed class Comment : IProfileEntry
    {
        public Comment(string text)
        {
            Text = text;
        }

        public string Text { get; }

        public override string ToString() => "# " + Text;
    }
}
