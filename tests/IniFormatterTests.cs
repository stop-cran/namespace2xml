using Microsoft.Extensions.Logging;
using Moq;
using Namespace2Xml.Formatters;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Namespace2Xml.Tests
{
    public class IniFormatterTests
    {
        private IniFormatter formatter;
        private MemoryStream stream;

        [SetUp]
        public void Setup()
        {
            stream = new MemoryStream();
            formatter = new IniFormatter(
                () => stream,
                new string[0],
                ".",
                Mock.Of<ILogger<IniFormatter>>());
        }

        [Test]
        public async Task ShouldFormatSimpleNamespace()
        {
            await formatter.Write(Helpers.ToTree(new { a = new { b = "1" } }), default);

            CheckIni("b=1");
        }

        [Test]
        public async Task ShouldFormatNamespaceWithSimpleGroup()
        {
            await formatter.Write(Helpers.ToTree(new { a = new { b = new { x = "1" } } }), default);

            CheckIni("[b]x=1");
        }

        [Test]
        public async Task ShouldFormatNamespaceWithSimpleGroupAndMultipleValues()
        {
            await formatter.Write(Helpers.ToTree(new { a = new { b = new { x = "1", y = "2" } } }), default);

            CheckIni("[b]x=1y=2");
        }

        [Test]
        public async Task ShouldFormatNamespaceWithNestedGroup()
        {
            await formatter.Write(Helpers.ToTree(new { a = new { b = new { c = new { x = "1" } } } }), default);

            CheckIni("[b:c]x=1");
        }

        [Test]
        public async Task ShouldFormatNamespaceWithNestedGroupAndMultipleValues()
        {
            await formatter.Write(Helpers.ToTree(new { a = new { b = new { c = new { x = "1", y = "2" } } } }), default);

            CheckIni("[b:c]x=1y=2");
        }

        [Test]
        public async Task ShouldFormatNamespaceWithMultipleNestedGroups()
        {
            await formatter.Write(Helpers.ToTree(new { a = new { b = new { c = new { x = "1" }, d = new { y = "2" } } } }), default);

            CheckIni("[b:c]x=1[b:d]y=2");
        }

        private void CheckIni(string expectedXml) =>
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
