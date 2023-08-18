using System;
using Microsoft.Extensions.Logging;
using Moq;
using Namespace2Xml.Formatters;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Namespace2Xml.Tests
{
    public class XmlFormatterTests
    {
        private Mock<IOptions<QualifiedNameOptions>> optionsMock;
        private List<string> outputPrefix, arrays;
        private Dictionary<string, string> keys;
        private MemoryStream stream;

        [SetUp]
        public void Setup()
        {
            keys = new Dictionary<string, string>();
            arrays = new List<string>();
            outputPrefix = new List<string>();
            stream = new MemoryStream();

            optionsMock = new Mock<IOptions<QualifiedNameOptions>>();
            optionsMock.Setup(x => x.Value)
                .Returns(new QualifiedNameOptions { XmlRoot = "XmlRoot"});
        }

        private XmlFormatter CreateFormatter() =>
            new XmlFormatter(
                () => stream,
                outputPrefix,
                Scheme.XmlOptions.NoIndent,
                optionsMock.Object,
                new QualifiedNameMatchDictionary<string>(keys.ToDictionary(x => x.Key.Split('.').ToQualifiedName(), x => x.Value)),
                new QualifiedNameMatchList(arrays
                    .Select(x => x.Split('.').ToQualifiedName())),
                new QualifiedNameMatchList(),
                Mock.Of<ILogger<XmlFormatter>>());

        [Test]
        public async Task ShouldFormatSimpleXml()
        {
            await CreateFormatter().Write(
                Helpers.ToTree(new { a = new { x = "1" } }),
                default);

            CheckXml("<a x=\"1\" />");
        }

        [Test]
        public async Task ShouldFormatKeys()
        {
            keys.Add("a", "test");

            await CreateFormatter().Write(
                Helpers.ToTree(new { a = new { b = new { x = "11" } } }), default);

            CheckXml("<a test=\"b\" x=\"11\" />");
        }

        [Test]
        public async Task ShouldFormatXmlnsSuffix()
        {
            await CreateFormatter().Write(
                new ProfileTreeNode(
                            "a".ToNamePart(),
                            new ProfileTree[]
                            {
                                new ProfileTreeLeaf(Helpers.CreatePayload("xmlns:ddd", "http://example.com"), System.Array.Empty<Comment>(), QualifiedName.Empty),
                                new ProfileTreeNode("b".ToNamePart(),
                                new[]
                                {
                                    new ProfileTreeLeaf(Helpers.CreatePayload("ddd:x", "11"), System.Array.Empty<Comment>(), QualifiedName.Empty)
                                })
                            }), default);

            CheckXml("<a xmlns:ddd=\"http://example.com\"><b ddd:x=\"11\" /></a>");
        }

        [Test]
        public async Task ShouldFormatArrays()
        {
            arrays.Add("a");

            await CreateFormatter().Write(
                Helpers.ToTree(new { a = new { b = new { x = "11" } } }),
                default);

            CheckXml("<a x=\"11\" />");
        }

        [Test]
        public async Task ShouldFormatXmlns()
        {
            await CreateFormatter().Write(
                Helpers.ToTree(new { a = new { xmlns = "http://example.com", x = "1" } }),
                default);

            CheckXml("<a x=\"1\" xmlns=\"http://example.com\" />");
        }

        [Test]
        public async Task ShouldApplyOutputPrefix()
        {
            outputPrefix.Add("xx");
            outputPrefix.Add("yy");

            await CreateFormatter().Write(
                Helpers.ToTree(new { a = new { x = "1" } }),
                default);

            CheckXml("<xx><yy x=\"1\" /></xx>");
        }

        private void CheckXml(string expectedXml) =>
            Encoding.UTF8.GetString(stream.ToArray())
            .ShouldBe("<?xml version=\"1.0\" encoding=\"utf-8\"?>" + expectedXml);

        [TearDown]
        public void TearDown()
        {
            stream.Dispose();
        }
    }
}
