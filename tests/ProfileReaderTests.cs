using Microsoft.Extensions.Logging;
using Moq;
using Namespace2Xml;
using Namespace2Xml.Formatters;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Namespace2Xml.Tests
{
    public class ProfileReaderTests
    {
        private Mock<IStreamFactory> streamFactory;
        private MemoryStream output;

        private Mock<IOptions<QualifiedNameOptions>> optionsMock;

        [SetUp]
        public void Setup()
        {
            output = new MemoryStream();
            streamFactory = new Mock<IStreamFactory>();

            optionsMock = new Mock<IOptions<QualifiedNameOptions>>();
            optionsMock.Setup(x => x.Value)
                .Returns(new QualifiedNameOptions { ImplicitRoot = "ImplicitRoot" });

            streamFactory
                .Setup(f => f.CreateInputStream("input"))
                .Returns<string>(_ => new MemoryStream(Encoding.UTF8.GetBytes("a.x=1")));
        }

        [Test]
        public async Task ShouldReadFiles()
        {
            var entries = await new ProfileReader(
                streamFactory.Object,
                optionsMock.Object,
                Mock.Of<ILogger<ProfileReader>>())
                .ReadFiles(new[] { "input" }, default);

            var payload = entries
                .ShouldHaveSingleItem()
                .ShouldBeOfType<Payload>();

            payload.Name.Parts.Count.ShouldBe(2);
            payload.Name.Parts[0].Tokens
                .ShouldHaveSingleItem()
                .ShouldBeOfType<TextNameToken>()
                .Text
                .ShouldBe("a");
            payload.Name.Parts[1].Tokens
                .ShouldHaveSingleItem()
                .ShouldBeOfType<TextNameToken>()
                .Text
                .ShouldBe("x");
            payload.Value
                .ShouldHaveSingleItem()
                .ShouldBeOfType<TextValueToken>()
                .Text
                .ShouldBe("1");
        }

        [Test]
        [TestCase("input.json", "{\"a\": {\"c\": [11,\"22\"], \"d\": {}, \"e\": [], \"f\": \"asdfgh\", \"g\": null, \"b-*\": {\"z\": \"qwerty/${a.*.y}\"}}}", "ImplicitRoot.a.c.0=11;ImplicitRoot.a.c.1=22;ImplicitRoot.a.d={};ImplicitRoot.a.e=[];ImplicitRoot.a.f=asdfgh;ImplicitRoot.a.g=null;ImplicitRoot.a.b-*.z=qwerty/${a.*.y}")]
        [TestCase("input.yaml", "a:\n  c: [11, \"22\"]\n  d: {}\n  e: []\n  f: asdfgh\n  \"b-*\":\n    z: \"qwerty/${a.*.y}\"", "ImplicitRoot.a.c.0=11;ImplicitRoot.a.c.1=22;ImplicitRoot.a.d={};ImplicitRoot.a.e=[];ImplicitRoot.a.f=asdfgh;ImplicitRoot.a.b-*.z=qwerty/${a.*.y}")]
        [TestCase("input.xml", "<a xmlns=\"uri:example.com\"><b z=\"qwerty/${a.*.y}\" /><c><d e=\"11\"/><d e=\"22\"/></c><f/></a>", "a.xmlns=uri:example.com;a.b.z=qwerty/${a.*.y};a.c.d.0.e=11;a.c.d.1.e=22;a.f=")]
        public async Task ShouldReadFormattedFile(string inputFileName, string inputText, string expected)
        {
            streamFactory
                .Setup(f => f.CreateInputStream(inputFileName))
                .Returns<string>(_ => new MemoryStream(Encoding.UTF8.GetBytes(inputText)));

            var entries = await new ProfileReader(
                streamFactory.Object,
                optionsMock.Object,
                Mock.Of<ILogger<ProfileReader>>())
                .ReadFiles(new[] { inputFileName }, default);

            entries.OfType<Payload>().Select(p => p.ToString()).ShouldBe(expected.Split(";"));
        }

        [Test]
        public async Task ShouldLogReadError()
        {
            await new ProfileReader(
                streamFactory.Object,
                optionsMock.Object,
                Mock.Of<ILogger<ProfileReader>>())
                .ReadFiles(new[] { "_invalid" }, default)
                .ContinueWith(t => t.Exception
                    .ShouldBeOfType<AggregateException>()
                    .InnerExceptions
                    .ShouldHaveSingleItem()
                    .ShouldBeOfType<ApplicationException>());

            // RK TODO: logger.Verify(l => l.Error(It.IsAny<object>(), It.IsAny<Exception>()));
        }

        [Test]
        public async Task ShouldThrowApplicationExceptionOnParseError()
        {
            streamFactory
                .Setup(f => f.CreateInputStream("error"))
                .Returns<string>(_ => new MemoryStream(Encoding.UTF8.GetBytes("a.x-1")));

            await new ProfileReader(
                streamFactory.Object,
                optionsMock.Object,
                Mock.Of<ILogger<ProfileReader>>())
                .ReadFiles(new[] { "error" }, default)
                .ContinueWith(t => t.Exception
                    .ShouldBeOfType<AggregateException>()
                    .InnerExceptions
                    .ShouldHaveSingleItem()
                    .ShouldBeOfType<ApplicationException>());

            // RK TODO: logger.Verify(l => l.Error(It.IsAny<object>()));
        }
    }
}
