using System.Collections.Immutable;
using System.Text;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;

namespace Namespace2Xml.Pipeline;

/// <summary>
/// A diagnostic occurrence together with the keys specification Section 24 orders it by.
/// </summary>
/// <remarks>
/// Neither key is a member of the Section 6.4.3 element, which is closed. They are emission-side
/// bookkeeping in the same sense as <see cref="DiagnosticOccurrence.CardinalityKey"/>: what the
/// stream shows is the resulting order, not the reason for it.
/// </remarks>
/// <param name="Occurrence">The diagnostic and the key it is emitted once per.</param>
/// <param name="OrderingKey">
/// The Section 4.7 stable ordering key of the item this diagnostic concerns. A diagnostic that
/// concerns a source but no individual item carries the key with only the CLI source ordinal set,
/// per Section 24, which places it before every item of that source.
/// </param>
/// <param name="DestinationOrder">
/// The Section 21.3 destination index, for a diagnostic that concerns a destination but no source
/// item. Ignored when <paramref name="OrderingKey"/> is present.
/// </param>
public sealed record BufferedDiagnostic(
    DiagnosticOccurrence Occurrence,
    StableOrderingKey? OrderingKey = null,
    int? DestinationOrder = null)
{
    /// <summary>The diagnostic being ordered.</summary>
    public Diagnostic Diagnostic => Occurrence.Diagnostic;

    /// <summary>
    /// The Section 24 group: 0 carries a source ordering key, 1 carries a destination only, 2
    /// carries neither.
    /// </summary>
    internal int Group => OrderingKey is not null ? 0 : DestinationOrder is not null ? 1 : 2;
}

/// <summary>
/// Accumulates the diagnostics of one unit of work and drains them in the total order specification
/// Section 24 fixes.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not thread-safe, and deliberately mergeable instead. Section 24 permits concurrent
/// work and requires the resulting stream to be identical anyway, so a shared buffer behind a lock
/// would satisfy memory safety and still be wrong: for a code the registry scopes to the whole
/// invocation, the occurrence that survives would be whichever worker reached the lock first. Give
/// each unit of work its own buffer and <see cref="Merge"/> them, and the surviving occurrence is
/// the one that sorts earliest, which is a property of the data.
/// </para>
/// <para>
/// The buffer enforces cardinality on insertion because nothing downstream can. A stream carrying
/// <c>REFERENCE002</c> once per reference rather than once per reachable owning value is valid
/// against the Section 6.4.3 schema, passes every field comparison, and is still the wrong stream.
/// </para>
/// </remarks>
public sealed class DiagnosticBuffer
{
    private readonly Dictionary<(string Code, string CardinalityKey), BufferedDiagnostic> entries = [];

    /// <summary>Whether any buffered diagnostic is a blocking error, per Section 15.4.</summary>
    public bool HasBlockingError { get; private set; }

    /// <summary>The number of buffered occurrences.</summary>
    public int Count => entries.Count;

    /// <summary>
    /// Buffers a diagnostic unless its cardinality slot is already filled by an occurrence that
    /// sorts earlier.
    /// </summary>
    /// <param name="entry">The occurrence and its ordering keys.</param>
    /// <returns>
    /// <c>true</c> when the buffer now holds <paramref name="entry"/>, <c>false</c> when the slot
    /// was already held by an occurrence Section 24 orders earlier.
    /// </returns>
    public bool Add(BufferedDiagnostic entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var slot = (entry.Diagnostic.Code, entry.Occurrence.CardinalityKey);
        if (entries.TryGetValue(slot, out var held) && Compare(held, entry) <= 0)
        {
            return false;
        }

        entries[slot] = entry;
        HasBlockingError = HasBlockingError || entry.Diagnostic.Severity == DiagnosticSeverity.Error;
        return true;
    }

    /// <summary>Folds another buffer into this one, resolving cardinality collisions by order.</summary>
    /// <param name="other">The buffer to fold in. Left unmodified.</param>
    public void Merge(DiagnosticBuffer other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var entry in other.entries.Values)
        {
            Add(entry);
        }
    }

    /// <summary>Returns every buffered diagnostic in Section 24 emission order.</summary>
    /// <returns>The ordered diagnostics.</returns>
    public ImmutableArray<Diagnostic> Drain() =>
        [.. entries.Values.Order(BufferedDiagnosticOrder.Instance).Select(entry => entry.Diagnostic)];

    private static int Compare(BufferedDiagnostic left, BufferedDiagnostic right) =>
        BufferedDiagnosticOrder.Instance.Compare(left, right);
}

/// <summary>The total order specification Section 24 fixes over the diagnostic stream.</summary>
/// <remarks>
/// Section 24 orders by phase, then by source ordering key, then by destination for diagnostics
/// that carry no ordering key, then by code and qualified path. Section 22's cardinalities leave no
/// further tie to break, so this comparer is total over any stream a conforming run can produce.
/// </remarks>
public sealed class BufferedDiagnosticOrder : IComparer<BufferedDiagnostic>
{
    /// <summary>The single instance.</summary>
    public static BufferedDiagnosticOrder Instance { get; } = new();

    private BufferedDiagnosticOrder()
    {
    }

    /// <summary>Compares two buffered diagnostics under Section 24.</summary>
    /// <param name="x">The first occurrence.</param>
    /// <param name="y">The second occurrence.</param>
    /// <returns>Negative when <paramref name="x"/> is emitted first, positive when second, zero when neither precedes.</returns>
    public int Compare(BufferedDiagnostic? x, BufferedDiagnostic? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var byPhase = ((int)x.Diagnostic.Phase).CompareTo((int)y.Diagnostic.Phase);
        if (byPhase != 0)
        {
            return byPhase;
        }

        var byGroup = x.Group.CompareTo(y.Group);
        if (byGroup != 0)
        {
            return byGroup;
        }

        if (x.OrderingKey is { } left && y.OrderingKey is { } right)
        {
            var byKey = left.CompareTo(right);
            if (byKey != 0)
            {
                return byKey;
            }
        }
        else if (x.DestinationOrder is { } leftOrder && y.DestinationOrder is { } rightOrder)
        {
            var byDestination = leftOrder.CompareTo(rightOrder);
            if (byDestination != 0)
            {
                return byDestination;
            }
        }

        var byCode = Utf8Order.Compare(x.Diagnostic.Code, y.Diagnostic.Code);
        return byCode != 0 ? byCode : Utf8Order.Compare(x.Diagnostic.Path, y.Diagnostic.Path);
    }
}
