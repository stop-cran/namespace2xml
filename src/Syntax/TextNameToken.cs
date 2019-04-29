namespace Namespace2Xml.Syntax
{
    [Equals]
    public sealed class TextNameToken : INameToken
    {
        public TextNameToken(string text)
        {
            Text = text;
        }

        public string Text { get; }

        public override string ToString() => Text;
    }
}
