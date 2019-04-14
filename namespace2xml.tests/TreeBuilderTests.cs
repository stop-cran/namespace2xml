using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using Sprache;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Namespace2Xml.Tests
{
    public class TreeBuilderTests
    {
        private Parser<IEnumerable<IProfileEntry>> parser;
        private TreeBuilder builder;

        [SetUp]
        public void Setup()
        {
            parser = Parsers.Profile;
            builder = new TreeBuilder();
        }

        [Test]
        public void ShouldConvert()
        {
            var profile = parser.TryParse(@"
a=1
a.b=1
a.b.c=2+${a.b.d}/7
a.b.d=3
x.y=2
");
            profile.WasSuccessful.ShouldBeTrue();
            profile.Expectations.ShouldBeEmpty();

            var tree = builder.Build(profile.Value).ToList();

            tree.Count.ShouldBe(3);
            tree[0].Name.ShouldBe("a");
            tree[1].Name.ShouldBe("a");
            tree[1].ShouldBeOfType<ProfileTreeNode>().Children.Count.ShouldBe(2);
            tree[2].Name.ShouldBe("x");

            tree[1].ShouldBeOfType<ProfileTreeNode>().Children[1]
                .ShouldBeOfType<ProfileTreeNode>().Children[0]
                .ShouldBeOfType<ProfileTreeLeaf>().Value
                .ShouldBe("2+3/7");
        }

        [Test]
        public void ShouldApplyReference()
        {
            var profile = parser.TryParse(@"
a.x=2+${a.y}+${a.z}
a.y=${a.z}/5
a.z=${a.w}
a.w=x
");
            profile.WasSuccessful.ShouldBeTrue();
            profile.Expectations.ShouldBeEmpty();

            var tree = builder.Build(profile.Value).ToList();

            tree.ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeNode>()
                .Children
                .First()
                .ShouldBeOfType<ProfileTreeLeaf>().Value
                .ShouldBe("2+x/5+x");
        }

        [Test]
        public void ShouldThrowOnBrokenReference()
        {
            var profile = parser.Parse(@"
x=2+${y}
z=3
");

            Should.Throw<ArgumentException>(() =>
                builder.Build(profile).ToList());
        }

        [Test]
        public void ShouldThrowOnCyclicReference()
        {
            var profile = parser.Parse(@"
x=2+${y}/7
y=3+${x}
");

            Should.Throw<ArgumentException>(() =>
                builder.Build(profile).ToList());
        }

        [Test]
        [TestCase("x.a=${x.*}\nx.b=1")]
        [TestCase("x.*.*=*-*-*")]
        [TestCase("x.*.*.*=*-*")]
        public void ShouldThrowOnInconclusiveSubstitutes(string text)
        {
            var profile = parser.Parse(text);

            Should.Throw<NotSupportedException>(() =>
                builder.Build(profile).ToList());
        }

        [Test]
        public void ShouldSubstituteOnStarChar()
        {
            var profile = parser.Parse("x.a=*");

            builder.Build(profile)
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeNode>()
                .Children
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeLeaf>()
                .Value.ShouldBe("*");
        }

        [Test]
        public void ShouldEraseUnmatchedSubstitutes()
        {
            var profile = parser.Parse(@"
x.*.a=1
y.y=2
");

            builder.Build(profile)
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeNode>()
                .Children
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeLeaf>();
        }

        [Test]
        public void ShouldSubstituteStrict()
        {
            var profile = parser.Parse(@"
x.*.a=1
x.y=2
");

            var item = builder.Build(profile)
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeNode>();

            item.Name.ShouldBe("x");
            item.Children.Count.ShouldBe(2);

            var item2 = item.Children[0]
               .ShouldBeOfType<ProfileTreeNode>();

            item2.Name.ShouldBe("y");

            var item3 = item2.Children[0]
                .ShouldBeOfType<ProfileTreeLeaf>();

            item3.Name.ShouldBe("a");
            item3.Value.ShouldBe("1");
        }

        [Test]
        public void ShouldOverride()
        {
            var profile = parser.Parse(@"
x.*.a=1
x.y=2
x.y.a=3
");

            var item = builder.Build(profile)
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeNode>();

            item.Name.ShouldBe("x");
            item.Children.Count.ShouldBe(2);

            var item2 = item.Children[1]
               .ShouldBeOfType<ProfileTreeNode>();

            item2.Name.ShouldBe("y");

            var item3 = item2.Children[0]
                .ShouldBeOfType<ProfileTreeLeaf>();

            item3.Name.ShouldBe("a");
            item3.Value.ShouldBe("3");
        }
    }
}