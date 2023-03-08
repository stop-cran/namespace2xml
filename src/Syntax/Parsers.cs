using Sprache;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Syntax
{
    public static class Parsers
    {
        public static Parser<NamePart> GetNamePartParser()
        {
            var nameChar = Parse.CharExcept("\\*.=}")
                .Except(Parse.LineTerminator);

            var delimiterEscaping = Parse.String("\\.")
                .Select(x => x.Where(y => y != '\\'));

            var textNameToken = nameChar
                .AtLeastOnce()
                .Or(delimiterEscaping)
                .Text()
                .Select(parsedName =>
                    (INameToken)new TextNameToken(parsedName));

            var substituteNameToken = Parse.Char('*')
                .Return((INameToken)new SubstituteNameToken())
                .Named("name substitute");

            return substituteNameToken
                .Or(textNameToken)
                .AtLeastOnce()
                .Select(tokens => new NamePart(tokens))
                .Named("name part");
        }

        public static Parser<QualifiedName> GetQualifiedNameParser() =>
            GetNamePartParser()
                .DelimitedBy(Parse.Char('.'))
                .Select(parsedName => new QualifiedName(parsedName))
                .Named("qualified name");

        public static Parser<IEnumerable<IValueToken>> GetValueParser()
        {
            var textValueToken = Parse.CharExcept("*")
                .Except(Parse.LineTerminator)
                .Except(Parse.String("${"))
                .AtLeastOnce()
                .Text()
                .Select(text =>
                    (IValueToken)new TextValueToken(text))
                .Named("text value");

            var referenceValueToken = GetReferenceValueToken();

            var substituteValueToken = Parse.Char('*')
                .Return((IValueToken)new SubstituteValueToken())
                .Named("* substitute value");

            return referenceValueToken
                .Or(substituteValueToken)
                .Or(textValueToken)
                .Or(Parse.AnyChar.Except(Parse.LineTerminator).Many().Text().Select(x => (IValueToken)new TextValueToken(x)))
                .Many()
                .Named("value");
        }

        public static Parser<IValueToken> GetReferenceValueToken()
        {
            return Parse.String("${")
                .Then(_ => GetQualifiedNameParser())
                .Then(parsedName =>
                    Parse.Char('}').Return((IValueToken)new ReferenceValueToken(parsedName)))
                .Named("reference");
        }

        public static Parser<IEnumerable<IProfileEntry>> GetProfileParser(int fileNumber, string fileName)
        {
            var qualifiedName = GetQualifiedNameParser();
            var value = GetValueParser();

            var comment = Parse.Char('#').Token()
                .Then(_ => Parse.AnyChar
                    .Except(Parse.LineTerminator)
                    .Many()
                    .Text()
                    .Select(parsedComment =>
                        (IProfileEntry)new Comment(parsedComment)))
                    .Named("comment");

            var payload = from parsedName in qualifiedName
                          from parsedValue in Parse.Char('=').Then(_ => value)
                          select new { parsedName, parsedValue };

            var payloadSpan = payload
                .Named("payload")
                .Span()
                .Select(span =>
                    (IProfileEntry)new Payload(
                        span.Value.parsedName,
                        span.Value.parsedValue,
                        new SourceMark(fileNumber, fileName, span.Start.Line)));

            var payloadNameIgnore = Parse.Char('!').Token()
                .Then(_ => GetQualifiedNameParser())
                .Span()
                .Select(span =>
                    (IProfileEntry)new PayloadIgnore(
                        span.Value,
                        new SourceMark(fileNumber, fileName, span.Start.Line)))
                .Named("payloadNameIgnore");

            var payloadIgnore = Parse.Char('!').Token()
                .Then(_ => payload)
                .Span()
                .Select(span =>
                    (IProfileEntry)new PayloadIgnore(
                        span.Value.parsedName,
                        new SourceMark(fileNumber, fileName, span.Start.Line)))
                .Named("payload");

            return comment
                .Or(payloadIgnore)
                .Or(payloadNameIgnore)
                .Or(payloadSpan)
                .DelimitedBy(Parse.LineTerminator.AtLeastOnce())
                .Contained(
                    Parse.LineTerminator.Many(),
                    Parse.LineTerminator.Many())
                .End();
        }
    }
}
