using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using Sprache;

namespace Namespace2Xml.Tests
{
    public class QualifiedNameMatchDictionaryTests
    {
        [Test]
        public void ShouldMatchMiddleItem()
        {
            var d = new QualifiedNameMatchDictionary<string>();

            d.Add(Parsers.GetQualifiedNameParser().Parse("test.queue-*.xxx"), "test");

            d.TryMatch(Parsers.GetQualifiedNameParser().Parse("test.queue-11.xxx"), out _).ShouldBeTrue();
        }
    }
}
