using Namespace2Xml.Diagnostics;

namespace Namespace2Xml.Text;

/// <summary>
/// The outcome of decoding one source under specification Section 7.4: either the decoded text and
/// the encoding that produced it, or the single <c>PARSE002</c> the source earned.
/// </summary>
/// <remarks>
/// Section 22 scopes <c>PARSE002</c> to once per failing source, so this carries one diagnostic
/// rather than a collection. Decoding stops at the first position it cannot produce; there is no
/// resynchronization, because a decoder that guessed where to resume would report a second and
/// later fault whose position depended on that guess.
/// </remarks>
public sealed class DecodedSource
{
    internal DecodedSource(string text, SourceEncoding encoding)
    {
        Text = text;
        Encoding = encoding;
    }

    internal DecodedSource(DiagnosticOccurrence diagnostic) => Diagnostic = diagnostic;

    /// <summary>The decoded character stream, or <see langword="null"/> when decoding failed.</summary>
    /// <remarks>Any byte-order mark has been removed; see Section 7.4.</remarks>
    public string? Text { get; }

    /// <summary>The encoding used, or <see langword="null"/> when decoding failed.</summary>
    public SourceEncoding? Encoding { get; }

    /// <summary>The <c>PARSE002</c> occurrence, or <see langword="null"/> when decoding succeeded.</summary>
    public DiagnosticOccurrence? Diagnostic { get; }

    /// <summary>Whether the source decoded.</summary>
    public bool Succeeded => Text is not null;
}
