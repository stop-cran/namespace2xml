using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using Sprache;

namespace Namespace2Xml.Tests
{
    public class QualifiedNameMatchListTests
    {
        [Test]
        public void ShouldMatchMiddleItem()
        {
            var d = new QualifiedNameMatchList();

            d.Add(Parsers.GetQualifiedNameParser().Parse("test.queue-*.xxx"));

            d.IsMatch(Parsers.GetQualifiedNameParser().Parse("test.queue-11.xxx")).ShouldBeTrue();
        }
    }
}
