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
        private List<QualifiedName> multiline;
        private MemoryStream stream;

        [SetUp]
        public void Setup()
        {
            strings = new List<QualifiedName>();
            multiline = new List<QualifiedName>();
            stream = new MemoryStream();
        }

        private YamlFormatter CreateFormatter() =>
            new YamlFormatter(
                () => stream,
                new string[0],
                new QualifiedNameMatchDictionary<string>(),
                new QualifiedNameMatchList(),
                new QualifiedNameMatchList(strings),
                new QualifiedNameMatchList(multiline),
                Mock.Of<ILogger<YamlFormatter>>());

        [Test]
        [TestCase("1", "1")]
        [TestCase("0.5", "0.5")]
        [TestCase("null", "")]
        [TestCase(null, "")]
        [TestCase("true", "true")]
        [TestCase("00:05:36", "00:05:36")]
        [TestCase("2018-01-19", "2018-01-19")]
        [TestCase("01-02-2020", "01-02-2020")]
        [TestCase("[]", "[]")]
        [TestCase("{}", "{}")]
        public async Task ShouldFormatValues(string value, string expectedValue)
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

        [Test]
        [TestCase("row1\nrow2", "|-\r\n  row1\r\n  row2")]
        [TestCase("row1\n row2", "|-\r\n  row1\r\n   row2")]
        public async Task ShouldFormatMultiline(string value, string expectedValue)
        {
            multiline.Add(new[] { "a", "x" }.ToQualifiedName());

            await CreateFormatter().Write(
                Helpers.ToTree(new { a = new { x = value } }),
                default);

            CheckYamlRaw("x: " + expectedValue + "\r\n");
        }

        private void CheckYaml(string expectedXml) =>
            Encoding.UTF8.GetString(stream.ToArray())
            .Replace("\r", "")
            .Replace("\n", "")
            .Replace(" ", "")
            .ShouldBe(expectedXml);

        private void CheckYamlRaw(string expectedXml) =>
            Encoding.UTF8.GetString(stream.ToArray())
                .ShouldBe(expectedXml);

        [TearDown]
        public void TearDown()
        {
            stream.Dispose();
        }
    }
}
