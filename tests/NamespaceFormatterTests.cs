using Namespace2Xml.Formatters;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Namespace2Xml.Tests
{
    public class NamespaceFormatterTests
    {
        private NamespaceFormatter formatter;
        private MemoryStream stream;

        [SetUp]
        public void Setup()
        {
            stream = new MemoryStream();
            formatter = new NamespaceFormatter(
                () => stream,
                new string[0],
                ".");
        }

        [Test]
        public async Task ShouldFormatPayload()
        {
            await formatter.Write(
                Helpers.ToTree(new { a = new { b = new { x = "1" } } }),
                default);

            CheckNamespace("b.x=1");
        }

        [Test]
        public async Task ShouldEscapeNames()
        {
            await formatter.Write(
                new ProfileTreeNode("a".ToNamePart(),
                new[]
                {
                    new ProfileTreeLeaf(Helpers.CreatePayload("x.y", "1"), System.Array.Empty<Comment>(), QualifiedName.Empty)
                }), default);

            CheckNamespace("x\\.y=1");
        }

        [Test]
        public async Task ShouldFormatComment()
        {
            await formatter.Write(
                new ProfileTreeNode("a".ToNamePart(),
                new[]
                {
                    new ProfileTreeLeaf(Helpers.CreatePayload("x", "1"), new [] { new Comment("test") }, QualifiedName.Empty)
                }), default);

            CheckNamespace("# test\nx=1");
        }

        private void CheckNamespace(string expectedXml) =>
            Encoding.UTF8.GetString(stream.ToArray())
            .Replace("\r", "")
            .Trim()
            .ShouldBe(expectedXml);

        [TearDown]
        public void TearDown()
        {
            stream.Dispose();
        }
    }
}
