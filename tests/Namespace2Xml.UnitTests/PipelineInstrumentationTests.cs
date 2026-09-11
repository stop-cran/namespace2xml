using System.Collections.Immutable;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

[TestFixture]
public sealed class PipelineInstrumentationTests
{
    [Test]
    public void DisabledInstrumentationDoesNotConstructPathPayloads()
    {
        var pattern = QualifiedNameLexer.Lex("a.b").Name.ShouldNotBeNull();
        var mask = ExclusionMask.Of([pattern]);
        var path = ImmutableArray.Create<NamePart>(
            new OrdinaryPart([new LiteralToken("a")]),
            new OrdinaryPart([new LiteralToken("b")]));

        PipelineInstrumentation.IsEnabled.ShouldBeFalse();

        Warm(mask, pattern, path);

        var directAllocations = MeasureDirect(pattern, path);
        var maskedAllocations = MeasureMask(mask, path);

        maskedAllocations.ShouldBe(
            directAllocations,
            "the disabled observer branch must not construct canonical path payloads");
    }

    private static void Warm(
        ExclusionMask mask,
        QualifiedName pattern,
        ImmutableArray<NamePart> path)
    {
        for (var iteration = 0; iteration < 10_000; iteration++)
        {
            _ = mask.Suppresses(path);
            _ = WildcardMatch.TryMatchPrefix(
                pattern.Parts,
                pattern.Parts.Length,
                path,
                out _);
        }
    }

    private static long MeasureMask(
        ExclusionMask mask,
        ImmutableArray<NamePart> path)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            _ = mask.Suppresses(path);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static long MeasureDirect(
        QualifiedName pattern,
        ImmutableArray<NamePart> path)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            _ = WildcardMatch.TryMatchPrefix(
                pattern.Parts,
                pattern.Parts.Length,
                path,
                out _);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
