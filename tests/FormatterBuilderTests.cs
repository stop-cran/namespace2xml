using log4net;
using Moq;
using Namespace2Xml.Formatters;
using Namespace2Xml.Scheme;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;

namespace Namespace2Xml.Tests
{
    public class FormatterBuilderTests
    {
        private Mock<IStreamFactory> streamFactory;
        private Mock<ILog> logger;
        private FormatterBuilder formatterBuilder;

        [SetUp]
        public void Setup()
        {
            streamFactory = new Mock<IStreamFactory>();
            logger = new Mock<ILog>();
            formatterBuilder = new FormatterBuilder(streamFactory.Object, logger.Object);
        }

        [Test]
        public void ShouldBuildIgnoreBuilder()
        {
            var formatters = formatterBuilder.Build(
                new SchemeNode("a".ToNamePart(),
                new[]
                {
                    new SchemeLeaf(EntryType.output, "xml,json,yaml,namespace,ini")
                }))
                .ToList();

            formatters.Count.ShouldBe(5);

            foreach (var (prefix, formatter) in formatters)
            {
                prefix.Parts
                    .ShouldHaveSingleItem()
                    .Tokens
                    .ShouldHaveSingleItem()
                    .ShouldBeOfType<TextNameToken>()
                    .Text
                    .ShouldBe("a");

                formatter.ShouldBeOfType<IgnoreFormatter>();
            }
        }

        [Test]
        public void ShouldDetectSchemeError()
        {
            Should.Throw<ApplicationException>(() =>
            formatterBuilder.Build(
                new SchemeNode("a".ToNamePart(),
                new ISchemeEntry[]
                {
                    new SchemeLeaf(EntryType.output, "xml"),
                    new SchemeError("error1", new SourceMark(0,"testfile", 12)),
                })).ToList());
            logger.Verify(l => l.Error(It.IsAny<object>()));
        }

        [Test]
        public void ShouldIgnoreSchemeIfNoOutput()
        {
            formatterBuilder.Build(
                new SchemeNode("a".ToNamePart(),
                new ISchemeEntry[]
                {
                    new SchemeLeaf(EntryType.filename, "xxx"),
                }))
                .ShouldBeEmpty();
        }
    }
}
