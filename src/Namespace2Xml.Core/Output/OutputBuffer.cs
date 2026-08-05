using System.Collections.Immutable;

namespace Namespace2Xml.Output;

/// <summary>
/// A complete serialized destination, held as an immutable sequence of byte segments.
/// </summary>
/// <remarks>
/// <para>
/// Section 21.2 requires every planned output to be serialized completely into an immutable
/// in-memory byte buffer before any destination is opened, and Section 21.2 also forbids streaming
/// or spilling after the validation gate, "because that would change the specified failure model".
/// This type is that buffer.
/// </para>
/// <para>
/// It is segmented rather than contiguous because Section 16.2 makes
/// <c>--max-total-output-bytes</c> "the upper bound on aggregate live serialized buffer payload".
/// A growable contiguous buffer doubles: at the 4 GiB default it would need 8 GiB live to cross 4,
/// and the copy would exceed the very bound it is trying to respect. Segments never copy and never
/// need a contiguous 4 GiB allocation to exist.
/// </para>
/// <para>
/// Segments are <see cref="ReadOnlyMemory{T}"/> over arrays no other object retains, so the buffer
/// is immutable in fact and not merely by convention. Nothing here is pooled: a pooled array
/// returned while a buffer still referenced it would mutate a published file's contents, and the
/// window between serialization and publication is exactly where that would go unnoticed.
/// </para>
/// </remarks>
public sealed class OutputBuffer
{
    private readonly ImmutableArray<ReadOnlyMemory<byte>> segments;

    internal OutputBuffer(ImmutableArray<ReadOnlyMemory<byte>> segments, long length)
    {
        this.segments = segments;
        Length = length;
    }

    /// <summary>An output with no content, which Section 24 admits as zero bytes.</summary>
    public static OutputBuffer Empty { get; } = new([], 0);

    /// <summary>The total number of bytes.</summary>
    public long Length { get; }

    /// <summary>The segments, in order. Concatenating them yields the destination's exact bytes.</summary>
    public ImmutableArray<ReadOnlyMemory<byte>> Segments => segments;

    /// <summary>
    /// Whether this buffer satisfies the Section 24 termination rule: content ends with exactly one
    /// LF, and an output with no content is admissible.
    /// </summary>
    public bool IsWellTerminatedText
    {
        get
        {
            if (Length == 0)
            {
                return true;
            }

            var tail = LastBytes(2);

            return tail[^1] == (byte)'\n' && (tail.Length == 1 || tail[0] != (byte)'\n');
        }
    }

    /// <summary>Writes the whole buffer to a stream in segment order.</summary>
    /// <param name="destination">The stream to write to.</param>
    public void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        foreach (var segment in segments)
        {
            destination.Write(segment.Span);
        }
    }

    /// <summary>Writes the whole buffer to a stream in segment order.</summary>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when every segment has been written.</returns>
    public async ValueTask WriteToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        foreach (var segment in segments)
        {
            await destination.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Copies the whole buffer into one array.</summary>
    /// <returns>The destination's exact bytes.</returns>
    /// <exception cref="InvalidOperationException">
    /// The buffer is longer than the largest array the runtime can hold. Publication uses
    /// <see cref="WriteTo(Stream)"/> and never needs this; it exists for tests and for callers that
    /// have already bounded the length.
    /// </exception>
    public byte[] ToArray()
    {
        if (Length > Array.MaxLength)
        {
            throw new InvalidOperationException(
                $"an output of {Length} bytes cannot be materialized as one array; write it to a stream instead");
        }

        var result = new byte[Length];
        var offset = 0;

        foreach (var segment in segments)
        {
            segment.Span.CopyTo(result.AsSpan(offset));
            offset += segment.Length;
        }

        return result;
    }

    private byte[] LastBytes(int count)
    {
        var wanted = (int)Math.Min(count, Length);
        var result = new byte[wanted];
        var remaining = wanted;

        for (var index = segments.Length - 1; index >= 0 && remaining > 0; index--)
        {
            var segment = segments[index].Span;
            var take = Math.Min(remaining, segment.Length);

            segment[^take..].CopyTo(result.AsSpan(remaining - take));
            remaining -= take;
        }

        return result;
    }
}
