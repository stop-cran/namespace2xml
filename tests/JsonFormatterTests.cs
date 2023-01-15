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

        private JsonFormatter CreateFormatter() => new JsonFormatter(
                () => stream,
                new string[0],
                new QualifiedNameMatchDictionary<string>(),
                new QualifiedNameMatchList(),
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
        public async Task ShouldFormatString()
        {
            await CreateFormatter().Write(
                Helpers.ToTree(new { a = new { x = "y" } }), default);

            CheckJson("{\"x\":\"y\"}");
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
