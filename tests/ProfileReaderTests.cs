using log4net;
using Moq;
using Namespace2Xml;
using Namespace2Xml.Formatters;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Namespace2Xml.Tests
{
    public class ProfileReaderTests
    {
        private Mock<IStreamFactory> streamFactory;
        private Mock<ILog> logger;
        private MemoryStream output;

        [SetUp]
        public void Setup()
        {
            output = new MemoryStream();
            streamFactory = new Mock<IStreamFactory>();
            logger = new Mock<ILog>();

            streamFactory
                .Setup(f => f.CreateInputStream("input"))
                .Returns<string>(_ => new MemoryStream(Encoding.UTF8.GetBytes("a.x=1")));
        }

        [Test]
        public async Task ShouldReadFiles()
        {
            var entries = await new ProfileReader(
                streamFactory.Object,
                logger.Object)
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
        public async Task ShouldLogReadError()
        {
            await new ProfileReader(
                streamFactory.Object,
                logger.Object)
                .ReadFiles(new[] { "_invalid" }, default)
                .ContinueWith(t => t.Exception
                    .ShouldBeOfType<AggregateException>()
                    .InnerExceptions
                    .ShouldHaveSingleItem()
                    .ShouldBeOfType<ApplicationException>());

            logger.Verify(l => l.Error(It.IsAny<object>(), It.IsAny<Exception>()));
        }

        [Test]
        public async Task ShouldThrowApplicationExceptionOnParseError()
        {
            streamFactory
                .Setup(f => f.CreateInputStream("error"))
                .Returns<string>(_ => new MemoryStream(Encoding.UTF8.GetBytes("a.x-1")));

            await new ProfileReader(
                streamFactory.Object,
                logger.Object)
                .ReadFiles(new[] { "error" }, default)
                .ContinueWith(t => t.Exception
                    .ShouldBeOfType<AggregateException>()
                    .InnerExceptions
                    .ShouldHaveSingleItem()
                    .ShouldBeOfType<ApplicationException>());

            logger.Verify(l => l.Error(It.IsAny<object>()));
        }
    }
}
