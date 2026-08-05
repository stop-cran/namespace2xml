using System.Collections.Immutable;

namespace Namespace2Xml.Profiles;

/// <summary>
/// One element of an Appendix A.2 <c>ordinary-component</c> or <c>local-component</c>: either
/// literal text or a wildcard token.
/// </summary>
/// <remarks>
/// A component is a sequence rather than a string because a wildcard is not a character. Storing
/// <c>a*b</c> as text would make the literal name <c>a\*b</c> and the pattern <c>a*b</c> the same
/// value, and Section 21 requires name encoding to be injective.
/// </remarks>
public abstract record NameToken
{
    private protected NameToken()
    {
    }
}

/// <summary>Literal text, already unescaped.</summary>
/// <param name="Text">The scalars this token contributes. Never empty.</param>
public sealed record LiteralToken(string Text) : NameToken;

/// <summary>
/// An Appendix A.2 <c>wildcard-token</c>: <c>*</c>, or <c>*[identifier]</c> for an explicit capture.
/// </summary>
/// <param name="CaptureId">
/// The explicit capture identifier, or <see langword="null"/> for the legacy bare form.
/// </param>
public sealed record WildcardToken(string? CaptureId) : NameToken;

/// <summary>
/// One Appendix A.2 <c>component</c> of a qualified name.
/// </summary>
public abstract record NamePart
{
    private protected NamePart()
    {
    }
}

/// <summary>
/// An Appendix A.2 <c>xml-name-component</c>: the part an attribute marker may qualify, and the
/// two forms an element name can take.
/// </summary>
public abstract record XmlNameComponent : NamePart
{
    private protected XmlNameComponent()
    {
    }
}

/// <summary>An Appendix A.2 <c>ordinary-component</c>.</summary>
public sealed record OrdinaryPart : XmlNameComponent
{
    /// <summary>Creates an ordinary component from its tokens.</summary>
    public OrdinaryPart(ImmutableArray<NameToken> tokens) => Tokens = tokens;

    /// <summary>The component's tokens, in source order. Never empty.</summary>
    public ImmutableArray<NameToken> Tokens { get; }

    /// <summary>The literal text when the component contains no wildcard, otherwise null.</summary>
    public string? LiteralText =>
        Tokens.Length == 1 && Tokens[0] is LiteralToken literal ? literal.Text : null;

    /// <inheritdoc/>
    public bool Equals(OrdinaryPart? other) => TokenSequence.Equal(Tokens, other?.Tokens);

    /// <inheritdoc/>
    public override int GetHashCode() => TokenSequence.HashCode(Tokens);
}

/// <summary>An Appendix A.2 <c>qualified-element</c>: <c>Q{uri}local</c>.</summary>
public sealed record QualifiedElementPart : XmlNameComponent
{
    /// <summary>Creates a qualified element from its URI and local tokens.</summary>
    public QualifiedElementPart(string uri, ImmutableArray<NameToken> local)
    {
        Uri = uri;
        Local = local;
    }

    /// <summary>The URI body, already unescaped. May be empty: <c>Q{}local</c> is Section 11.4's
    /// explicit spelling for an unqualified element.</summary>
    public string Uri { get; }

    /// <summary>The local name's tokens, in source order. Never empty.</summary>
    public ImmutableArray<NameToken> Local { get; }

    /// <inheritdoc/>
    public bool Equals(QualifiedElementPart? other) =>
        other is not null
        && string.Equals(Uri, other.Uri, StringComparison.Ordinal)
        && TokenSequence.Equal(Local, other.Local);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Uri, TokenSequence.HashCode(Local));
}

/// <summary>An Appendix A.2 <c>typed-attribute</c>: <c>@name</c> or <c>@Q{uri}name</c>.</summary>
/// <param name="Name">The attribute's name component.</param>
public sealed record AttributePart(XmlNameComponent Name) : NamePart;

/// <summary>
/// An Appendix A.2 <c>typed-content</c>: <c>#n</c>, the Section 11.4 content-token ordering value.
/// </summary>
/// <param name="Ordinal">The ordering value. Never negative, and never written with a leading zero.</param>
public sealed record ContentPart(int Ordinal) : NamePart;

/// <summary>Structural comparison for token sequences.</summary>
/// <remarks>
/// <see cref="ImmutableArray{T}"/> compares by reference, so a record holding one compares two
/// identically-populated instances unequal. Qualified names are dictionary keys throughout the
/// overlay, where that would silently produce a second entry for a path that already exists.
/// </remarks>
internal static class TokenSequence
{
    internal static bool Equal(ImmutableArray<NameToken> left, ImmutableArray<NameToken>? right) =>
        right is { } other && left.SequenceEqual(other);

    internal static int HashCode(ImmutableArray<NameToken> tokens)
    {
        var hash = default(HashCode);
        foreach (var token in tokens)
        {
            hash.Add(token);
        }

        return hash.ToHashCode();
    }
}
