using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Output;

/// <summary>The three Section 19 output formats that spell a path as flat key text.</summary>
public enum FlatFormat
{
    /// <summary>Section 19.1 <c>namespace</c>.</summary>
    Namespace,

    /// <summary>Section 19.2 <c>quotednamespace</c>.</summary>
    QuotedNamespace,

    /// <summary>Section 19.6 <c>ini</c>, in the <c>PortableIni1</c> dialect.</summary>
    Ini,
}

/// <summary>One flat entry with the key text its format will write.</summary>
/// <param name="Entry">The projected scalar.</param>
/// <param name="Section">
/// The Section 19.6 section name, or the empty string for a global key and for every format that
/// has no sections.
/// </param>
/// <param name="Key">The key text.</param>
public sealed record FlatKeyedEntry(FlatEntry Entry, string Section, string Key);

/// <summary>
/// Spells flat entries as keys and enforces the Section 16.4 rule that "two distinct logical paths
/// must never silently become one namespace, shell, or INI key".
/// </summary>
/// <remarks>
/// <para>
/// The three formats differ in how much a key text can carry. Namespace encoding is total and
/// injective (Section 19.1), so a collision there is reachable only when a name has no spelling at
/// all. Quoted-namespace and INI "instead apply their own validation and escaping after joining"
/// (Section 16.4) and in fact have no escape, so <c>a.b_c</c> and <c>a.b.c</c> both join to
/// <c>a_b_c</c> and collide. Detection is not a formality for those two.
/// </para>
/// <para>
/// Collision detection runs last, "after root application, delimiter joining, segment escaping, and
/// identifier normalization", because every one of those steps can create one.
/// </para>
/// </remarks>
public sealed partial class FlatKeyProjector
{
    private readonly FlatFormat format;
    private readonly string delimiter;
    private readonly DiagnosticBuffer diagnostics;
    private readonly string? destination;

    /// <summary>Creates a projector.</summary>
    /// <param name="format">The output format.</param>
    /// <param name="delimiter">The Section 16.4 delimiter joining path parts.</param>
    /// <param name="diagnostics">The buffer key faults accumulate in.</param>
    /// <param name="destination">The Section 6.4.3 <c>destination</c> this instance writes to.</param>
    public FlatKeyProjector(
        FlatFormat format,
        string delimiter,
        DiagnosticBuffer diagnostics,
        string? destination = null)
    {
        ArgumentNullException.ThrowIfNull(delimiter);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.format = format;
        this.delimiter = delimiter;
        this.diagnostics = diagnostics;
        this.destination = destination;
    }

    /// <summary>The Section 16.4 default delimiter for a format.</summary>
    /// <param name="format">The output format.</param>
    public static string DefaultDelimiter(FlatFormat format) => format switch
    {
        FlatFormat.Namespace => NamespaceEncoder.DefaultDelimiter,
        FlatFormat.QuotedNamespace => "_",
        FlatFormat.Ini => ":",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Spells every entry and rejects post-projection collisions.</summary>
    /// <param name="entries">The entries, in Section 19.1 emission order.</param>
    /// <returns>
    /// The entries that have a key, in the order they arrived. An entry whose key cannot be spelled
    /// and every entry after the first at one projected identity are dropped, having reported a
    /// blocking diagnostic that stops the invocation before publication.
    /// </returns>
    public ImmutableArray<FlatKeyedEntry> Project(IEnumerable<FlatEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var keyed = ImmutableArray.CreateBuilder<FlatKeyedEntry>();
        var taken = new Dictionary<(string Section, string Key), FlatEntry>();

        foreach (var entry in entries)
        {
            if (!TrySpell(entry, out var section, out var key))
            {
                continue;
            }

            if (taken.TryGetValue((section, key), out var first))
            {
                ReportCollision(entry, first, section, key);
                continue;
            }

            taken.Add((section, key), entry);
            keyed.Add(new FlatKeyedEntry(entry, section, key));
        }

        return keyed.ToImmutable();
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ShellIdentifier { get; }

    [GeneratedRegex(@"^[A-Za-z0-9_.:-]+$")]
    private static partial Regex IniName { get; }

    private bool TrySpell(
        FlatEntry entry,
        [NotNullWhen(true)] out string? section,
        [NotNullWhen(true)] out string? key)
    {
        section = null;
        key = null;

        if (entry.Path.IsEmpty)
        {
            ReportRootScalar(entry);
            return false;
        }

        switch (format)
        {
            case FlatFormat.Namespace:
                if (!NamespaceEncoder.TryEncodeName(
                        new QualifiedName(entry.Path), delimiter, out var encoded, out var fault))
                {
                    ReportUnspellable(entry, fault.Message);
                    return false;
                }

                section = string.Empty;
                key = encoded!;
                return true;

            case FlatFormat.QuotedNamespace:
                if (!TryPartTexts(entry, out var parts, ReportShellFault))
                {
                    return false;
                }

                var joined = string.Join(delimiter, parts);

                if (!ShellIdentifier.IsMatch(joined))
                {
                    ReportShellFault(
                        entry,
                        $"'{joined}' is not a POSIX shell identifier: Section 19.2 requires a "
                        + "quoted-namespace key to match [A-Za-z_][A-Za-z0-9_]* after applying "
                        + "root and delimiter.");
                    return false;
                }

                section = string.Empty;
                key = joined;
                return true;

            case FlatFormat.Ini:
                return TrySpellIni(entry, out section, out key);

            default:
                throw new InvalidOperationException($"unknown flat format {format}");
        }
    }

    /// <summary>
    /// Section 19.6: "a surviving scalar path with one part becomes a global key; for a path with
    /// two or more parts, the final part is the key and every preceding part is joined with the
    /// configured INI delimiter to form the section name".
    /// </summary>
    private bool TrySpellIni(
        FlatEntry entry,
        [NotNullWhen(true)] out string? section,
        [NotNullWhen(true)] out string? key)
    {
        section = null;
        key = null;

        if (!TryPartTexts(entry, out var parts, ReportIniFault))
        {
            return false;
        }

        var last = parts[^1];
        var head = string.Join(delimiter, parts.Take(parts.Length - 1));

        if (!IniName.IsMatch(last))
        {
            ReportIniFault(entry, Unsupported("key", last));
            return false;
        }

        if (head.Length > 0 && !IniName.IsMatch(head))
        {
            ReportIniFault(entry, Unsupported("section", head));
            return false;
        }

        section = head;
        key = last;
        return true;
    }

    private static string Unsupported(string role, string text) =>
        $"'{text}' is not a usable PortableIni1 {role} name: Section 19.6 requires "
        + "[A-Za-z0-9_.:-]+ after delimiter joining, which excludes '[', ']', '=', comment "
        + "markers, whitespace, and control characters.";

    /// <summary>The literal text of every part, which is all a format without escapes can join.</summary>
    /// <remarks>
    /// A typed XML component or a surviving wildcard has no literal text. Section 16.4 gives these
    /// formats "their own validation" and no escape, so such a part has no spelling here rather
    /// than a lossy one, and the format's own name diagnostic says so.
    /// </remarks>
    private static bool TryPartTexts(
        FlatEntry entry,
        out ImmutableArray<string> texts,
        Action<FlatEntry, string> report)
    {
        var builder = ImmutableArray.CreateBuilder<string>(entry.Path.Length);

        foreach (var part in entry.Path)
        {
            if (part is not OrdinaryPart ordinary || ordinary.LiteralText is not { } text)
            {
                report(
                    entry,
                    "this path contains a component that is not plain literal text, and this "
                    + "format joins parts without escaping them, so the component has no key "
                    + "spelling here.");
                texts = [];
                return false;
            }

            builder.Add(text);
        }

        texts = builder.MoveToImmutable();
        return true;
    }

    /// <summary>Reports a scalar the selected view leaves at its root with no name at all.</summary>
    /// <remarks>
    /// Each of Sections 19.1, 19.2, and 19.6 says a bare scalar "retains the final concrete
    /// selector part" as its key, so this is reachable only when neither a selector part nor
    /// <c>root</c> supplies one. It is reported in each format's own name code rather than in one
    /// shared code, because a consumer filtering on <c>SHELL001</c> is asking about shell keys.
    /// </remarks>
    private void ReportRootScalar(FlatEntry entry)
    {
        const string Message =
            "this scalar is at the root of the selected view and has no path parts, so there is "
            + "no key to write: Section 16.3 'root' or a concrete selector part must supply one.";

        switch (format)
        {
            case FlatFormat.Namespace:
                ReportUnspellable(entry, Message);
                break;

            case FlatFormat.QuotedNamespace:
                ReportShellFault(entry, Message);
                break;

            case FlatFormat.Ini:
                ReportIniFault(entry, Message);
                break;

            default:
                throw new InvalidOperationException($"unknown flat format {format}");
        }
    }

    private void ReportUnspellable(FlatEntry entry, string message) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Serialize001(
                DiagnosticPhase.Planning,
                "\u00A719.1",
                $"the path '{Named(entry)}' has no namespace spelling: {message}",
                cardinalityKey: FlatIdentity.Key(destination, null),
                destination: destination)));

    private void ReportShellFault(FlatEntry entry, string message) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Shell001(
                DiagnosticPhase.Planning,
                "\u00A719.2",
                message,
                cardinalityKey: FlatIdentity.Key(destination, Named(entry)),
                path: Named(entry),
                destination: destination)));

    private void ReportIniFault(FlatEntry entry, string message) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Ini001(
                DiagnosticPhase.Planning,
                "\u00A719.6",
                message,
                cardinalityKey: FlatIdentity.Key(destination, Named(entry)),
                path: Named(entry),
                destination: destination)));

    private void ReportCollision(FlatEntry entry, FlatEntry first, string section, string key) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Flat001(
                DiagnosticPhase.Planning,
                "\u00A716.4",
                $"'{Named(entry)}' and '{Named(first)}' are distinct logical paths that both "
                + $"project to {Spelled(section, key)}: Section 16.4 forbids two paths silently "
                + "becoming one flat key.",
                cardinalityKey: FlatIdentity.Key(destination, $"{section}\u0000{key}"),
                path: Named(entry),
                destination: destination)));

    private static string? Named(FlatEntry entry) => FlatIdentity.PathText(entry.LogicalPath);

    private static string Spelled(string section, string key) =>
        section.Length > 0 ? $"key '{key}' in section '{section}'" : $"key '{key}'";
}
