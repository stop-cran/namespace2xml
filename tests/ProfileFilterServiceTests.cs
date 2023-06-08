using System.Collections.Generic;
using System.Linq;
using Namespace2Xml.Services;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using Sprache;

namespace Namespace2Xml.Tests;

public class ProfileFilterServiceTests
{
    private Parser<IEnumerable<IProfileEntry>> parser;
    private IProfileFilterService service;

    [SetUp]
    public void SetUp()
    {
        parser = Parsers.GetProfileParser(0, "testfile");
        service = new ProfileFilterService();
    }

    [Test]
    [TestCase("a.b=1\nb.c=2", "output=namespace", 2)]
    [TestCase("a.b=1\nb.c=2", "a.output=namespace\nb.output=namespace", 2)]
    [TestCase("a.b=1\nb.c=2", "a.output=namespace\na.output=namespace", 1)]
    [TestCase("a.b=1\na.c=2", "a.output=namespace", 2)]
    [TestCase("a.b=1\nb.c=2", "a.output=namespace", 1)]
    [TestCase("a.b=1\n*.c=2", "a.output=namespace", 2)]
    [TestCase("a.b=1\nb.c=2", "*.output=namespace", 2)]
    [TestCase("a1.b1=1\na2.b2=1\nb.c=2", "a*.output=namespace", 2)]
    [TestCase("a.b=${b.c}\nb.c=1", "a.output=namespace", 2)]
    [TestCase("a.b=${b.c}\nb.c=${c.d}\nc.d=1", "a.output=namespace", 3)]
    [TestCase("x*.b=1", "a.output=namespace", 0)]
    [TestCase("a*.b=1", "a.output=namespace", 1)]
    [TestCase("a*.b=1", "*.output=namespace", 1)]
    public void ShouldFilter(string inputsString, string schemesString, int expectedCount)
    {
        var inputs = parser.TryParse(inputsString);
        var schemes = parser.TryParse(schemesString);
        service.FilterByOutput(inputs.Value.ToList(), schemes.Value.ToList()).Count.ShouldBe(expectedCount);
    }
}