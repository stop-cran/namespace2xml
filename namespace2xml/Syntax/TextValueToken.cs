namespace Namespace2Xml.Syntax
{
    public sealed class TextValueToken : IValueToken
    {
        public TextValueToken(string text)
        {
            Text = text;
        }

        public string Text { get; }

        public override string ToString() => Text;
    }
}
