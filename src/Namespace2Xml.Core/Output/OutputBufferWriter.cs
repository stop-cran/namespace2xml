using System.Collections.Immutable;
using System.Text;
using Namespace2Xml.Budgets;

namespace Namespace2Xml.Output;

/// <summary>
/// Builds one <see cref="OutputBuffer"/>, charging <c>--max-total-output-bytes</c> before it
/// allocates.
/// </summary>
/// <remarks>
/// <para>
/// Section 23: "Accounting occurs before allocation or expansion whenever possible", and
/// "serialized output buffers ... consume the corresponding global budget". Every writer sharing one
/// <see cref="GlobalBudget"/> therefore shares one aggregate ceiling, which is what Section 21.2
/// means by "aggregate live serialized buffer payload".
/// </para>
/// <para>
/// A refused write faults the writer permanently. A serializer that ignored one return value would
/// otherwise go on to produce a buffer that is shorter than the document it was asked to serialize
/// and structurally valid anyway -- a wrong file rather than a failed run, and the failure mode
/// Section 21.2 is written to prevent.
/// </para>
/// </remarks>
public sealed class OutputBufferWriter
{
    /// <summary>
    /// The size of each allocated segment, chosen to stay below the 85,000-byte large-object
    /// threshold so that a multi-gigabyte aggregate does not fragment the large object heap.
    /// </summary>
    public const int SegmentSize = 64 * 1024;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly GlobalBudget budget;
    private readonly ImmutableArray<ReadOnlyMemory<byte>>.Builder completed =
        ImmutableArray.CreateBuilder<ReadOnlyMemory<byte>>();

    private byte[]? current;
    private int used;
    private long length;
    private bool built;

    /// <summary>Creates a writer that charges the given budget.</summary>
    /// <param name="budget">The invocation's budget, shared by every writer.</param>
    public OutputBufferWriter(GlobalBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);

        this.budget = budget;
    }

    /// <summary>The bytes written so far.</summary>
    public long Length => length;

    /// <summary>
    /// The crossing that faulted this writer, or <see langword="null"/> while it is still usable.
    /// </summary>
    public BudgetFault? Fault { get; private set; }

    /// <summary>Writes raw bytes.</summary>
    /// <param name="bytes">The bytes to append.</param>
    /// <returns><see langword="false"/> when the write would cross <c>--max-total-output-bytes</c>.</returns>
    public bool TryWrite(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(built, this);

        if (Fault is not null)
        {
            return false;
        }

        if (!budget.TryConsume(ResourceBound.MaxTotalOutputBytes, bytes.Length, out var fault))
        {
            Fault = fault;

            return false;
        }

        Append(bytes);

        return true;
    }

    /// <summary>Writes text as UTF-8 without a byte-order mark, as Section 24 requires.</summary>
    /// <param name="text">The text to append.</param>
    /// <returns><see langword="false"/> when the write would cross <c>--max-total-output-bytes</c>.</returns>
    /// <remarks>
    /// The byte count is computed before anything is encoded, so an oversized write is refused
    /// without ever allocating its encoded form.
    /// </remarks>
    public bool TryWrite(ReadOnlySpan<char> text)
    {
        ObjectDisposedException.ThrowIf(built, this);

        if (Fault is not null)
        {
            return false;
        }

        var byteCount = Utf8NoBom.GetByteCount(text);

        if (!budget.TryConsume(ResourceBound.MaxTotalOutputBytes, byteCount, out var fault))
        {
            Fault = fault;

            return false;
        }

        if (byteCount <= SegmentSize)
        {
            Span<byte> encoded = stackalloc byte[SegmentSize];

            Utf8NoBom.GetBytes(text, encoded);
            Append(encoded[..byteCount]);
        }
        else
        {
            var encoded = new byte[byteCount];

            Utf8NoBom.GetBytes(text, encoded);
            Append(encoded);
        }

        return true;
    }

    /// <summary>Writes text followed by the Section 24 line terminator.</summary>
    /// <param name="text">The text to append before the LF.</param>
    /// <returns><see langword="false"/> when the write would cross <c>--max-total-output-bytes</c>.</returns>
    /// <remarks>
    /// LF is the only terminator this tool emits. Section 24 fixes it on every platform, so there is
    /// no call anywhere that consults <see cref="Environment.NewLine"/>: on Windows that would
    /// silently produce CRLF and break byte-identical output across platforms.
    /// </remarks>
    public bool TryWriteLine(ReadOnlySpan<char> text) => TryWrite(text) && TryWrite("\n"u8);

    /// <summary>
    /// Completes the buffer, checking the Section 24 termination rule.
    /// </summary>
    /// <param name="buffer">The completed buffer, when this returns <see langword="true"/>.</param>
    /// <param name="violation">
    /// Why the buffer was rejected, when this returns <see langword="false"/>: either the writer
    /// faulted on a budget crossing, or the content does not end with exactly one LF.
    /// </param>
    /// <returns><see langword="false"/> when the buffer must not be published.</returns>
    /// <remarks>
    /// The termination rule is checked here because this is the only path from a serializer to a
    /// published file. Checking it in each serializer would mean checking it in each serializer added
    /// later, and the one that forgot would emit a file that looked entirely reasonable.
    /// </remarks>
    public bool TryBuildText(out OutputBuffer buffer, out string? violation)
    {
        if (Fault is { } fault)
        {
            buffer = OutputBuffer.Empty;
            violation = $"serialization crossed {fault.Spelling}";

            return false;
        }

        var completedBuffer = Build();

        if (!completedBuffer.IsWellTerminatedText)
        {
            buffer = OutputBuffer.Empty;
            violation = "a text output with content must end with exactly one LF";

            return false;
        }

        buffer = completedBuffer;
        violation = null;

        return true;
    }

    /// <summary>
    /// Completes the buffer without checking the Section 24 termination rule.
    /// </summary>
    /// <returns>The completed buffer.</returns>
    /// <exception cref="InvalidOperationException">The writer faulted on a budget crossing.</exception>
    /// <remarks>
    /// Every output this version produces is text, so publication uses
    /// <see cref="TryBuildText(out OutputBuffer, out string?)"/>. This exists for callers that
    /// assemble a buffer for some purpose other than a destination file.
    /// </remarks>
    public OutputBuffer Build()
    {
        ObjectDisposedException.ThrowIf(built, this);

        if (Fault is { } fault)
        {
            throw new InvalidOperationException($"serialization crossed {fault.Spelling}");
        }

        if (current is not null && used > 0)
        {
            completed.Add(current.AsMemory(0, used));
        }

        built = true;
        current = null;

        return new OutputBuffer(completed.ToImmutable(), length);
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        length += bytes.Length;

        while (!bytes.IsEmpty)
        {
            if (current is null)
            {
                current = new byte[SegmentSize];
                used = 0;
            }

            var take = Math.Min(bytes.Length, SegmentSize - used);

            bytes[..take].CopyTo(current.AsSpan(used));
            used += take;
            bytes = bytes[take..];

            if (used == SegmentSize)
            {
                completed.Add(current);
                current = null;
                used = 0;
            }
        }
    }
}
