using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Scheme;

/// <summary>
/// Specification Section 15.1 step 1: "resolve references among scheme entries".
/// </summary>
/// <remarks>
/// <para>
/// This is a second reference resolver, and deliberately not the Section 13 one. That one resolves
/// a reference against the overlay: a tree of typed payloads, reached through the simple alias
/// index, after wildcard generation. A scheme builds no overlay. Section 15 requires scheme content
/// to "project to qualified directive paths and scalar directive values", and Section 15.1 adds
/// that "scheme references cannot target input data", so the graph resolved here is a much smaller
/// one — <c>(selector, directive)</c> to text — and every node in it is already required to hold "a
/// nonempty scalar value after format parsing".
/// </para>
/// <para>
/// Three consequences follow from the graph being that shape, and each is a rule this resolver does
/// not implement because there is nothing here for it to act on. There is no alias index, because a
/// directive name is a fixed ASCII vocabulary rather than an XML component, so <c>REFERENCE004</c>
/// cannot arise. There is no structured node, so <c>REFERENCE005</c> cannot arise. And there is no
/// scalar kind: Section 13.2's type forwarding governs typed payloads, and a directive value is
/// text that its own Section 16 clause interprets, so resolution here is textual splicing.
/// </para>
/// <para>
/// Section 22's cardinalities are stated as "once per reachable owning value" and "once per
/// canonically distinct reachable cycle", and both read in this phase without amendment: the owning
/// value is the scheme entry, and reachability is trivial because every scheme entry is compiled.
/// A shadowed declaration is resolved too — Section 15.4 has a phase "complete every independent
/// check that does not depend on a failed result", and a defect written in a declaration that a
/// later one overrides is still a defect the author wants to hear about.
/// </para>
/// </remarks>
public static class SchemeReferenceResolver
{
    /// <summary>Resolves every reference among the scheme entries.</summary>
    /// <param name="entries">Every scheme entry, as parsed.</param>
    /// <param name="budget">The invocation's global budget.</param>
    /// <param name="diagnostics">The scheme phase's buffer.</param>
    /// <returns>
    /// The same entries in the same order, with every reference-bearing value replaced by its
    /// resolved form. An entry whose value could not be resolved keeps its unresolved value; the
    /// phase aborts on the blocking diagnostic rather than on the value.
    /// </returns>
    public static ImmutableArray<SchemeEntry> Resolve(
        ImmutableArray<SchemeEntry> entries,
        GlobalBudget budget,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (entries.IsDefaultOrEmpty || !entries.Any(entry => entry.Value.ContainsReference))
        {
            return entries;
        }

        var resolver = new Session(entries, budget, diagnostics);
        var result = ImmutableArray.CreateBuilder<SchemeEntry>(entries.Length);

        foreach (var entry in entries)
        {
            result.Add(entry.Value.ContainsReference
                ? entry with { Value = resolver.Splice(entry, []) ?? entry.Value }
                : entry);
        }

        return result.ToImmutable();
    }

    /// <summary>One directive's identity: the setting a Section 15.2 override stream decides.</summary>
    private readonly record struct DirectiveKey(SelectorKey Selector, SchemeDirective Directive)
    {
        /// <inheritdoc/>
        public override string ToString()
        {
            var selector = Selector.ToString();
            var spelling = SchemeDirectives.CanonicalSpelling(Directive);

            return selector.Length == 0 ? spelling : $"{selector}.{spelling}";
        }
    }

    private sealed class Session
    {
        private readonly Dictionary<DirectiveKey, SchemeEntry> winners = [];
        private readonly Dictionary<DirectiveKey, InterpretedValue?> resolved = [];
        private readonly HashSet<string> reportedCycles = new(StringComparer.Ordinal);
        private readonly HashSet<string> warnedAliases = new(StringComparer.Ordinal);
        private readonly GlobalBudget budget;
        private readonly DiagnosticBuffer diagnostics;

        internal Session(
            ImmutableArray<SchemeEntry> entries, GlobalBudget budget, DiagnosticBuffer diagnostics)
        {
            this.budget = budget;
            this.diagnostics = diagnostics;

            // Section 15.2: "A later matching directive overrides an earlier matching directive for
            // the same effective setting." A reference names a setting, not a line, so it reads the
            // value that setting ends up with. Ordering by the Section 4.7 key rather than by array
            // position because SchemeCompiler decides the same winner the same way, and two
            // components that disagree about which declaration won would be worse than either.
            foreach (var entry in entries.OrderBy(entry => entry.Order))
            {
                winners[new DirectiveKey(new SelectorKey(entry.Selector), entry.Directive)] = entry;
            }
        }

        /// <summary>Resolves one value's references in place, keeping its other tokens.</summary>
        /// <param name="owner">The entry the value was written in.</param>
        /// <param name="chain">The directives currently being resolved, outermost first.</param>
        /// <returns>The resolved value, or <see langword="null"/> when it could not be resolved.</returns>
        /// <remarks>
        /// A Section 12.1 capture token is spliced rather than resolved. Captures are bound where a
        /// directive's value is read — at Section 14.1 expansion or at a per-path match — which is
        /// several steps after this one, so a value carrying both a reference and a <c>*</c> must
        /// survive step 1 with the <c>*</c> intact: <c>app.*.filename=${app.delimiter}*.conf</c>
        /// resolves the reference here and still writes one file per match.
        /// <para>
        /// What a reference contributes is one <see cref="ResolvedReferenceToken"/> rather than the
        /// referent's own tokens, because Section 16.2 requires the result to stay distinguishable
        /// from text the author wrote: it is "opaque segment data", so a <c>/</c> that arrived
        /// through a reference is encoded instead of creating a directory. Everything downstream
        /// that does not care reads it through <see cref="InterpretedValue.LiteralText"/>.
        /// </para>
        /// <para>
        /// The referent's text is total in practice: a reference naming a wildcard selector is
        /// refused above, and <c>*</c> in the value of a selector that defines no capture lexes as a
        /// literal asterisk rather than as a substitution, so a referent never holds a capture. The
        /// fallback that splices tokens is therefore unreachable, and exists so that a future
        /// referent shape degrades to the old behaviour rather than to an exception. The empty-text
        /// guard is unreachable for the same kind of reason — Section 15 rejects an empty directive
        /// value when the scheme is read — and keeps a value that somehow reached it in Section 22
        /// canonical form rather than carrying a token that contributes nothing.
        /// </para>
        /// </remarks>
        internal InterpretedValue? Splice(SchemeEntry owner, ImmutableArray<DirectiveKey> chain)
        {
            var tokens = ImmutableArray.CreateBuilder<ValueToken>();

            foreach (var token in owner.Value.Tokens)
            {
                if (token is not ReferenceToken reference)
                {
                    tokens.Add(token);
                    continue;
                }

                if (Target(reference, owner, chain) is not { } referent)
                {
                    return null;
                }

                if (referent.LiteralText is not { } text)
                {
                    tokens.AddRange(referent.Tokens);
                }
                else if (text.Length > 0)
                {
                    tokens.Add(new ResolvedReferenceToken(text));
                }
            }

            return new InterpretedValue(tokens.ToImmutable());
        }

        private InterpretedValue? Target(
            ReferenceToken reference, SchemeEntry owner, ImmutableArray<DirectiveKey> chain)
        {
            var name = reference.Name;

            // Section 13.3: "Free wildcard references such as ${a.*} are blocking errors." A scheme
            // reference cannot contain a bound capture either, because the captures a selector
            // defines are not known until the step that reads the directive.
            if (name.Parts.Any(WildcardMatch.HasWildcard))
            {
                Report(
                    DiagnosticCodes.Reference001,
                    "\u00A713.3",
                    owner,
                    $"the reference '{CanonicalPath.Of(name)}' contains a wildcard, and Section "
                    + "15.1 step 1 resolves references among scheme entries before any selector is "
                    + "expanded.");
                return null;
            }

            if (!TryAddress(name, owner, out var key))
            {
                return null;
            }

            if (chain.Contains(key))
            {
                ReportCycle(key, owner, chain);
                resolved[key] = null;
                return null;
            }

            return ResolveKey(key, name, owner, chain);
        }

        /// <summary>Maps a reference name to the directive it names.</summary>
        /// <param name="name">The referenced name.</param>
        /// <param name="owner">The entry the reference was written in.</param>
        /// <param name="key">The directive named, when there is one.</param>
        /// <returns>Whether the name spells a directive at all.</returns>
        /// <remarks>
        /// Section 15's "the final qualified-name part identifies a directive" is what makes this a
        /// total function rather than a lookup: a reference whose last part is not a directive name
        /// cannot name a scheme entry however the rest of it is spelled, so it is missing before the
        /// index is consulted. Section 15.1's "scheme references cannot target input data" is the
        /// reason that is an error rather than a fallback to the overlay.
        /// </remarks>
        private bool TryAddress(QualifiedName name, SchemeEntry owner, out DirectiveKey key)
        {
            key = default;

            if (name.Parts[^1] is not OrdinaryPart { LiteralText: { } last })
            {
                ReportMissing(name, owner, "its final part is not literal text, so it names no "
                    + "Section 15 directive");
                return false;
            }

            if (!SchemeDirectives.TryRecognize(last, out var directive, out var alias))
            {
                ReportMissing(name, owner, $"'{last}' is not a recognized Section 15 directive");
                return false;
            }

            if (alias != SchemeAlias.None)
            {
                WarnAlias(alias, owner);
            }

            var selector = name.Parts.Length == 1
                ? null
                : new QualifiedName([.. name.Parts[..^1]]);

            key = new DirectiveKey(new SelectorKey(selector), directive);
            return true;
        }

        private InterpretedValue? ResolveKey(
            DirectiveKey key,
            QualifiedName name,
            SchemeEntry owner,
            ImmutableArray<DirectiveKey> chain)
        {
            if (resolved.TryGetValue(key, out var memo))
            {
                return memo;
            }

            if (!winners.TryGetValue(key, out var target))
            {
                ReportMissing(name, owner, "no scheme entry declares it, and Section 15.1 step 1 "
                    + "says scheme references cannot target input data");
                return null;
            }

            if (!target.Value.ContainsReference)
            {
                resolved[key] = target.Value;
                return target.Value;
            }

            // Section 6.2 bounds "reference recursion depth", and Section 23 requires the bound be
            // consumed in normative pipeline order — which puts step 1 before step 15. The level
            // charged is the number of nested unresolved values entered, so reading a value that
            // holds no reference costs nothing: that is a lookup, not recursion.
            if (!budget.TryEnter(ResourceBound.MaxReferenceDepth, chain.Length + 1, out var tooDeep))
            {
                ReportDepth(owner, tooDeep);
                resolved[key] = null;
                return null;
            }

            var value = Splice(target, chain.Add(key));

            resolved[key] = value;

            return value;
        }

        private void ReportMissing(QualifiedName name, SchemeEntry owner, string because) =>
            Report(
                DiagnosticCodes.Reference002,
                "\u00A715.1",
                owner,
                $"the reference '{CanonicalPath.Of(name)}' resolves to no scheme entry: {because}.");

        /// <summary>Reports one cycle, keyed and located by the cycle rather than by its entry point.</summary>
        /// <param name="key">The directive whose resolution re-entered the chain.</param>
        /// <param name="owner">The entry the closing reference was written in.</param>
        /// <param name="chain">The directives currently being resolved, outermost first.</param>
        /// <remarks>
        /// <see cref="ReferenceCycles"/> fixes the canonical form Section 22 requires. The report is
        /// located at the ring's canonical first member rather than at the entry the chain happened
        /// to start from, because Section 24 puts <c>source</c>, <c>line</c> and <c>path</c> in the
        /// stream too, and a ring is the same ring whichever of its members was resolved first.
        /// </remarks>
        private void ReportCycle(
            DirectiveKey key, SchemeEntry owner, ImmutableArray<DirectiveKey> chain)
        {
            var start = chain.IndexOf(key);
            var members = chain.RemoveRange(0, start < 0 ? 0 : start);
            var ring = members.Select(member => member.ToString()).ToArray();
            var least = ReferenceCycles.LeastRotation(ring);
            var rotated = members.RemoveRange(0, least).AddRange(members.Take(least));
            var order = ring.Skip(least).Concat(ring.Take(least)).ToArray();

            if (!reportedCycles.Add(ReferenceCycles.Identity(order)))
            {
                return;
            }

            var at = winners.TryGetValue(rotated[0], out var first) ? first : owner;

            Report(
                DiagnosticCodes.Reference003,
                "\u00A713.1",
                at,
                $"the scheme reference chain {string.Join(" -> ", order)} -> {order[0]} is a cycle, "
                + "so no directive in it can be resolved.");
        }

        private void ReportDepth(SchemeEntry owner, BudgetFault fault) =>
            diagnostics.Add(new BufferedDiagnostic(
                DiagnosticCodes.Limit001(
                    DiagnosticPhase.Scheme,
                    "\u00A723",
                    $"resolving this scheme reference crosses {fault.Spelling}, whose limit is "
                    + $"{fault.Limit}.",
                    owner.Source,
                    owner.Line,
                    column: null,
                    path: Path(owner)),
                owner.Order));

        /// <remarks>
        /// Section 15.3 raises <c>WARN002</c> "once per alias category and scheme", and the key
        /// below is that scope — the same one the readers use, so an alias written once as a
        /// declaration and once inside a reference in the same file warns once, as the registry
        /// says. The set here is a second guard only for the message, which names the reference.
        /// </remarks>
        private void WarnAlias(SchemeAlias alias, SchemeEntry owner)
        {
            var key = $"{owner.Source}:{alias}";

            if (!warnedAliases.Add(key))
            {
                return;
            }

            diagnostics.Add(new BufferedDiagnostic(
                DiagnosticCodes.Warn002(
                    DiagnosticPhase.Scheme,
                    "\u00A715.3",
                    $"'{SchemeDirectives.Spelling(alias)}' is a deprecated alias for "
                    + $"'{SchemeDirectives.Replacement(alias)}'.",
                    cardinalityKey: key,
                    source: owner.Source,
                    line: owner.Line,
                    column: null,
                    declaration: owner.Declaration),
                owner.Order));
        }

        /// <summary>Buffers one reference diagnostic against the entry that owns the reference.</summary>
        /// <param name="factory">The Section 22 code's factory.</param>
        /// <param name="spec">The clause the report enforces.</param>
        /// <param name="owner">The entry the reference was written in.</param>
        /// <param name="message">The prose.</param>
        private void Report(
            Func<DiagnosticPhase, string, string, string, string?, int?, int?, string?, DiagnosticOccurrence> factory,
            string spec,
            SchemeEntry owner,
            string message)
        {
            var path = Path(owner);

            diagnostics.Add(new BufferedDiagnostic(
                factory(
                    DiagnosticPhase.Scheme,
                    spec,
                    message,
                    path,
                    owner.Source,
                    owner.Line,
                    null,
                    path),
                owner.Order));
        }

        private static string Path(SchemeEntry owner) =>
            new DirectiveKey(new SelectorKey(owner.Selector), owner.Directive).ToString();
    }
}
