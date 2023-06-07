using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Namespace2Xml.Syntax
{
    [Equals(DoNotAddEqualityOperators = true)]
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
                                return "(.*)";

                            default:
                                throw new NotSupportedException();
                        }
                    })) + "$"));
        }

        public bool IsMatch(string s) =>
            match.Value.IsMatch(s);

        [return: NullGuard.AllowNull]
        public IEnumerable<string> GetMatch(NamePart p)
        {
            if (!p.HasSubstitutes)
            {
                var m = match.Value.Match(p.ToString());

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

        public bool HasSubstitutes => Tokens.Any(x => x is SubstituteNameToken);

        public override string ToString() =>
            string.Join("", Tokens);
    }
}
