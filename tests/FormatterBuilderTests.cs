using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
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
        private ILoggerFactory loggerFactory;
        private FormatterBuilder formatterBuilder;

        [SetUp]
        public void Setup()
        {
            streamFactory = new Mock<IStreamFactory>();
            loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new ConsoleLoggerProvider(Mock.Of<IOptionsMonitor<ConsoleLoggerOptions>>(f => f.CurrentValue == new ConsoleLoggerOptions())));
            formatterBuilder = new FormatterBuilder(streamFactory.Object, loggerFactory);
        }

        [Test]
        public void ShouldBuildIgnoreBuilder()
        {
            var formatters = formatterBuilder.Build(
                new SchemeNode("a".ToNamePart(),
                new[]
                {
                    new SchemeLeaf(EntryType.output, "xml,json,yaml,namespace,ini", Helpers.CreatePayload("", ""))
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
                    new SchemeLeaf(EntryType.output, "xml", Helpers.CreatePayload("", "")),
                    new SchemeError("error1", new SourceMark(0,"testfile", 12)),
                })).ToList());
            // RK TODO: logger.Verify(l => l.Error(It.IsAny<object>()));
        }

        [Test]
        public void ShouldIgnoreSchemeIfNoOutput()
        {
            formatterBuilder.Build(
                new SchemeNode("a".ToNamePart(),
                new ISchemeEntry[]
                {
                    new SchemeLeaf(EntryType.filename, "xxx", Helpers.CreatePayload("", "")),
                }))
                .ShouldBeEmpty();
        }
    }
}
