using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Namespace2Xml.Syntax
{
    [Equals]
    public sealed class NamePart
    {
        private readonly Lazy<Regex> match;

        public NamePart(IEnumerable<INameToken> tokens)
        {
            Tokens = tokens.ToList();

            match = new Lazy<Regex>(() => new Regex(
                    "^" + string.Join("", Tokens.Select(p =>
                    {
                        switch (p)
                        {
                            case TextNameToken t:
                                return Regex.Escape(t.Text);

                            case SubstituteNameToken s:
                                return "(.+)";

                            default:
                                throw new NotSupportedException();
                        }
                    })) + "$"));
        }

        [return: NullGuard.AllowNull]
        public IEnumerable<string> GetMatch(NamePart p)
        {
            //var texts = p.Tokens.OfType<TextNameToken>().Select(t => t.Text).ToList();

            //if (texts.Count == p.Tokens.Count)
            //{
            //    var m = match.Value.Match(string.Join("", texts));

            if (p.Tokens.Count == 1 &&
                p.Tokens[0] is TextNameToken textToken)
            {
                var m = match.Value.Match(textToken.Text);

                if (m.Success)
                    return m.Groups
                        .Cast<Group>()
                        .Skip(1)
                        .Select(group => group.Value);
                return null;
            };

            return null;
        }

        public IReadOnlyList<INameToken> Tokens { get; }

        public override string ToString() =>
            string.Join("", Tokens);
    }
}
