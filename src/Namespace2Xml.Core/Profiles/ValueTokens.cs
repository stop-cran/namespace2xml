using System.Collections.Immutable;

namespace Namespace2Xml.Profiles;

/// <summary>
/// One element of an interpreted value: literal text, a wildcard substitution, or a reference.
/// </summary>
/// <remarks>
/// A value is a sequence rather than a string for the reason Appendix A.3 gives: escapes, wildcard
/// tokens, and references are recognized in one left-to-right pass and emitted text is never
/// rescanned, so the difference between the <c>${</c> that begins a reference and the <c>${</c> that
/// <c>\${</c> emits has to survive lexing.
/// </remarks>
public abstract record ValueToken
{
    private protected ValueToken()
    {
    }
}

/// <summary>Literal text, already unescaped.</summary>
/// <param name="Text">The scalars this token contributes. Never empty.</param>
public sealed record LiteralValueToken(string Text) : ValueToken;

/// <summary>
/// A Section 12.1 legacy capture substitution, or a Section 12.2 explicit one.
/// </summary>
/// <param name="CaptureId">
/// The explicit capture identifier, or <see langword="null"/> for the legacy bare form.
/// </param>
public sealed record ValueWildcardToken(string? CaptureId) : ValueToken;

/// <summary>A Section 8.4 reference.</summary>
/// <param name="Name">The referenced name.</param>
public sealed record ReferenceToken(QualifiedName Name) : ValueToken;

/// <summary>
/// Text a resolved reference contributed, kept distinct from text the author wrote.
/// </summary>
/// <param name="Text">The referent's resolved text. Never empty.</param>
/// <remarks>
/// Section 15.1 step 1 resolves references among scheme entries, and Section 16.2 says what the
/// result is: "Scheme references are resolved before capture substitution, but their resulting text
/// is opaque segment data: <c>/</c> or <c>\</c> supplied by a reference is encoded and never creates
/// a directory." Splicing the referent's text in as an ordinary literal would lose exactly that
/// distinction, and a <c>root</c> holding <c>a/b</c> would silently start writing into a
/// subdirectory — the traversal the same clause forbids a capture. The token therefore survives
/// resolution so that the one consumer to which the difference matters can still see it; every
/// other reader takes the text through <see cref="InterpretedValue.LiteralText"/>, because for
/// every other directive it is simply settled text.
/// </remarks>
public sealed record ResolvedReferenceToken(string Text) : ValueToken;

/// <summary>
/// An interpreted value: the token sequence Appendix A.3 produces.
/// </summary>
public sealed record InterpretedValue
{
    /// <summary>Creates a value from its tokens.</summary>
    /// <param name="tokens">The tokens, in source order. May be empty: a value may be empty.</param>
    /// <remarks>
    /// Adjacent literals are merged and empty ones dropped, for the reason the name tokens are: the
    /// list exists only to keep wildcards and references distinct from text, so equality must not
    /// depend on how the value was built.
    /// </remarks>
    public InterpretedValue(ImmutableArray<ValueToken> tokens) => Tokens = Canonical(tokens);

    /// <summary>The tokens, in source order.</summary>
    public ImmutableArray<ValueToken> Tokens { get; }

    /// <summary>
    /// The value's text when it contains no wildcard and no unresolved reference, otherwise
    /// <see langword="null"/>. An empty value has empty text.
    /// </summary>
    /// <remarks>
    /// A <see cref="ResolvedReferenceToken"/> is settled text and folds in here, because the one
    /// consumer that must not treat it as written text asks for the tokens instead.
    /// </remarks>
    public string? LiteralText => Tokens switch
    {
        [] => string.Empty,
        [LiteralValueToken literal] => literal.Text,
        [ResolvedReferenceToken resolved] => resolved.Text,
        _ => Tokens.All(token => token is LiteralValueToken or ResolvedReferenceToken)
            ? string.Concat(Tokens.Select(token => token switch
            {
                LiteralValueToken literal => literal.Text,
                ResolvedReferenceToken resolved => resolved.Text,
                _ => string.Empty,
            }))
            : null,
    };

    /// <summary>Whether the value contains a reference.</summary>
    public bool ContainsReference => Tokens.OfType<ReferenceToken>().Any();

    /// <summary>Whether the value contains a wildcard substitution.</summary>
    public bool ContainsWildcard => Tokens.OfType<ValueWildcardToken>().Any();

    /// <inheritdoc/>
    public bool Equals(InterpretedValue? other) =>
        other is not null && Tokens.SequenceEqual(other.Tokens);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var token in Tokens)
        {
            hash.Add(token);
        }

        return hash.ToHashCode();
    }

    private static ImmutableArray<ValueToken> Canonical(ImmutableArray<ValueToken> tokens)
    {
        if (tokens.IsDefaultOrEmpty)
        {
            return [];
        }

        var needsWork = false;

        for (var i = 0; i < tokens.Length && !needsWork; i++)
        {
            needsWork = tokens[i] is LiteralValueToken { Text.Length: 0 }
                || (i > 0 && tokens[i] is LiteralValueToken && tokens[i - 1] is LiteralValueToken);
        }

        if (!needsWork)
        {
            return tokens;
        }

        var result = ImmutableArray.CreateBuilder<ValueToken>(tokens.Length);

        foreach (var token in tokens)
        {
            if (token is not LiteralValueToken literal)
            {
                result.Add(token);
                continue;
            }

            if (literal.Text.Length == 0)
            {
                continue;
            }

            if (result.Count > 0 && result[^1] is LiteralValueToken previous)
            {
                result[^1] = new LiteralValueToken(previous.Text + literal.Text);
            }
            else
            {
                result.Add(literal);
            }
        }

        return result.ToImmutable();
    }
}
