using Sprache;
using System.Collections.Generic;

namespace Namespace2Xml.Syntax
{
    public static class Parsers
    {
        public static Parser<QualifiedName> QualifiedName
        {
            get
            {
                var nameChar = Parse.Char('\\')
                    .Then(_ => Parse.Chars("\\*.}"))
                    .Or(Parse.CharExcept("*.=}")
                        .Except(Parse.LineTerminator));

                var textNameToken = nameChar
                    .AtLeastOnce()
                    .Text()
                    .Select(parsedName =>
                        (INameToken)new TextNameToken(parsedName));

                var substituteNameToken = Parse.Char('*')
                    .Return((INameToken)new SubstituteNameToken())
                    .Named("name substitute");

                var namePart = substituteNameToken
                    .Or(textNameToken)
                    .AtLeastOnce()
                    .Select(tokens => new NamePart(tokens))
                    .Named("name part");

                return namePart
                    .DelimitedBy(Parse.Char('.'))
                    .Select(parsedName => new QualifiedName(parsedName))
                    .Named("qualified name");
            }
        }

        public static Parser<IEnumerable<IProfileEntry>> Profile
        {
            get
            {
                var textValueToken = Parse.CharExcept("*")
                    .Except(Parse.LineTerminator)
                    .Except(Parse.String("${"))
                    .AtLeastOnce()
                    .Text()
                    .Select(text =>
                        (IValueToken)new TextValueToken(text))
                    .Named("text value");

                var referenceValueToken = Parse.String("${")
                    .Then(_ => QualifiedName)
                    .Then(parsedName => Parse.Char('}')
                        .Return((IValueToken)new ReferenceValueToken(parsedName)))
                    .Named("reference");

                var substituteValueToken = Parse.Char('*')
                    .Return((IValueToken)new SubstituteValueToken())
                    .Named("* substitute value");

                var value = referenceValueToken
                    .Or(substituteValueToken)
                    .Or(textValueToken)
                    .Many()
                    .Named("value");

                var comment = Parse.Char('#').Token()
                    .Then(_ => Parse.AnyChar
                        .Except(Parse.LineTerminator)
                        .Many()
                        .Text()
                        .Select(parsedComment =>
                            (IProfileEntry)new Comment(parsedComment)))
                        .Named("comment");

                var payload = from parsedName in QualifiedName
                              from c in Parse.Char('=')
                              from parsedValue in value
                              select new { parsedName, parsedValue };

                var payloadSpan = from span in payload.Named("payload").Span()
                                  select (IProfileEntry)new Payload(
                                      span.Value.parsedName,
                                      span.Value.parsedValue,
                                      span.Start.Line);

                return comment
                    .Or(payloadSpan)
                    .DelimitedBy(Parse.LineTerminator.AtLeastOnce())
                    .Contained(
                        Parse.LineTerminator.Many(),
                        Parse.LineTerminator.Many())
                    .End();
            }
        }
    }
}
