namespace Namespace2Xml.Syntax
{
    [Equals(DoNotAddEqualityOperators = true)]
    public sealed class SubstituteNameToken : INameToken
    {
        public override string ToString() => "*";
    }
}
