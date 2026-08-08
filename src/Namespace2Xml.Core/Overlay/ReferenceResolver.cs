using System.Collections.Immutable;
using System.Text;
using Namespace2Xml.Budgets;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Overlay;

/// <summary>
/// Section 13: resolving references in the entries an output instance can reach.
/// </summary>
/// <remarks>
/// <para>
/// Section 13.1 puts this after wildcard generation and ordinary merging, so what is resolved is
/// the finished model rather than any one source's contributions. Section 14.4 then bounds it:
/// "Missing, cyclic, ambiguous, free-wildcard, and non-scalar references in entries unreachable
/// from every concrete output instance do not fail the run", and "Selected entries and their
/// transitive reference closure are resolved strictly".
/// </para>
/// <para>
/// Both halves of that sentence come from one traversal. Reachability is computed by following
/// references outward from the selected subtrees, and a reference is only ever resolved because
/// something reachable asked for it — so an entry nothing selects is never visited, and its defects
/// are never reported. Scanning the model for unresolved payloads and then filtering the
/// diagnostics would produce the same output on correct input and a different exit code on
/// incorrect input.
/// </para>
/// </remarks>
public sealed class ReferenceResolver
{
    private readonly OverlayNode model;
    private readonly DiagnosticBuffer diagnostics;
    private readonly GlobalBudget budget;
    private readonly Dictionary<string, ImmutableArray<ImmutableArray<NamePart>>> aliases;
    private readonly Dictionary<string, ScalarPayload?> resolved = new(StringComparer.Ordinal);
    private readonly HashSet<string> reportedCycles = new(StringComparer.Ordinal);

    private ReferenceResolver(
        OverlayNode model, DiagnosticBuffer diagnostics, GlobalBudget budget)
    {
        this.model = model;
        this.diagnostics = diagnostics;
        this.budget = budget;
        aliases = BuildAliasIndex(model);
    }

    /// <summary>
    /// Resolves every reference reachable from the given roots, returning the rewritten model.
    /// </summary>
    /// <param name="model">The merged model, after step 12.</param>
    /// <param name="roots">The selected paths of the concrete output instances.</param>
    /// <param name="budget">The invocation's Section 23 budgets.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The model with every reachable unresolved payload replaced.</returns>
    public static OverlayNode Resolve(
        OverlayNode model,
        IEnumerable<ImmutableArray<NamePart>> roots,
        GlobalBudget budget,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var resolver = new ReferenceResolver(model, diagnostics, budget);

        foreach (var root in roots)
        {
            resolver.Reach(root);
        }

        return resolver.Rewrite(model, []);
    }

    /// <summary>
    /// Section 13.2 canonical interpolation text: what a referenced payload contributes to a
    /// concatenation.
    /// </summary>
    /// <param name="payload">The referenced payload.</param>
    /// <returns>The interpolation text.</returns>
    /// <remarks>
    /// Null interpolates as the four letters <c>null</c>. That is Section 13.2's own list and not
    /// <see cref="ScalarPayload.ToCanonicalText"/>, which refuses null on purpose because Section
    /// 19 lets each output format spell it differently. Interpolation is not an output format:
    /// the result is text inside a larger string, and by then no format has a say in it.
    /// </remarks>
    public static string Interpolate(ScalarPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return payload.IsNull ? "null" : payload.ToCanonicalText();
    }

    private static Dictionary<string, ImmutableArray<ImmutableArray<NamePart>>> BuildAliasIndex(
        OverlayNode model)
    {
        var found = new Dictionary<string, List<ImmutableArray<NamePart>>>(StringComparer.Ordinal);

        Collect(model, [], found);

        return found.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToImmutableArray(),
            StringComparer.Ordinal);
    }

    private static void Collect(
        OverlayNode node,
        ImmutableArray<NamePart> path,
        Dictionary<string, List<ImmutableArray<NamePart>>> found)
    {
        if (node.Payload is not null && !path.IsEmpty && SimpleAlias(path) is { } alias)
        {
            if (!found.TryGetValue(alias, out var canonical))
            {
                canonical = [];
                found[alias] = canonical;
            }

            canonical.Add(path);
        }

        foreach (var (name, child) in OverlayAddressing.Addresses(node))
        {
            Collect(child, path.Add(name), found);
        }
    }

    /// <summary>
    /// The Section 13.1 simple alias of a canonical path, or null when the path has none.
    /// </summary>
    /// <remarks>
    /// Section 13.1 lists the rewrites: an XML alias "replaces every <c>Q{uri}local</c> or
    /// <c>@Q{uri}local</c> part with <c>local</c>, replaces every <c>@local</c> part with
    /// <c>local</c>, removes a <c>#n</c> wrapper before a child element, and removes a terminal
    /// text/CDATA <c>#n</c> so that the scalar aliases to its owning element path". A comment's
    /// content-token path has no alias at all: "XML comment content-token paths never enter the
    /// simple alias index".
    /// </remarks>
    private static string? SimpleAlias(ImmutableArray<NamePart> path)
    {
        var parts = ImmutableArray.CreateBuilder<NamePart>(path.Length);

        for (var i = 0; i < path.Length; i++)
        {
            switch (path[i])
            {
                case ContentPart when i == path.Length - 1:
                    // A terminal content token is the owning element's scalar, so it aliases to the
                    // element. Section 11.4 calls the two "two canonical addresses for one scalar
                    // identity, not two candidates in the simple-alias ambiguity index", which is
                    // why dropping the part here cannot make the element ambiguous with itself.
                    break;

                case ContentPart:
                    // A '#n' wrapper before a child element is removed outright.
                    break;

                case AttributePart attribute:
                    parts.Add(Unqualified(attribute.Name));
                    break;

                case XmlNameComponent component:
                    parts.Add(Unqualified(component));
                    break;

                default:
                    parts.Add(path[i]);
                    break;
            }
        }

        return CanonicalPath.Of(parts.ToImmutable());
    }

    private static XmlNameComponent Unqualified(XmlNameComponent component) => component switch
    {
        QualifiedElementPart qualified => new OrdinaryPart(qualified.Local),
        _ => component,
    };

    /// <summary>
    /// Whether a reference is canonical, which Section 13.1 decides by the presence of a typed
    /// address component.
    /// </summary>
    /// <remarks>
    /// An <see cref="OrdinaryPart"/> written <c>Q{}local</c> counts. Section 11.4 gives that
    /// spelling exactly this job — "a marked component bypasses that index and names one canonical
    /// component outright" — and Section 13.1's worked example ends "<c>${a.Q{}x}</c> selects the
    /// child element", which is only true if the marker reaches this predicate. It is the sole
    /// in-band escape from the ambiguity the index can raise, so an implementation that drops it
    /// creates a blocking error with no expressible answer.
    /// </remarks>
    private static bool IsCanonical(QualifiedName name) =>
        name.Parts.Any(part =>
            part is AttributePart
                or ContentPart
                or QualifiedElementPart
                or OrdinaryPart { IsExplicitlyCanonical: true });

    private void Reach(ImmutableArray<NamePart> root)
    {
        var node = Descend(root);

        if (node is null)
        {
            return;
        }

        Walk(node, root);
    }

    private void Walk(OverlayNode node, ImmutableArray<NamePart> path)
    {
        if (node.Payload is { IsUnresolved: true })
        {
            ResolveAt(path, []);
        }

        foreach (var (name, child) in OverlayAddressing.Addresses(node))
        {
            Walk(child, path.Add(name));
        }
    }

    /// <summary>
    /// Resolves the payload at one path, memoized, with the chain that reached it for cycle
    /// reporting.
    /// </summary>
    /// <returns>
    /// The resolved payload, or <see langword="null"/> when resolution failed and a diagnostic was
    /// reported.
    /// </returns>
    private ScalarPayload? ResolveAt(ImmutableArray<NamePart> path, ImmutableArray<ImmutableArray<NamePart>> chain)
    {
        var key = CanonicalPath.Of(path) ?? string.Empty;

        if (resolved.TryGetValue(key, out var memo))
        {
            return memo;
        }

        if (chain.Any(entry => string.Equals(CanonicalPath.Of(entry), key, StringComparison.Ordinal)))
        {
            ReportCycle(path, chain);
            resolved[key] = null;
            return null;
        }

        var node = Descend(path);

        if (node?.Payload is not { IsUnresolved: true } payload)
        {
            // Already a settled scalar, or nothing at all. Either way there is nothing to resolve;
            // the caller decides whether the absence is a missing-reference error.
            return node?.Payload;
        }

        // Section 6.2 bounds "reference recursion depth" and Section 23 requires the reference
        // budget be "consumed in [its] normative pipeline order". The level charged is the number
        // of nested unresolved values entered, so reading a value that is already settled costs
        // nothing: that is a lookup, not recursion. TryEnter rather than TryConsume because
        // Section 23 lists this among the levels rather than the totals -- a wide model that
        // resolves a thousand independent one-deep references is not deep.
        if (!budget.TryEnter(ResourceBound.MaxReferenceDepth, chain.Length + 1, out var tooDeep))
        {
            ReportDepth(path, tooDeep);
            resolved[key] = null;

            return null;
        }

        var value = Resolve(payload, path, chain.Add(path));

        resolved[key] = value;

        return value;
    }

    private ScalarPayload? Resolve(
        ScalarPayload payload, ImmutableArray<NamePart> path, ImmutableArray<ImmutableArray<NamePart>> chain)
    {
        var tokens = payload.UnresolvedValue.Tokens;

        // Section 13.2: "If a value consists of exactly one reference and no literal text, it
        // inherits the referenced scalar kind and value." Type forwarding is transitive because the
        // referent is itself resolved before it is adopted.
        if (tokens is [ReferenceToken single])
        {
            return Target(single, path, chain)?.As(payload.Spelling);
        }

        var text = new StringBuilder();

        foreach (var token in tokens)
        {
            switch (token)
            {
                case LiteralValueToken literal:
                    text.Append(literal.Text);
                    break;

                case ReferenceToken reference:
                    if (Target(reference, path, chain) is not { } referent)
                    {
                        return null;
                    }

                    text.Append(Interpolate(referent));
                    break;

                default:
                    throw new InvalidOperationException(
                        "Section 12 substitutes every wildcard token before step 15, so an "
                        + "unresolved payload reaching here holds only literals and references.");
            }
        }

        // Section 13.2: "If a value contains any concatenation, its result is a string." It is a
        // settled string rather than an untyped one, so Section 18 inference never sees it: the
        // rule already decided the kind, and re-inferring would turn '${a}${b}' over two digits
        // into a number the specification says is a string.
        return ScalarPayload.OfString(text.ToString()).As(payload.Spelling);
    }

    private ScalarPayload? Target(
        ReferenceToken reference,
        ImmutableArray<NamePart> path,
        ImmutableArray<ImmutableArray<NamePart>> chain)
    {
        var name = reference.Name;

        // Section 13.3: "Free wildcard references such as ${a.*} are blocking errors."
        if (name.Parts.Any(WildcardMatch.HasWildcard))
        {
            Report(
                DiagnosticCodes.Reference001,
                "\u00A713.3",
                path,
                $"the reference '{CanonicalPath.Of(name)}' still contains a wildcard, which no "
                + "capture bound, so it names a pattern rather than one scalar.");
            return null;
        }

        if (!TryAddress(name, path, out var target))
        {
            return null;
        }

        var referent = ResolveAt(target, chain);

        if (referent is null)
        {
            // Either the chain reported a cycle, or the referent's own resolution failed. Section
            // 22 counts these once per reachable owning value, and the value that failed has
            // already reported for itself; reporting again here would name this value for a defect
            // written somewhere else.
            return null;
        }

        if (referent.IsUnresolved)
        {
            throw new InvalidOperationException(
                "a memoized referent is either resolved or null.");
        }

        return referent;
    }

    /// <summary>Section 13.1 addressing: one exact path, or one entry of the alias index.</summary>
    private bool TryAddress(
        QualifiedName name,
        ImmutableArray<NamePart> path,
        out ImmutableArray<NamePart> target)
    {
        target = [];

        if (IsCanonical(name))
        {
            var node = Descend(name.Parts);

            if (node?.Payload is null)
            {
                ReportMissing(name, node, path);
                return false;
            }

            target = name.Parts;
            return true;
        }

        if (!aliases.TryGetValue(CanonicalPath.Of(name) ?? string.Empty, out var candidates))
        {
            ReportMissing(name, Descend(name.Parts), path);
            return false;
        }

        // Section 13.1: "more than one canonical scalar having the same simple alias is a blocking
        // ambiguous-reference error that lists the canonical candidates."
        if (candidates.Length > 1)
        {
            Report(
                DiagnosticCodes.Reference004,
                "\u00A713.1",
                path,
                $"the reference '{CanonicalPath.Of(name)}' has {candidates.Length} canonical "
                + "scalars with that simple alias: "
                + string.Join(", ", candidates.Select(c => $"'{CanonicalPath.Of(c)}'"))
                + ". Address one of them exactly.");
            return false;
        }

        target = candidates[0];
        return true;
    }

    private void ReportMissing(
        QualifiedName name,
        OverlayNode? node,
        ImmutableArray<NamePart> path)
    {
        // Section 13.1 separates the two: "referencing a path that has descendants but no
        // scalar/null payload is a missing-reference error", while Section 13.3 makes a structured
        // node a non-scalar reference. The distinction is whether the reference names a node the
        // model treats as a container in its own right.
        if (node is not null && (node.HasExplicitMapping || node.HasExplicitSequence))
        {
            Report(
                DiagnosticCodes.Reference005,
                "\u00A713.3",
                path,
                $"the reference '{CanonicalPath.Of(name)}' names a structured node, and Section "
                + "13.1 copies only the scalar payload stored at the exact canonical path.");
            return;
        }

        Report(
            DiagnosticCodes.Reference002,
            "\u00A713.1",
            path,
            $"the reference '{CanonicalPath.Of(name)}' resolves to no scalar or null payload.");
    }

    /// <summary>Reports one cycle, keyed and located by the cycle rather than by its entry point.</summary>
    /// <param name="path">The path whose resolution re-entered the chain.</param>
    /// <param name="chain">The paths currently being resolved, outermost first.</param>
    /// <remarks>
    /// <para>
    /// Section 22 fixes the canonical form: "Rotate the ring so its lexicographically smallest
    /// canonical path under unsigned UTF-8 byte order is first; when the same smallest path appears
    /// more than once, choose the lexicographically smallest resulting rotated sequence." Comparing
    /// whole rotations states both halves once — the first members are weighed first, so a strictly
    /// smaller member wins outright and a tie falls through to the rest of the ring. Written as a
    /// separate tie-break the second half would be unreachable code: <c>ResolveAt</c> pushes a path
    /// only when its canonical spelling is not already on the chain, so a detected ring holds
    /// pairwise distinct members.
    /// </para>
    /// <para>
    /// A cycle of <c>n</c> members is entered from all <c>n</c> of them, and each entry spells the
    /// chain starting somewhere else, so both the key and the reported location are taken from that
    /// rotation. Keying alone is not enough: Section 24 makes the <c>source</c>, <c>line</c> and
    /// <c>path</c> members part of the output too, and locating the report at whichever member an
    /// output instance happened to reach first would make them depend on selector order.
    /// </para>
    /// <para>
    /// The identity of a ring is the ring, not its printed form. A canonical path escapes the
    /// delimiter, <c>=</c>, <c>}</c>, <c>*</c> and the line terminators, but not a space, a hyphen
    /// or a greater-than sign, so a member may legitimately contain the text this method joins on:
    /// <c>["a -&gt; b", "c"]</c> and <c>["a", "b -&gt; c"]</c> print identically. Deduplicating on
    /// the printed chain would silently discard one of two genuinely distinct cycles, which is the
    /// same failure <see cref="CanonicalPath.Of(QualifiedName?)"/> documents for a non-injective
    /// path spelling. The identity therefore length-prefixes each member and the prose is built
    /// separately.
    /// </para>
    /// </remarks>
    private void ReportCycle(
        ImmutableArray<NamePart> path, ImmutableArray<ImmutableArray<NamePart>> chain)
    {
        var entry = CanonicalPath.Of(path) ?? string.Empty;
        var keys = chain.Select(member => CanonicalPath.Of(member) ?? string.Empty).ToArray();
        var start = Array.IndexOf(keys, entry);
        var members = chain.RemoveRange(0, start < 0 ? 0 : start);

        keys = [.. keys.Skip(start < 0 ? 0 : start)];

        var least = 0;

        for (var i = 1; i < keys.Length; i++)
        {
            // CompareRotations weighs first members first, so this one comparison is the whole
            // Section 22 rule: least first member, and the whole sequence where those tie.
            if (CompareRotations(keys, i, least) < 0)
            {
                least = i;
            }
        }

        var rotated = members.RemoveRange(0, least).AddRange(members.Take(least));
        var order = keys.Skip(least).Concat(keys.Take(least)).ToArray();
        var identity = string.Concat(order.Select(member => $"{member.Length}\u0000{member}"));

        if (!reportedCycles.Add(identity))
        {
            return;
        }

        var display = string.Join(" -> ", order);

        Report(
            DiagnosticCodes.Reference003,
            "\u00A713.1",
            rotated[0],
            $"the reference chain {display} -> {order[0]} is a cycle, so no value "
            + "in it can be resolved.");
    }

    /// <summary>
    /// Compares the two rotations of <paramref name="keys"/> beginning at the given offsets, under
    /// the Section 22 unsigned UTF-8 byte order.
    /// </summary>
    /// <param name="keys">The ring's canonical member paths, in ring order.</param>
    /// <param name="left">The offset the left rotation starts at.</param>
    /// <param name="right">The offset the right rotation starts at.</param>
    /// <returns>A negative value, zero, or a positive value in the usual comparison convention.</returns>
    private static int CompareRotations(string[] keys, int left, int right)
    {
        for (var i = 0; i < keys.Length; i++)
        {
            var comparison = Utf8Order.Compare(
                keys[(left + i) % keys.Length], keys[(right + i) % keys.Length]);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    /// <summary>Buffers one reference diagnostic against the value that owns the reference.</summary>
    /// <param name="factory">The Section 22 code's factory.</param>
    /// <param name="spec">The clause the report enforces.</param>
    /// <param name="path">The owning value's path.</param>
    /// <param name="message">The prose.</param>
    /// <remarks>
    /// The Section 24 ordering key is the owning node's payload mark. Section 24 orders the stream
    /// "by source ordering key", and a reference defect is written at one place in one source even
    /// though it is detected five steps later. Leaving every reference diagnostic on one key would
    /// still be total — the comparer falls through to code and path — but it would order two
    /// defects in the same file by their names rather than by where they were written.
    /// </remarks>
    private void Report(
        Func<DiagnosticPhase, string, string, string, string?, int?, int?, string?, DiagnosticOccurrence> factory,
        string spec,
        ImmutableArray<NamePart> path,
        string message)
    {
        var canonical = CanonicalPath.Of(path);
        var node = Descend(path);
        var owner = node?.Payload;
        var origin = owner is { IsUnresolved: true } ? owner.Origin : default;

        diagnostics.Add(new BufferedDiagnostic(
            factory(
                DiagnosticPhase.Planning,
                spec,
                message,
                canonical ?? string.Empty,
                origin.Source,
                origin.Line,
                origin.Column,
                canonical),
            node?.Marks.PayloadMark ?? node?.Marks.Position ?? StableOrderingKey.First));
    }

    /// <summary>Reports the reference-depth budget crossing against the value that crossed it.</summary>
    /// <param name="path">The path whose resolution would have entered a level too deep.</param>
    /// <param name="fault">The crossing.</param>
    /// <remarks>
    /// <c>LIMIT001</c> is counted once per invocation, so the buffer keeps the first of these and
    /// the rest are the same crossing seen again from another reachable value. Locating it at the
    /// owning payload still matters for that first one, and is what <see cref="Report"/> does for
    /// every other reference diagnostic.
    /// </remarks>
    private void ReportDepth(ImmutableArray<NamePart> path, BudgetFault fault)
    {
        var canonical = CanonicalPath.Of(path);
        var node = Descend(path);
        var owner = node?.Payload;
        var origin = owner is { IsUnresolved: true } ? owner.Origin : default;

        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Limit001(
                DiagnosticPhase.Planning,
                "\u00A723",
                $"resolving this reference crosses {fault.Spelling}, whose limit is {fault.Limit}.",
                origin.Source,
                origin.Line,
                origin.Column,
                canonical),
            node?.Marks.PayloadMark ?? node?.Marks.Position ?? StableOrderingKey.First));
    }

    private OverlayNode? Descend(ImmutableArray<NamePart> path)
    {
        var node = model;

        foreach (var part in path)
        {
            if (!node.Children.TryGetValue(part, out var child))
            {
                var addressed = OverlayAddressing.Addresses(node)
                    .Where(entry => entry.Key.Equals(part))
                    .Select(entry => entry.Value)
                    .FirstOrDefault();

                if (addressed is null)
                {
                    return null;
                }

                child = addressed;
            }

            node = child;
        }

        return node;
    }

    /// <summary>Rebuilds the model with every payload this run resolved.</summary>
    private OverlayNode Rewrite(OverlayNode node, ImmutableArray<NamePart> path)
    {
        var result = node;

        if (node.Payload is { IsUnresolved: true }
            && resolved.TryGetValue(CanonicalPath.Of(path) ?? string.Empty, out var value)
            && value is not null)
        {
            result = result.WithPayload(value, node.Marks.PayloadMark ?? node.Marks.Position);
        }

        foreach (var (name, child) in node.OrderedChildren)
        {
            var rewritten = Rewrite(child, path.Add(name));

            if (!ReferenceEquals(rewritten, child))
            {
                result = result.WithChild(name, rewritten);
            }
        }

        foreach (var (ordering, item) in node.OrderedSequence)
        {
            var name = OrderingValues.ToNamePart(ordering);
            var rewritten = Rewrite(item.Node, path.Add(name));

            if (!ReferenceEquals(rewritten, item.Node))
            {
                result = result.WithSequenceItem(ordering, item with { Node = rewritten });
            }
        }

        return result;
    }
}
