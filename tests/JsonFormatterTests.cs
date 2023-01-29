using Microsoft.Extensions.Logging;
using Moq;
using Namespace2Xml.Formatters;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Namespace2Xml.Tests
{
    public class JsonFormatterTests
    {
        private QualifiedNameMatchList strings;
        private MemoryStream stream;

        [SetUp]
        public void Setup()
        {
            strings = new QualifiedNameMatchList();
            stream = new MemoryStream();
        }

        private JsonFormatter CreateFormatter(params string[] arrays) => new JsonFormatter(
                () => stream,
                new string[0],
                new QualifiedNameMatchDictionary<string>(),
                new QualifiedNameMatchList(arrays.Select(a => a.Split('.').ToQualifiedName())),
                strings,
                Mock.Of<ILogger<JsonFormatter>>());

        [Test]
        public async Task ShouldFormatSimpleJson()
        {
            await CreateFormatter().Write(
                Helpers.ToTree(new { a = new { x = "1" } }),
                default);

            CheckJson("{\"x\":1}");
        }

        [Test]
        [TestCase("y", "{\"x\":\"y\"}")]
        [TestCase("{}", "{\"x\":{}}")]
        [TestCase("{}", "{\"x\":{}}")]
        [TestCase("[]", "{\"x\":[]}")]
        [TestCase("null", "{\"x\":null}")]
        public async Task ShouldFormatString(string y, string expected)
        {
            await CreateFormatter().Write(
                Helpers.ToTree(new { a = new { x = y } }), default);

            CheckJson(expected);
        }

        [Test]
        public async Task ShouldFormatArray()
        {
            await CreateFormatter("a.x").Write(
                Helpers.ToTree(new { a = new { x = new[] { "a", "b", "c" } } }), default);

            CheckJson("{\"x\":[\"a\",\"b\",\"c\"]}");
        }

        [Test]
        public async Task ShouldFormatStringForce()
        {
            strings.Add(new[] { "a", "x" }.ToQualifiedName());

            await CreateFormatter().Write(
                Helpers.ToTree(new { a = new { x = "1" } }), default);

            CheckJson("{\"x\":\"1\"}");
        }

        private void CheckJson(string expectedXml) =>
            Encoding.UTF8.GetString(stream.ToArray())
            .Replace("\r", "")
            .Replace("\n", "")
            .Replace(" ", "")
            .ShouldBe(expectedXml);

        [TearDown]
        public void TearDown()
        {
            stream.Dispose();
        }
    }
}
