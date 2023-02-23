using System.Linq;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using Sprache;

namespace Namespace2Xml.Tests;

public class ParsersTests
{
    [Test]
    public void TextNamePartParserTest()
    {
        var parser = Parsers.GetNamePartParser();
        var res = parser.Parse("a");
        res.Tokens.Count.ShouldBe(1);
        (res.Tokens.First() is TextNameToken).ShouldBe(true);
    }

    [Test]
    public void SubstituteNamePartParserTest()
    {
        var parser = Parsers.GetNamePartParser();
        var res = parser.Parse("*");
        res.Tokens.Count.ShouldBe(1);
        (res.Tokens.First() is SubstituteNameToken).ShouldBe(true);
    }

    [Test]
    [TestCase("a", 1, "a")]
    [TestCase("a.b", 1, "a")]
    [TestCase("*", 1, "*")]
    [TestCase(@"a-*.b", 2, "a-*")]
    [TestCase(@"a\.b", 3, @"a.b")]
    [TestCase(@"a\.b\.c.d", 5, @"a.b.c")]
    public void NamePartParserTest(string input, int tokensCount, string namePart)
    {
        var parser = Parsers.GetNamePartParser();
        var res = parser.Parse(input);
        res.Tokens.Count.ShouldBe(tokensCount);
        res.ToString().ShouldBe(namePart);
    }

    [Test]
    [TestCase("a", 1)]
    [TestCase("a.b", 2)]
    [TestCase(@"a-*.*.b", 3)]
    [TestCase(@"a\.b", 1)]
    [TestCase(@"a.b\.c.*", 3)]
    public void QualifiedNameParserTests(string input, int namesCount)
    {
        var parser = Parsers.GetQualifiedNameParser();
        var res = parser.Parse(input);
        res.Parts.Count.ShouldBe(namesCount);
    }

    [Test]
    [TestCase("a", 1)]
    [TestCase("*", 1)]
    [TestCase("${a}", 1)]
    [TestCase("a${a}*", 3)]
    [TestCase("a${*}*", 3)]
    public void ValueParserTest(string input, int tokensCount)
    {
        var parser = Parsers.GetValueParser();
        var res = parser.Parse(input);
        res.Count().ShouldBe(tokensCount);
    }

    [Test]
    public void TextValueParserTest()
    {
        var parser = Parsers.GetValueParser();
        var res = parser.Parse("a").ToList();
        res.Count.ShouldBe(1);
        (res.First() is TextValueToken).ShouldBe(true);
    }

    [Test]
    [TestCase("${a}", 1)]
    [TestCase("${*}", 1)]
    [TestCase("${a*}", 1)]
    [TestCase("${a.*}", 2)]
    public void ReferenceValueParserTest(string input, int namesCount)
    {
        var parser = Parsers.GetValueParser();
        var res = parser.Parse(input).ToList();
        res.Count.ShouldBe(1);
        (res.First() is ReferenceValueToken).ShouldBe(true);
        QualifiedNameParserTests(((ReferenceValueToken)res.First()).Name.ToString(), namesCount);
    }
}