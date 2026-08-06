using System.Collections.Immutable;
using Namespace2Xml.Text;

namespace Namespace2Xml.Profiles;

/// <summary>
/// Classifies physical records under Section 8.1, in the order that section fixes.
/// </summary>
/// <remarks>
/// Classification is total: every record receives a kind, and a record that satisfies no rule is
/// <see cref="NamespaceRecordKind.Malformed"/> rather than skipped. This type reports the kind and
/// does not emit diagnostics, because a record does not know which source it came from.
/// </remarks>
public static class NamespaceRecordClassifier
{
    /// <summary>Classifies every record of a document.</summary>
    public static ImmutableArray<NamespaceRecord> Classify(ImmutableArray<PhysicalRecord> records) =>
        [.. records.Select(record => Classify(record.Text, record.Line))];

    /// <summary>Classifies one record.</summary>
    /// <param name="text">The record's scalars, without its terminator.</param>
    /// <param name="line">One-based line number under Section 22.</param>
    public static NamespaceRecord Classify(string text, int line)
    {
        ArgumentNullException.ThrowIfNull(text);

        var first = FirstNonBlank(text);

        if (first < 0)
        {
            return NamespaceRecord.Ignored(line);
        }

        // Rules 2 and 3 test the first non-space/tab scalar itself, which is why a record beginning
        // "\#" is not a comment: its first such scalar is the backslash. No separate check is
        // needed for the escaped forms Section 8.1 calls out.
        if (text[first] == '#')
        {
            return NamespaceRecord.OfComment(text[first..], line, first + 1);
        }

        if (text[first] == '!')
        {
            return NamespaceRecord.OfMask(text[(first + 1)..], line, first + 2);
        }

        var separator = FindSeparatingEquals(text);

        return separator < 0
            ? NamespaceRecord.Malformed(line)
            : NamespaceRecord.OfEntry(text[..separator], text[(separator + 1)..], line);
    }

    /// <summary>
    /// Finds the Section 8.1 separating <c>=</c>: the first unescaped <c>=</c> outside a
    /// <c>Q{...}</c> URI, or <c>-1</c> when the record has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This walks the Appendix A.2 <c>qname</c> productions far enough to know where it is, rather
    /// than searching for a character. Section 11.4 makes <c>Q{...}</c> one atomic lexer context
    /// whose only escapes are <c>\}</c> and <c>\\</c>, so an <c>=</c> inside a namespace URI cannot
    /// be written any other way and a scan that ignored URI context would split
    /// <c>Q{urn:x?v=1}b=value</c> in the middle of its URI. That record is one the tool itself emits
    /// under Section 16.4, so the naive scan makes its own output unreadable.
    /// </para>
    /// <para>
    /// Only the structure that can hide an <c>=</c> is tracked. Whether an escape is one Section 8.2
    /// permits, whether a URI is terminated, and whether a component is empty are all decided by the
    /// name lexer on the name this returns; deciding them here would report a name error before
    /// knowing where the name ends.
    /// </para>
    /// </remarks>
    public static int FindSeparatingEquals(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var index = 0;
        var atComponentStart = true;

        while (index < text.Length)
        {
            var c = text[index];

            if (c == '\\')
            {
                // Every name escape consumes the backslash and one following scalar, so "\=" is not
                // a separator and "\\=" is: the backslash pair is consumed first.
                index = NextScalar(text, NextScalar(text, index));
                atComponentStart = false;
                continue;
            }

            if (atComponentStart && c == '@')
            {
                // "@Q{urn:p}x" is one component, so an attribute marker leaves the URI marker
                // position open rather than closing it.
                index++;
                continue;
            }

            if (atComponentStart && c == 'Q' && index + 1 < text.Length && text[index + 1] == '{')
            {
                index = SkipUri(text, index + 2);
                atComponentStart = false;
                continue;
            }

            if (c == '=')
            {
                return index;
            }

            atComponentStart = c == '.';
            index = NextScalar(text, index);
        }

        return -1;
    }

    /// <summary>
    /// Skips a <c>Q{...}</c> URI body, returning the index after its terminating <c>}</c>, or the
    /// end of the text when it has none.
    /// </summary>
    private static int SkipUri(string text, int index)
    {
        while (index < text.Length)
        {
            if (text[index] == '\\')
            {
                index = NextScalar(text, NextScalar(text, index));
                continue;
            }

            if (text[index] == '}')
            {
                return index + 1;
            }

            index = NextScalar(text, index);
        }

        return index;
    }

    /// <summary>Advances one Unicode scalar, so a surrogate pair is consumed as one.</summary>
    private static int NextScalar(string text, int index)
    {
        if (index >= text.Length)
        {
            return text.Length;
        }

        return index + (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            ? 2
            : 1);
    }

    /// <summary>Finds the first scalar that is neither a space nor a tab, or <c>-1</c>.</summary>
    private static int FirstNonBlank(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not (' ' or '\t'))
            {
                return i;
            }
        }

        return -1;
    }
}
