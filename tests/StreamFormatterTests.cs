using Moq;
using Namespace2Xml;
using Namespace2Xml.Formatters;
using Namespace2Xml.Semantics;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Namespace2Xml.Tests
{
    public class StreamFormatterTests
    {
        private Mock<Func<Stream>> outputStreamFactory;
        private Mock<StreamFormatter> formatter;

        [SetUp]
        public void Setup()
        {
            outputStreamFactory = new Mock<Func<Stream>>();
            formatter = new Mock<StreamFormatter>(outputStreamFactory.Object);
        }

        [Test]
        public async Task ShouldThrowOnProfileError()
        {
            await formatter.Object.Write(
                new ProfileTreeNode("a".ToNamePart(),
                new[]
                {
                    new ProfileTreeError("x".ToNamePart(),
                    "an error",
                    new SourceMark(0, "1", 1))
                }), default)
                .ContinueWith(t => t.Exception
                    .ShouldBeOfType<AggregateException>()
                    .InnerExceptions
                    .ShouldHaveSingleItem()
                    .ShouldBeOfType<ApplicationException>());

            outputStreamFactory.Verify(f => f(), Times.Never());
        }
    }
}
