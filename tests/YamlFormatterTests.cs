using Microsoft.Extensions.Logging;
using Moq;
using Namespace2Xml.Formatters;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Namespace2Xml.Tests
{
    public class YamlFormatterTests
    {
        private List<QualifiedName> strings;
        private MemoryStream stream;

        [SetUp]
        public void Setup()
        {
            strings = new List<QualifiedName>();
            stream = new MemoryStream();
        }

        private YamlFormatter CreateFormatter() =>
            new YamlFormatter(
                () => stream,
                new string[0],
                new QualifiedNameMatchDictionary<string>(),
                new QualifiedNameMatchList(),
                new QualifiedNameMatchList(strings),
                Mock.Of<ILogger<YamlFormatter>>());

        [Test]
        [TestCase("1", "1")]
        [TestCase("0.5", "0.5")]
        [TestCase("null", "")]
        [TestCase("true", "true")]
        [TestCase("00:05:36", "00:05:36")]
        [TestCase("2018-01-19", "2018-01-19T00:00:00.0000000")]
        public async Task ShouldFormatSimpleJson(string value, string expectedValue)
        {
            await CreateFormatter().Write(
                Helpers.ToTree(new { a = new { x = value } }),
                default);

            CheckYaml("x:" + expectedValue);
        }

        [Test]
        [TestCase("true", "'true'")]
        [TestCase("null", "'null'")]
        public async Task ShouldFormatQuotedStrings(string value, string expectedValue)
        {
            strings.Add(new[] { "a", "x" }.ToQualifiedName());
            await CreateFormatter().Write(
                Helpers.ToTree(new { a = new { x = value } }),
                default);

            CheckYaml("x:" + expectedValue);
        }

        private void CheckYaml(string expectedXml) =>
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
