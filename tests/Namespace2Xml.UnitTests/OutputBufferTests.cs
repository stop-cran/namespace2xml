using System.Text;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Output;
using NUnit.Framework;
using Shouldly;

namespace Namespace2Xml.UnitTests;

/// <summary>
/// Sections 21.2, 16.2 and 24 output buffers. Expectations are authored from those clauses.
/// </summary>
[TestFixture]
public sealed class OutputBufferTests
{
    private const int Segment = OutputBufferWriter.SegmentSize;

    private static OutputBufferWriter Writer(long maxTotalOutputBytes = long.MaxValue) =>
        new(new GlobalBudget(ResourceLimits.Defaults with { MaxTotalOutputBytes = maxTotalOutputBytes }));

    private static string Text(OutputBuffer buffer) =>
        Encoding.UTF8.GetString(buffer.ToArray());

    // ---- Content ---------------------------------------------------------------------------------

    [Test]
    public void AnEmptyWriterBuildsAnEmptyBuffer()
    {
        var buffer = Writer().Build();

        buffer.Length.ShouldBe(0);
        buffer.ToArray().ShouldBeEmpty();
        buffer.Segments.ShouldBeEmpty();
    }

    [Test]
    public void WrittenBytesComeBackInOrder()
    {
        var writer = Writer();

        writer.TryWrite("alpha"u8).ShouldBeTrue();
        writer.TryWrite("beta"u8).ShouldBeTrue();

        Text(writer.Build()).ShouldBe("alphabeta");
    }

    /// <summary>Section 24: UTF-8 without a byte-order mark.</summary>
    [Test]
    public void TextIsEncodedAsUtf8WithoutAByteOrderMark()
    {
        var writer = Writer();

        writer.TryWrite("Grüße 😀").ShouldBeTrue();

        var bytes = writer.Build().ToArray();

        bytes.ShouldBe(Encoding.UTF8.GetBytes("Grüße 😀"));
        bytes[0].ShouldNotBe((byte)0xEF);
    }

    /// <summary>Section 24: LF is the physical line terminator on every platform.</summary>
    [Test]
    public void ALineIsTerminatedByLfAndNeverByTheHostConvention()
    {
        var writer = Writer();

        writer.TryWriteLine("a").ShouldBeTrue();
        writer.TryWriteLine("b").ShouldBeTrue();

        Text(writer.Build()).ShouldBe("a\nb\n");
    }

    [Test]
    public void ContentSpanningManySegmentsRoundTripsExactly()
    {
        var writer = Writer();
        var chunk = new byte[7_919];

        for (var index = 0; index < chunk.Length; index++)
        {
            chunk[index] = (byte)(index % 251);
        }

        for (var repeat = 0; repeat < 40; repeat++)
        {
            writer.TryWrite(chunk).ShouldBeTrue();
        }

        var buffer = writer.Build();

        buffer.Length.ShouldBe(chunk.Length * 40L);
        buffer.ToArray().ShouldBe(Enumerable.Repeat(chunk, 40).SelectMany(bytes => bytes).ToArray());
    }

    [Test]
    public void AWriteLargerThanOneSegmentIsSplitWithoutLoss()
    {
        var writer = Writer();
        var big = new string('x', Segment * 2 + 13);

        writer.TryWrite(big).ShouldBeTrue();

        var buffer = writer.Build();

        buffer.Length.ShouldBe(big.Length);
        buffer.Segments.Length.ShouldBe(3);
        Text(buffer).ShouldBe(big);
    }

    [Test]
    public void SegmentsNeverExceedTheConfiguredSize()
    {
        var writer = Writer();

        for (var repeat = 0; repeat < 30; repeat++)
        {
            writer.TryWrite(new string('y', 5_000)).ShouldBeTrue();
        }

        writer.Build().Segments.ShouldAllBe(segment => segment.Length <= Segment);
    }

    [Test]
    public void ContentExactlyFillingSegmentsLeavesNoEmptyTail()
    {
        var writer = Writer();

        writer.TryWrite(new byte[Segment * 2]).ShouldBeTrue();

        var buffer = writer.Build();

        buffer.Segments.Length.ShouldBe(2);
        buffer.Segments.ShouldAllBe(segment => segment.Length == Segment);
    }

    [Test]
    public void WritingToAStreamProducesTheSameBytesAsMaterializing()
    {
        var writer = Writer();

        writer.TryWrite(DistinctBytes(Segment + 7)).ShouldBeTrue();

        var buffer = writer.Build();
        using var stream = new MemoryStream();

        buffer.WriteTo(stream);

        buffer.Segments.Length.ShouldBe(2);
        stream.ToArray().ShouldBe(DistinctBytes(Segment + 7));
    }

    [Test]
    public async Task WritingAsynchronouslyProducesTheSameBytes()
    {
        var writer = Writer();

        writer.TryWrite(DistinctBytes(Segment + 3)).ShouldBeTrue();

        var buffer = writer.Build();
        using var stream = new MemoryStream();

        await buffer.WriteToAsync(stream);

        buffer.Segments.Length.ShouldBe(2);
        stream.ToArray().ShouldBe(DistinctBytes(Segment + 3));
    }

    /// <summary>
    /// Section 21.2: the buffer is the destination's exact bytes, so segment order is content. A
    /// payload that repeats one byte would hide a reversal, which is how this went unnoticed until a
    /// mutation put it back the wrong way round.
    /// </summary>
    [Test]
    public void SegmentsAreConcatenatedInWriteOrder()
    {
        var writer = Writer();

        writer.TryWrite(DistinctBytes(Segment * 3 + 11)).ShouldBeTrue();

        var buffer = writer.Build();

        buffer.Segments.Length.ShouldBe(4);
        buffer.ToArray().ShouldBe(DistinctBytes(Segment * 3 + 11));

        using var stream = new MemoryStream();

        buffer.WriteTo(stream);
        stream.ToArray().ShouldBe(buffer.ToArray());
    }

    /// <summary>
    /// A payload whose every byte position differs from its neighbours across segment boundaries, so
    /// that any reordering, duplication or loss of a segment changes the result.
    /// </summary>
    private static byte[] DistinctBytes(int count)
    {
        var result = new byte[count];

        for (var index = 0; index < count; index++)
        {
            result[index] = (byte)((index * 31) + (index / 251));
        }

        return result;
    }

    // ---- Section 21.2: immutability -------------------------------------------------------------

    /// <summary>
    /// Section 21.2 requires the buffer to be immutable between serialization and publication.
    /// Continuing to write must not change a buffer already built.
    /// </summary>
    [Test]
    public void ABuiltBufferIsSealedAgainstFurtherWriting()
    {
        var writer = Writer();

        writer.TryWrite("kept"u8).ShouldBeTrue();

        var buffer = writer.Build();

        Should.Throw<ObjectDisposedException>(() => writer.TryWrite("added"u8));
        Text(buffer).ShouldBe("kept");
    }

    [Test]
    public void BuildingTwiceIsRejected()
    {
        var writer = Writer();

        writer.Build();

        Should.Throw<ObjectDisposedException>(() => writer.Build());
    }

    /// <summary>
    /// A partly-filled final segment must be copied or sliced so that later writes into the same
    /// array cannot reach a published buffer. Nothing here is pooled for the same reason.
    /// </summary>
    [Test]
    public void TwoBuffersFromTwoWritersDoNotShareStorage()
    {
        var budget = new GlobalBudget(ResourceLimits.Defaults);
        var first = new OutputBufferWriter(budget);
        var second = new OutputBufferWriter(budget);

        first.TryWrite("first"u8).ShouldBeTrue();

        var firstBuffer = first.Build();

        second.TryWrite("SECOND"u8).ShouldBeTrue();
        second.Build();

        Text(firstBuffer).ShouldBe("first");
    }

    // ---- Section 16.2: the output budget ---------------------------------------------------------

    [Test]
    public void AWriteThatWouldCrossTheOutputBoundIsRefused()
    {
        var writer = Writer(maxTotalOutputBytes: 4);

        writer.TryWrite("abcd"u8).ShouldBeTrue();
        writer.TryWrite("e"u8).ShouldBeFalse();
        writer.Fault!.Value.Bound.ShouldBe(ResourceBound.MaxTotalOutputBytes);
    }

    /// <summary>
    /// Section 16.2: "Accounting occurs before allocation or expansion whenever possible." A refused
    /// write appends nothing.
    /// </summary>
    [Test]
    public void ARefusedWriteAppendsNothing()
    {
        var writer = Writer(maxTotalOutputBytes: 4);

        writer.TryWrite("abcd"u8);
        writer.TryWrite("efgh"u8);

        writer.Length.ShouldBe(4);
    }

    /// <summary>
    /// A serializer that ignored a refusal must not be able to publish a short file, so the writer
    /// faults permanently rather than merely refusing one write.
    /// </summary>
    [Test]
    public void AFaultedWriterRefusesEveryLaterWriteEvenIfItWouldFit()
    {
        var writer = Writer(maxTotalOutputBytes: 8);

        writer.TryWrite("aaaaaaaaaaaa"u8).ShouldBeFalse();
        writer.TryWrite("b"u8).ShouldBeFalse();
        writer.Length.ShouldBe(0);
    }

    [Test]
    public void AFaultedWriterCannotBeBuilt()
    {
        var writer = Writer(maxTotalOutputBytes: 1);

        writer.TryWrite("too long"u8).ShouldBeFalse();

        Should.Throw<InvalidOperationException>(() => writer.Build());
    }

    [Test]
    public void AFaultedWriterReportsWhyItCannotBeBuilt()
    {
        var writer = Writer(maxTotalOutputBytes: 1);

        writer.TryWrite("too long"u8).ShouldBeFalse();
        writer.TryBuildText(out _, out var violation).ShouldBeFalse();

        violation.ShouldNotBeNull().ShouldContain("--max-total-output-bytes");
    }

    /// <summary>
    /// Section 21.2 makes the ceiling an aggregate over every live buffer, so writers sharing one
    /// invocation share one budget.
    /// </summary>
    [Test]
    public void TheOutputBoundIsAggregateAcrossEveryDestination()
    {
        var budget = new GlobalBudget(ResourceLimits.Defaults with { MaxTotalOutputBytes = 10 });
        var first = new OutputBufferWriter(budget);
        var second = new OutputBufferWriter(budget);

        first.TryWrite("aaaaaa"u8).ShouldBeTrue();
        second.TryWrite("bbbb"u8).ShouldBeTrue();
        second.TryWrite("c"u8).ShouldBeFalse();
    }

    /// <summary>
    /// The byte count is computed before encoding, so a multi-byte scalar is charged what it costs
    /// rather than what its UTF-16 length suggests.
    /// </summary>
    [Test]
    public void TextIsChargedItsEncodedByteLengthAndNotItsCharLength()
    {
        var writer = Writer(maxTotalOutputBytes: 3);

        writer.TryWrite("😀").ShouldBeFalse();
        writer.Length.ShouldBe(0);
    }

    [Test]
    public void TextThatExactlyFitsIsAdmitted()
    {
        var writer = Writer(maxTotalOutputBytes: 4);

        writer.TryWrite("😀").ShouldBeTrue();
        writer.Length.ShouldBe(4);
    }

    // ---- Section 24: termination ------------------------------------------------------------------

    [Test]
    public void ContentEndingWithOneLineFeedIsAccepted()
    {
        var writer = Writer();

        writer.TryWriteLine("value").ShouldBeTrue();
        writer.TryBuildText(out var buffer, out var violation).ShouldBeTrue();

        violation.ShouldBeNull();
        Text(buffer).ShouldBe("value\n");
    }

    [Test]
    public void ContentWithNoTrailingLineFeedIsRejected()
    {
        var writer = Writer();

        writer.TryWrite("value").ShouldBeTrue();
        writer.TryBuildText(out _, out var violation).ShouldBeFalse();

        violation.ShouldNotBeNull().ShouldContain("exactly one LF");
    }

    [Test]
    public void ContentWithTwoTrailingLineFeedsIsRejected()
    {
        var writer = Writer();

        writer.TryWrite("value\n\n").ShouldBeTrue();

        writer.TryBuildText(out _, out _).ShouldBeFalse();
    }

    /// <summary>Section 24: "A text output with no content is zero bytes and satisfies these rules vacuously."</summary>
    [Test]
    public void AnOutputWithNoContentIsAccepted()
    {
        Writer().TryBuildText(out var buffer, out var violation).ShouldBeTrue();

        buffer.Length.ShouldBe(0);
        violation.ShouldBeNull();
    }

    /// <summary>A lone LF is content, and content ending in exactly one LF is well terminated.</summary>
    [Test]
    public void AnOutputOfOneLineFeedIsAccepted()
    {
        var writer = Writer();

        writer.TryWrite("\n").ShouldBeTrue();

        writer.TryBuildText(out _, out _).ShouldBeTrue();
    }

    /// <summary>
    /// The termination check must look across the segment boundary, not only inside the last segment.
    /// </summary>
    [Test]
    public void TerminationIsCheckedAcrossASegmentBoundary()
    {
        var writer = Writer();

        writer.TryWrite(new string('a', Segment - 1)).ShouldBeTrue();
        writer.TryWrite("\n\n").ShouldBeTrue();

        writer.TryBuildText(out _, out _).ShouldBeFalse();
    }

    [Test]
    public void ASingleLineFeedAloneInTheLastSegmentIsAccepted()
    {
        var writer = Writer();

        writer.TryWrite(new string('a', Segment)).ShouldBeTrue();
        writer.TryWrite("\n").ShouldBeTrue();

        writer.TryBuildText(out var buffer, out _).ShouldBeTrue();
        buffer.Segments.Length.ShouldBe(2);
    }

    /// <summary>
    /// The termination rule is about the final LF. Whether a raw CR may appear in output at all is a
    /// question for the serializers, and the buffer does not pre-empt it.
    /// </summary>
    [Test]
    public void TerminationLooksOnlyAtTheFinalLineFeed()
    {
        var writer = Writer();

        writer.TryWrite("value\r\n").ShouldBeTrue();

        var buffer = writer.Build();

        buffer.IsWellTerminatedText.ShouldBeTrue();
        Text(buffer).ShouldBe("value\r\n");
    }

    [Test]
    public void AnEmptyBufferIsWellTerminated()
    {
        OutputBuffer.Empty.IsWellTerminatedText.ShouldBeTrue();
        OutputBuffer.Empty.Length.ShouldBe(0);
    }
}
