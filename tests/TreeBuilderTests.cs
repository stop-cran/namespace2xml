using Microsoft.Extensions.Logging;
using Moq;
using Namespace2Xml.Formatters;
using Namespace2Xml.Semantics;
using Namespace2Xml.Syntax;
using NUnit.Framework;
using Shouldly;
using Sprache;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Namespace2Xml.Tests
{
    public class TreeBuilderTests
    {
        private Parser<IEnumerable<IProfileEntry>> parser;
        private TreeBuilder builder;

        [SetUp]
        public void Setup()
        {
            parser = Parsers.GetProfileParser(0, "testfile");
            builder = new TreeBuilder(Mock.Of<ILogger<TreeBuilder>>());
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

            var tree = builder.Build(profile.Value, new QualifiedNameMatchDictionary<Scheme.SubstituteType>()).ToList();

            tree.Count.ShouldBe(3);
            tree[0].NameString.ShouldBe("a");
            tree[1].NameString.ShouldBe("a");
            tree[1].ShouldBeOfType<ProfileTreeNode>().Children.Count.ShouldBe(2);
            tree[2].NameString.ShouldBe("x");

            tree[1].ShouldBeOfType<ProfileTreeNode>().Children[1]
                .ShouldBeOfType<ProfileTreeNode>().Children[0]
                .ShouldBeOfType<ProfileTreeLeaf>().Value
                .ShouldBe("2+3/7");
        }

        [Test]
        public void ShouldAppendComments()
        {
            var profile = parser.TryParse(@"
# test1
a=1
");
            profile.WasSuccessful.ShouldBeTrue();
            profile.Expectations.ShouldBeEmpty();

            var tree = builder.Build(profile.Value,
                new QualifiedNameMatchDictionary<Scheme.SubstituteType>()).ToList();

            tree.ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeLeaf>()
                .LeadingComments
                .ShouldHaveSingleItem()
                .Text
                .ShouldBe("test1");
        }

        [Test]
        public void ShouldOverride()
        {
            var tree = builder.Build(parser.Parse("a=1").Concat(parser.Parse("a=2")),
                new QualifiedNameMatchDictionary<Scheme.SubstituteType>()).ToList();

            tree.ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeLeaf>()
                .Value.ShouldBe("2");

            // RK TODO: logger.Verify(l => l.Debug(It.IsAny<object>()));
        }

        [Test]
        public void ShouldGenerateProfileErrorOnMissingReferences()
        {
            var tree = builder.Build(parser.Parse("a=${b}"),
                new QualifiedNameMatchDictionary<Scheme.SubstituteType>()).ToList();

            tree.ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeError>()
                .Error.ShouldBe("Reference b was not found at a [file: testfile, line: 1].");
        }

        [Test]
        public void ShouldGenerateProfileErrorOnCyclicReferences()
        {
            var tree = builder.Build(parser.Parse("a=${b}\nb=${c}\nc=${a}"),
                new QualifiedNameMatchDictionary<Scheme.SubstituteType>()).ToList();

            tree.First()
                .ShouldBeOfType<ProfileTreeError>()
                .Error.ShouldBe("Cyclic reference: a [file: testfile, line: 1] > b [file: testfile, line: 2] > c [file: testfile, line: 3] > a [file: testfile, line: 1].");
        }

        [Test]
        public void ShouldSkipUnmatchedReferenceSubstitutes()
        {
            var tree = builder.Build(parser.Parse("a.x.y=1\na.*.*=1,*,${c.*}\nc.z=3"),
                new QualifiedNameMatchDictionary<Scheme.SubstituteType>()).ToList();

            tree.Count.ShouldBe(2);

            var leaf = tree
                .First()
                .ShouldBeOfType<ProfileTreeNode>()
                .Children
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeNode>()
                .Children
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeLeaf>();

            leaf.Name.Tokens.ShouldHaveSingleItem().ShouldBeOfType<TextNameToken>().Text.ShouldBe("y");
            leaf.Value.ShouldBe("1");
        }

        [Test]
        public void ShouldApplyReferenceSubstitutes()
        {
            var tree = builder.Build(parser.Parse("a.x.y=1\na.*.*=1,*,${c.*}\nc.x=3"),
                new QualifiedNameMatchDictionary<Scheme.SubstituteType>()).ToList();

            tree.Count.ShouldBe(2);

            var leaf = tree
                .First()
                .ShouldBeOfType<ProfileTreeNode>()
                .Children
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeNode>()
                .Children
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeLeaf>();

            leaf.Name.Tokens.ShouldHaveSingleItem().ShouldBeOfType<TextNameToken>().Text.ShouldBe("y");
            leaf.Value.ShouldBe("1,x,3");
        }

        [Test]
        [TestCase("a.x=1", "a.*=2", "x", "2")]
        [TestCase("a.x=1", "a.*=*", "x", "x")]
        [TestCase("a.x=1", "a.x=1*", "x", "1*")]
        [TestCase("a.x=1\nb.x=2", "a.*=${b.*}+1", "x", "2+1")]
        [TestCase("a.x=1\nb.x=2", "a.*=${b.*}+${b.*}", "x", "2+2")]
        [TestCase("a.x=1\nb.x=2", "a.*=*+${b.*}", "x", "x+2")]
        [TestCase("a.x=1\nb.x=2", "a.*=*+*", "x", "x+x")]
        [TestCase("a.x-x=1", "a.*-*=2", "x-x", "2")]
        [TestCase("a.x-x=1", "a.*-*=*", "x-x", "x")]
        [TestCase("a.x-x=1\nb.x=2", "a.*-*=${b.*}+1", "x-x", "2+1")]
        [TestCase("a.x-y=1\nb.x=2\nc.x=3\nc.y=4", "a.*-*=${b.*}+${c.*}", "x-y", "2+3")]
        public void ShouldApplySubstitute(string baseLine, string substitute, string expectedName, string expectedValue)
        {
            var tree = builder.Build(parser.Parse(baseLine).Concat(parser.Parse(substitute)),
                new QualifiedNameMatchDictionary<Scheme.SubstituteType>())
                .ToList();
            var leaf = tree
                .OfType<ProfileTreeNode>()
                .Where(node => node.NameString == "a")
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeNode>()
                .Children
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeLeaf>();

            leaf.Name
                .Tokens
                .ShouldHaveSingleItem()
                .ShouldBeOfType<TextNameToken>()
                .Text
                .ShouldBe(expectedName);
            leaf.Value.ShouldBe(expectedValue);

            // RK TODO: logger.Verify(l => l.Debug(It.IsAny<object>()));
        }

        [Test]
        [TestCase("a.x=${a.x}", "Cyclic reference: a.x [file: testfile, line: 1] > a.x [file: testfile, line: 1].")]
        [TestCase("a.x=${b.x}", "Reference b.x was not found at a.x [file: testfile, line: 1].")]
        public void ShouldConvertErrors(string profile, string expectedError) =>
            builder.Build(parser.Parse(profile),
                new QualifiedNameMatchDictionary<Scheme.SubstituteType>())
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeNode>()
                .Children
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ProfileTreeError>()
                .Error
                .ShouldBe(expectedError);

        [Test]
        public void ShouldBuildScheme()
        {
            var profile = parser.TryParse("a.output=xml");
            profile.WasSuccessful.ShouldBeTrue();
            profile.Expectations.ShouldBeEmpty();

            var tree = builder.BuildScheme(profile.Value,
                new QualifiedName[0]).ToList();

            var leaf = tree.ShouldHaveSingleItem()
                .Children
                .ShouldHaveSingleItem()
                .ShouldBeOfType<Scheme.SchemeLeaf>();

            leaf.Type.ShouldBe(Scheme.EntryType.output);
            leaf.Value.ShouldBe("xml");
        }

        [Test]
        public void ShouldApplySchemeSubstitutes()
        {
            var profile = parser.TryParse("a*.output=xml");
            profile.WasSuccessful.ShouldBeTrue();
            profile.Expectations.ShouldBeEmpty();

            var tree = builder.BuildScheme(profile.Value,
                new[] { new[] { "ab" }.ToQualifiedName() }).ToList();

            tree.ShouldHaveSingleItem()
                .Name
                .Tokens
                .ShouldHaveSingleItem()
                .ShouldBeOfType<TextNameToken>()
                .Text
                .ShouldBe("ab");
        }

        [Test]
        [TestCase("aa*.filename=*.config", "b.config")]
        [TestCase("aa*.filename=dir-*/*.config", "dir-b/b.config")]
        [TestCase("a**.filename=dir-*/*.config", "dir-a/b.config")]
        [TestCase("**b.filename=*.config", "a.config")]
        public void ShouldApplyFilenameSubstitutes(string filenamePattern, string expectedFilename)
        {
            var profile = parser.TryParse("aa*.output=xml\n" + filenamePattern);
            profile.WasSuccessful.ShouldBeTrue();
            profile.Expectations.ShouldBeEmpty();

            var tree = builder.BuildScheme(profile.Value,
                new[] { new[] { "aab" }.ToQualifiedName() }).ToList();

            var leaf = tree.ShouldHaveSingleItem()
                .Children
                .Skip(1)
                .ShouldHaveSingleItem()
                .ShouldBeOfType<Scheme.SchemeLeaf>();

            leaf.Type.ShouldBe(Scheme.EntryType.filename);
            leaf.Value.ShouldBe(expectedFilename);
        }

        [Test]
        public void ShouldDetectSchemeError()
        {
            var profile = parser.TryParse("a._invalid_=xml");
            profile.WasSuccessful.ShouldBeTrue();
            profile.Expectations.ShouldBeEmpty();

            var tree = builder.BuildScheme(profile.Value,
                new QualifiedName[0]).ToList();

            var error = tree.ShouldHaveSingleItem()
                .Children
                .OfType<Scheme.SchemeError>()
                .ShouldHaveSingleItem();

            error.Error
                .ShouldBe("Unsupported entry type: _invalid_.");

            error.SourceMark.FileName
                .ShouldBe("testfile");

            error.SourceMark.LineNumber.ShouldBe(1);
        }

        [Test]
        [TestCase("a**.filename=***.config")]
        [TestCase("a***.filename=**.config")]
        public void ShouldDetectFilenameSubstituteErrors(string filenamePattern)
        {
            var profile = parser.TryParse("aa*.output=xml\n" + filenamePattern);
            profile.WasSuccessful.ShouldBeTrue();
            profile.Expectations.ShouldBeEmpty();

            var tree = builder.BuildScheme(profile.Value,
                new[] { new[] { "aab" }.ToQualifiedName() }).ToList();

            var error = tree
                .Skip(1)
                .ShouldHaveSingleItem()
                .Children
                .ShouldHaveSingleItem()
                .ShouldBeOfType<Scheme.SchemeError>();

            error.Error.ShouldBe("Unsupported substitutes.");
            error.SourceMark.FileName.ShouldBe("testfile");
            error.SourceMark.LineNumber.ShouldBe(2);
        }
    }
}