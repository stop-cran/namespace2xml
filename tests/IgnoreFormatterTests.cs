using Microsoft.Extensions.Logging;
using Moq;
using Namespace2Xml.Formatters;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Namespace2Xml.Tests
{
    public class IgnoreFormatterTests
    {
        private MemoryStream stream;
        private QualifiedNameMatchList ignoreList;

        [SetUp]
        public void Setup()
        {
            stream = new MemoryStream();
            ignoreList = new QualifiedNameMatchList();
        }

        private IgnoreFormatter CreateFormatter() =>
            new IgnoreFormatter(
                new NamespaceFormatter(
                    () => stream,
                    Array.Empty<string>(),
                    ".",
                    Mock.Of<ILogger<NamespaceFormatter>>()), ignoreList);

        [Test]
        public async Task ShouldIgnore()
        {
            ignoreList.Add(new[] { "a", "y" }.ToQualifiedName());
            await CreateFormatter()
                .Write(Helpers.ToTree(new { a = new { x = "1", y = "2" } }), default);

            CheckNamespace("x=1");
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
