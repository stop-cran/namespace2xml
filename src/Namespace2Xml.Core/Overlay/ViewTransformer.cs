using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scheme;

namespace Namespace2Xml.Overlay;

/// <summary>
/// The effective Section 16.5 and 16.6 configuration at one absolute path.
/// </summary>
/// <param name="Types">The winning type set, or null when no <c>type</c> directive matched.</param>
/// <param name="Key">The winning <c>key</c> field, or null when no <c>key</c> directive matched.</param>
/// <param name="TypeRule">The rule the type set came from, for the diagnostic members.</param>
/// <param name="KeyRule">The rule the key came from, for the diagnostic members.</param>
public readonly record struct EffectiveTransform(
    TypeSet? Types,
    string? Key,
    TransformRule? TypeRule,
    TransformRule? KeyRule);

/// <summary>
/// Specification Section 15.1 step 16: the path-scoped transformations, applied to one output view
/// in the normative order.
/// </summary>
/// <remarks>
/// <para>
/// Step 16 fixes the order as "<c>type=ignore</c>, explicit scalar/XML types, <c>type=array</c> or
/// <c>type=mapping</c>, <c>multiline</c>, <c>key</c>, then <c>root</c>", and the order is
/// observable: <c>type=array,multiline</c> converts a mapping to a sequence and then joins that
/// sequence, while the reverse order would meet a mapping with no sequence projection and be
/// <c>TYPE001</c>. Each kind is therefore a separate pass over the view rather than one visit per
/// node that applies everything it finds.
/// </para>
/// <para>
/// <c>root</c> is not a pass here. It is resolved into <see cref="Scheme.OutputInstance.Root"/> at
/// step 14 and applied at serialization, which is the same position in the order because it only
/// wraps the finished view.
/// </para>
/// <para>
/// Section 15.2 evaluates these rules "against absolute stable pre-transformation paths". The table
/// is therefore built once, against the paths as they stand when step 16 begins, and every pass
/// reads it. A pass that recomputed matches against its own output would let <c>key</c> see the
/// record paths <c>key</c> had just invented, which is the restart Section 15.1 forbids: "a
/// transformation does not cause scheme matching to restart against newly created paths".
/// </para>
/// <para>
/// Binding once is not by itself enough to keep that promise, because a reshaping pass moves the
/// values a later pass has to find. Section 15.1 therefore re-addresses the table together with the
/// values whenever a pass moves one: <c>type=array</c> replaces a mapping key with an ordering
/// value, and <c>key</c> puts a child in a record. Without that, a directive bound beneath a
/// reshaped node would silently do nothing — the pass would look for a spelling the earlier pass
/// had just destroyed — which is the failure Section 15.1 now names in full.
/// </para>
/// </remarks>
public static class ViewTransformer
{
    /// <summary>Applies every step 16 transformation to one view.</summary>
    /// <param name="view">The selected view, with the selector prefix already removed.</param>
    /// <param name="prefix">The absolute path the view is rooted at.</param>
    /// <param name="rules">Every compiled rule, in source order.</param>
    /// <param name="report">Receives a diagnostic and the ordering key it is buffered under.</param>
    /// <param name="bound">
    /// Receives the source order of every rule that bound to a live path in this view. Section 15.2
    /// warns once per declaration that binds nowhere, and a rule binding in one output instance and
    /// not another has bound, so the caller unions this across every view before warning.
    /// </param>
    /// <param name="effective">
    /// Receives the bound table, keyed by canonical path relative to the selector prefix. Section
    /// 16.6's XML node kinds "select an XML rendering for a scalar" without changing the view, so
    /// the format that has them reads them here at serialization rather than in a pass.
    /// </param>
    /// <returns>The transformed view, or null when the view root itself was ignored.</returns>
    public static OverlayNode? Transform(
        OverlayNode view,
        ImmutableArray<NamePart> prefix,
        ImmutableArray<TransformRule> rules,
        Action<DiagnosticOccurrence, StableOrderingKey> report,
        ISet<StableOrderingKey> bound,
        out IReadOnlyDictionary<string, EffectiveTransform> effective)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(bound);

        if (rules.IsDefaultOrEmpty)
        {
            effective = ImmutableDictionary<string, EffectiveTransform>.Empty;
            return view;
        }

        var table = new Dictionary<string, EffectiveTransform>(StringComparer.Ordinal);
        var paths = new Dictionary<string, ImmutableArray<NamePart>>(StringComparer.Ordinal);
        var bindings = new List<(TransformRule Rule, ImmutableArray<NamePart> Relative)>();
        Bind(view, prefix, [], rules, table, paths, bindings);
        Live(table, bindings, bound);
        effective = table;

        if (!ValidateAliases(prefix, bindings, report) || !Validate(prefix, table, paths, report))
        {
            return view;
        }

        var context = new Pass(prefix, table, paths, report);

        var ignored = context.Ignore(view, []);

        if (ignored is null)
        {
            return null;
        }

        // Explicit scalar and XML types are the second kind in the step 16 order. They select an
        // XML rendering for a scalar and force string rendering of one, and neither changes the
        // shape of the view, so there is nothing for a flat destination to do with them. They are
        // read from the same table at serialization time by the format that has them.
        var shaped = context.Shape(ignored, []);

        // Section 15.1: a reshaping pass "re-addresses the surviving descendants of that node", so
        // the passes that follow — and the serializer, which reads the table for the explicit
        // scalar and XML node kinds — find each directive at the address its value now has.
        context.Readdress();

        var joined = context.Multiline(shaped, []);
        var keyed = context.Key(joined, []);

        context.Readdress();

        return keyed;
    }

    /// <summary>
    /// The effective transform at one absolute path, for a serializer that needs an explicit
    /// scalar or XML type.
    /// </summary>
    /// <param name="effective">The bound table.</param>
    /// <param name="path">The absolute canonical path.</param>
    public static EffectiveTransform At(
        IReadOnlyDictionary<string, EffectiveTransform> effective,
        string path)
    {
        ArgumentNullException.ThrowIfNull(effective);

        return effective.TryGetValue(path, out var found) ? found : default;
    }

    /// <summary>
    /// Binds every rule to the absolute paths it matches, keeping the Section 15.2 later winner.
    /// </summary>
    private static void Bind(
        OverlayNode node,
        ImmutableArray<NamePart> prefix,
        ImmutableArray<NamePart> relative,
        ImmutableArray<TransformRule> rules,
        Dictionary<string, EffectiveTransform> effective,
        Dictionary<string, ImmutableArray<NamePart>> paths,
        List<(TransformRule Rule, ImmutableArray<NamePart> Relative)> bindings)
    {
        var absolute = prefix.AddRange(relative);
        var key = CanonicalPath.Of(relative) ?? string.Empty;

        foreach (var rule in rules)
        {
            if (!Matches(rule, absolute, out var captures))
            {
                continue;
            }

            bindings.Add((rule, relative));
            paths[key] = relative;

            var current = effective.TryGetValue(key, out var found) ? found : default;

            // Section 15.2: "a later matching directive overrides an earlier matching directive for
            // the same effective setting", and Section 16.6 makes the complete set the unit, so a
            // later type rule replaces the whole earlier set rather than merging into it.
            effective[key] = rule.Types is not null
                ? current with { Types = rule.Types, TypeRule = rule }
                : current with { Key = Field(rule, captures), KeyRule = rule };
        }

        foreach (var (name, child) in OverlayAddressing.Addresses(node))
        {
            Bind(child, prefix, relative.Add(name), rules, effective, paths, bindings);
        }
    }

    /// <summary>
    /// Section 16.6: "A directive matching only a descendant of an ignored path is inert and emits
    /// the unbound-directive warning from Section 15.2."
    /// </summary>
    /// <remarks>
    /// A binding at the ignored path itself is live: only a later complete non-ignore type set
    /// matching that path restores it, and that later set is written at exactly that path.
    /// </remarks>
    private static void Live(
        Dictionary<string, EffectiveTransform> effective,
        List<(TransformRule Rule, ImmutableArray<NamePart> Relative)> bindings,
        ISet<StableOrderingKey> bound)
    {
        foreach (var (rule, relative) in bindings)
        {
            var ignored = false;

            for (var depth = 0; depth < relative.Length && !ignored; depth++)
            {
                var ancestor = CanonicalPath.Of(relative[..depth]) ?? string.Empty;

                ignored = effective.TryGetValue(ancestor, out var above)
                    && above.Types is { IsIgnore: true };
            }

            if (!ignored)
            {
                bound.Add(rule.Order);
            }
        }
    }

    /// <summary>
    /// Whether one rule's path matches one absolute path, under the Section 15.2 typed model and
    /// the alias that section grants an unmarked component.
    /// </summary>
    /// <remarks>
    /// The concrete path is folded rather than the pattern, so <see cref="WildcardMatch"/> keeps
    /// its literal typed semantics and its constrained-capture handling. Section 15.2 grants the
    /// alias to <em>scheme paths</em> alone; Sections 8.6 and 12 say nothing of the kind, and
    /// <see cref="WildcardMatch"/> is shared with both.
    /// </remarks>
    private static bool Matches(
        TransformRule rule,
        ImmutableArray<NamePart> absolute,
        out WildcardCaptures captures)
    {
        if (rule.Path is not { } pattern)
        {
            captures = WildcardCaptures.Empty;
            return absolute.IsEmpty;
        }

        if (pattern.Parts.Length != absolute.Length)
        {
            captures = WildcardCaptures.Empty;
            return false;
        }

        return WildcardMatch.TryMatchPrefix(
            pattern.Parts,
            pattern.Parts.Length,
            SchemeAlias.Align(pattern.Parts, pattern.Parts.Length, absolute),
            out captures);
    }

    /// <summary>
    /// Section 12.1: a <c>key</c> value's <c>*</c> is a positional substitution of the captures the
    /// rule's own path bound at this location, so one wildcard rule can name a different field at
    /// every path it matches.
    /// </summary>
    /// <param name="rule">The matched rule.</param>
    /// <param name="captures">The captures this match bound.</param>
    /// <returns>The field name, or null when the rule is a <c>type</c>.</returns>
    private static string? Field(TransformRule rule, WildcardCaptures captures) =>
        rule.KeyField is { } template ? WildcardSubstitution.Apply(template, captures) : null;

    /// <summary>
    /// Section 16.5: "An effective <c>type=array</c> and <c>key</c> at the same path is therefore an
    /// illegal option combination and raises <c>SCHEME001</c>."
    /// </summary>
    /// <summary>
    /// Section 15.2 <c>SCHEME002</c>: "if ordinary and XML components make that alias ambiguous at
    /// a matched location, selector expansion at pipeline step 13 emits blocking <c>SCHEME002</c>
    /// and lists the canonical alternatives".
    /// </summary>
    /// <param name="prefix">The absolute path the view is rooted at.</param>
    /// <param name="bindings">Every rule-to-path binding the walk made, in walk order.</param>
    /// <param name="report">Receives the diagnostic and the ordering key it is buffered under.</param>
    /// <returns><see langword="false"/> when a directive is ambiguous.</returns>
    /// <remarks>
    /// <para>
    /// A rule reaching two distinct canonical paths that fold to one aliased path <em>is</em> the
    /// ambiguity: the fold is what let a single written component reach both, and a rule whose fold
    /// changed nothing cannot collide with itself, because Section 19.1's spelling is injective.
    /// Detecting it from the bindings rather than from the tree therefore needs no second walk and
    /// cannot report where no directive was written.
    /// </para>
    /// <para>
    /// The cardinality is "once per expanded declaration", and
    /// <see cref="Pipeline.DiagnosticBuffer"/> is what enforces it: it keys an entry on the code
    /// and the cardinality key, so the several locations one wildcard rule is ambiguous at collapse
    /// to the earliest of them. A second guard here would be dead code. The alternatives are sorted
    /// so the message does not depend on which of them the walk reached first.
    /// </para>
    /// </remarks>
    private static bool ValidateAliases(
        ImmutableArray<NamePart> prefix,
        List<(TransformRule Rule, ImmutableArray<NamePart> Relative)> bindings,
        Action<DiagnosticOccurrence, StableOrderingKey> report)
    {
        var reached = new Dictionary<(StableOrderingKey Order, string Aliased), string>();
        var legal = true;

        foreach (var (rule, relative) in bindings)
        {
            if (rule.Path is not { } pattern)
            {
                continue;
            }

            var absolute = prefix.AddRange(relative);
            var canonical = CanonicalPath.Of(absolute) ?? string.Empty;
            var aliased = CanonicalPath.Of(
                SchemeAlias.Align(pattern.Parts, pattern.Parts.Length, absolute)) ?? string.Empty;

            if (!reached.TryAdd((rule.Order, aliased), canonical))
            {
                var alternatives = new[] { reached[(rule.Order, aliased)], canonical };
                Array.Sort(alternatives, StringComparer.Ordinal);

                report(
                    DiagnosticCodes.Scheme002(
                        DiagnosticPhase.Planning,
                        "\u00A715.2",
                        $"'{rule.Declaration.Text}' reaches '{alternatives[0]}' and "
                        + $"'{alternatives[1]}' through one simple alias, and Section 15.2 makes an "
                        + "ambiguous alias an error rather than an order to choose between. Mark "
                        + "the component to name one of them outright.",
                        cardinalityKey: $"{rule.Declaration.Source}:{rule.Declaration.Line}",
                        source: rule.Declaration.Source,
                        line: rule.Declaration.Line,
                        path: aliased,
                        declaration: rule.Declaration.Text),
                    rule.Order);

                legal = false;
            }
        }

        return legal;
    }

    private static bool Validate(
        ImmutableArray<NamePart> prefix,
        Dictionary<string, EffectiveTransform> effective,
        Dictionary<string, ImmutableArray<NamePart>> paths,
        Action<DiagnosticOccurrence, StableOrderingKey> report)
    {
        var legal = true;

        foreach (var (key, transform) in effective.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (transform.Key is null || transform.Types is not { IsArray: true })
            {
                continue;
            }

            var rule = transform.KeyRule!;

            // Section 22's 'path' member names a canonical path, and the table is keyed by the
            // view-relative one, which is not a path any input ever wrote.
            var path = CanonicalPath.Of(prefix.AddRange(paths[key]));

            report(
                DiagnosticCodes.Scheme001(
                    DiagnosticPhase.Planning,
                    "\u00A716.5",
                    $"'{rule.Declaration.Text}' and '{transform.TypeRule!.Declaration.Text}' both "
                    + $"take effect at '{path}', and Section 16.5 makes 'type=array' with 'key' an "
                    + "illegal option combination rather than an order to choose between.",
                    cardinalityKey: $"{rule.Declaration.Source}:{rule.Declaration.Line}",
                    source: rule.Declaration.Source,
                    line: rule.Declaration.Line,
                    path: path,
                    declaration: rule.Declaration.Text),
                rule.Order);

            legal = false;
        }

        return legal;
    }

    private sealed class Pass(
        ImmutableArray<NamePart> prefix,
        Dictionary<string, EffectiveTransform> effective,
        Dictionary<string, ImmutableArray<NamePart>> paths,
        Action<DiagnosticOccurrence, StableOrderingKey> report)
    {
        /// <summary>Where a reshaping pass has moved each child it moved.</summary>
        /// <remarks>
        /// Keyed by the path of the node whose children moved, in the coordinates the bound table
        /// currently uses, and valued by the address each moved child now has relative to that
        /// node. <see cref="Readdress"/> folds these from the root, so a move recorded for a child
        /// during the same children-first walk composes with the move recorded for its parent.
        /// </remarks>
        private readonly Dictionary<string, Dictionary<NamePart, ImmutableArray<NamePart>>> moves =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Section 15.1: re-addresses the bound table over the moves the last passes recorded, so
        /// that a directive bound beneath a reshaped node is applied to the value it bound to
        /// rather than to a spelling the reshaping destroyed.
        /// </summary>
        /// <remarks>
        /// The moves are consumed, because the next pass records its own in the coordinates this
        /// call establishes.
        /// </remarks>
        public void Readdress()
        {
            if (moves.Count == 0)
            {
                return;
            }

            var moved = new List<(string Key, ImmutableArray<NamePart> Path, EffectiveTransform Transform)>();

            foreach (var (key, path) in paths)
            {
                var translated = Translate(path);

                moved.Add((CanonicalPath.Of(translated) ?? string.Empty, translated, effective[key]));
            }

            effective.Clear();
            paths.Clear();

            foreach (var (key, path, transform) in moved)
            {
                effective[key] = transform;
                paths[key] = path;
            }

            moves.Clear();
        }

        /// <summary>Records that one child of <paramref name="relative"/> has moved.</summary>
        /// <param name="relative">The path of the node whose child moved.</param>
        /// <param name="from">The name the child had.</param>
        /// <param name="to">The address the child now has, relative to the node.</param>
        private void Record(ImmutableArray<NamePart> relative, NamePart from, ImmutableArray<NamePart> to)
        {
            var key = CanonicalPath.Of(relative) ?? string.Empty;

            if (!moves.TryGetValue(key, out var at))
            {
                at = new Dictionary<NamePart, ImmutableArray<NamePart>>();
                moves[key] = at;
            }

            at[from] = to;
        }

        /// <summary>The address a pre-move path now has.</summary>
        private ImmutableArray<NamePart> Translate(ImmutableArray<NamePart> path)
        {
            var current = ImmutableArray<NamePart>.Empty;
            var seen = ImmutableArray<NamePart>.Empty;

            foreach (var part in path)
            {
                current = moves.TryGetValue(CanonicalPath.Of(seen) ?? string.Empty, out var at)
                    && at.TryGetValue(part, out var to)
                        ? current.AddRange(to)
                        : current.Add(part);
                seen = seen.Add(part);
            }

            return current;
        }

        /// <summary>Step 16's first kind: <c>type=ignore</c>.</summary>
        /// <remarks>
        /// Section 16.6: "An effective ignore at path <c>P</c> removes every descendant regardless
        /// of directives matching those descendants." Removal is therefore whole-subtree and the
        /// walk does not descend past it, which is also why a descendant's own directive becomes
        /// inert rather than being applied to something that no longer exists.
        /// </remarks>
        public OverlayNode? Ignore(OverlayNode node, ImmutableArray<NamePart> relative)
        {
            if (Effective(relative).Types is { IsIgnore: true })
            {
                if (relative.IsEmpty)
                {
                    var rule = Effective(relative).TypeRule!;

                    // Section 16.6: "Applying type=ignore to the concrete selector root is a
                    // blocking type error because it would leave an output instance without a root
                    // value. Use output=ignore to suppress that instance."
                    report(
                        DiagnosticCodes.Type001(
                            DiagnosticPhase.Planning,
                            "\u00A716.6",
                            $"'{rule.Declaration.Text}' ignores the concrete selector root "
                            + $"'{CanonicalPath.Of(prefix) ?? string.Empty}', which would leave the "
                            + "output instance without a root value. Use 'output=ignore' to "
                            + "suppress the instance.",
                            cardinalityKey: $"{rule.Declaration.Source}:{rule.Declaration.Line}",
                            source: rule.Declaration.Source,
                            line: rule.Declaration.Line,
                            path: CanonicalPath.Of(prefix),
                            declaration: rule.Declaration.Text),
                        rule.Order);

                    return node;
                }

                return null;
            }

            // Null from Rebuild means "no child changed"; null from this pass means "removed".
            // Returning Rebuild's null directly would delete every node whose subtree was untouched.
            return Rebuild(node, relative, Ignore) ?? node;
        }

        /// <summary>Step 16's third kind: <c>type=array</c> and <c>type=mapping</c>.</summary>
        /// <remarks>
        /// Descendants are transformed before the node itself, so that a rule bound to a child's
        /// pre-transformation path is still looked up under that path. Converting first would
        /// address the same child as <c>0</c> and lose its rule.
        /// </remarks>
        public OverlayNode Shape(OverlayNode node, ImmutableArray<NamePart> relative)
        {
            var rebuilt = Rebuild(node, relative, Shape) ?? node;

            return Effective(relative).Types switch
            {
                { IsArray: true } => ToSequence(rebuilt, relative),
                { IsMapping: true } => ToMapping(rebuilt, relative),
                _ => rebuilt,
            };
        }

        /// <summary>Step 16's fourth kind: <c>multiline</c>.</summary>
        public OverlayNode Multiline(OverlayNode node, ImmutableArray<NamePart> relative)
        {
            var rebuilt = Rebuild(node, relative, Multiline) ?? node;

            return Effective(relative).Types is { IsMultiline: true }
                ? Join(rebuilt, relative)
                : rebuilt;
        }

        /// <summary>Step 16's fifth kind: <c>key</c>.</summary>
        /// <remarks>
        /// Children are transformed before the node itself, because Section 16.5 builds a record
        /// out of "those mapping fields" as they finally stand. A nested <c>key</c> under a
        /// <c>key</c> must therefore already have produced its own records.
        /// </remarks>
        public OverlayNode Key(OverlayNode node, ImmutableArray<NamePart> relative)
        {
            var rebuilt = Rebuild(node, relative, Key) ?? node;

            return Effective(relative).Key is { } field
                ? ToRecords(rebuilt, relative, field)
                : rebuilt;
        }

        private EffectiveTransform Effective(ImmutableArray<NamePart> relative) =>
            effective.TryGetValue(CanonicalPath.Of(relative) ?? string.Empty, out var found)
                ? found
                : default;

        /// <summary>Section 16.6 <c>array</c>: force the winning container to a sequence.</summary>
        private OverlayNode ToSequence(OverlayNode node, ImmutableArray<NamePart> relative)
        {
            if (node.Marks.ContainerIsSequence)
            {
                // "If the winning container is already a sequence, retain it." A losing mapping
                // projection "remains omitted under Section 4.4" — it is already omitted, and
                // Section 4.4 rather than this pass is what omits it.
                return node;
            }

            if (!node.Marks.ContainerIsMapping)
            {
                Refuse(TypeRule(relative), relative, "\u00A716.6", "'type=array' needs a container projection to "
                    + "convert, and this path has none.");
                return node;
            }

            var children = node.OrderedChildren.ToList();

            // Section 16.6: "if every surviving key is an in-range canonical ordering value, items
            // are ordered numerically and retain explicit ordering provenance; otherwise every key
            // is discarded and every child — including in-range numeric and out-of-range decimal
            // children — receives a fresh implicit ordering value above the node's current
            // high-water mark in current mapping order."
            var numbered = children.All(child => OrderingValues.TryRead(child.Key, out _));

            // The converted node starts from an empty sequence rather than from the node's own.
            // Any items already there belong to the losing sequence projection, and Section 16.6
            // says a losing projection "is never merged into the converted result".
            var sequence = ImmutableDictionary<long, SequenceItem>.Empty;
            var allocator = SequenceOrderingAllocator.From(node.SequenceHighWater);

            foreach (var (name, child) in children)
            {
                if (numbered)
                {
                    OrderingValues.TryRead(name, out var explicitValue);
                    sequence = sequence.SetItem(explicitValue, SequenceItem.Numbered(child));
                    continue;
                }

                if (!allocator.TryAllocate(out var value))
                {
                    Refuse(TypeRule(relative), relative, "\u00A75.4", "'type=array' ran out of ordering values while "
                        + "converting this mapping.");
                    return node;
                }

                // The key this discards is the address a directive bound beneath the child still
                // uses, so Section 15.1 has the child's descendants follow it to the ordering
                // value. The numbered branch above needs no record: an in-range canonical ordering
                // value is spelled the same as the item it becomes.
                Record(relative, name, [OrderingValues.ToNamePart(value)]);
                sequence = sequence.SetItem(value, SequenceItem.Native(child));
            }

            return OverlayNode.Compose(
                // The same mark change Section 8.7 inference makes, made explicitly: the mapping
                // projection is consumed rather than left as a loser, so its shape-mark becomes the
                // sequence's and the node projects one container.
                node.Marks.AsInferredSequence(),
                node.Payload,
                hasExplicitMapping: false,
                hasExplicitSequence: true,
                ImmutableDictionary<NamePart, OverlayNode>.Empty,
                sequence,
                node.Comments,
                Math.Max(node.SequenceHighWater, allocator.HighWaterMark));
        }

        /// <summary>Section 16.6 <c>mapping</c>: force the winning container to a mapping.</summary>
        private OverlayNode ToMapping(OverlayNode node, ImmutableArray<NamePart> relative)
        {
            if (node.Marks.ContainerIsMapping)
            {
                return node;
            }

            if (!node.Marks.ContainerIsSequence)
            {
                Refuse(TypeRule(relative), relative, "\u00A716.6", "'type=mapping' needs a container projection to "
                    + "convert, and this path has none.");
                return node;
            }

            var children = ImmutableDictionary<NamePart, OverlayNode>.Empty;

            // Section 16.6: "a sequence projection becomes a mapping whose keys are the stable
            // ordering values rendered as canonical decimal strings", and "gaps and nonzero bases
            // are preserved as mapping keys" — which follows from keying by the ordering value
            // itself rather than by the item's position among its siblings.
            foreach (var (value, item) in node.OrderedSequence)
            {
                children = children.SetItem(OrderingValues.ToNamePart(value), item.Node);
            }

            return OverlayNode.Compose(
                node.Marks.AsForcedMapping(),
                node.Payload,
                hasExplicitMapping: true,
                hasExplicitSequence: false,
                children,
                ImmutableDictionary<long, SequenceItem>.Empty,
                node.Comments,
                // Section 5.4 never lowers the high-water mark, and converting away from a sequence
                // is not a reason to start: type=mapping followed by type=array must not reuse an
                // ordering value the first sequence had allocated.
                node.SequenceHighWater);
        }

        /// <summary>Section 16.6 <c>multiline</c>: join a sequence of scalar lines with LF.</summary>
        private OverlayNode Join(OverlayNode node, ImmutableArray<NamePart> relative)
        {
            if (!node.Marks.ContainerIsSequence)
            {
                // Section 16.6: "a lone scalar is a one-line value and is unchanged". A mapping
                // with no sequence projection, and a node with neither, are both TYPE001.
                if (node.Payload is not null)
                {
                    return node;
                }

                Refuse(TypeRule(relative), relative, "\u00A716.6", "'multiline' needs a sequence projection to join or "
                    + "a lone scalar to leave alone, and this path has neither.");
                return node;
            }

            var lines = new List<string>();

            foreach (var (_, item) in node.OrderedSequence)
            {
                // Section 16.6: "a nonempty sequence must contain only scalar or null payloads;
                // null contributes an empty line", and "a sequence item having only container
                // shape" is TYPE001.
                if (item.Node.Payload is not { } payload)
                {
                    Refuse(TypeRule(relative), relative, "\u00A716.6", "'multiline' met a sequence item that has only "
                        + "container shape, and a joined line must be a scalar or null.");
                    return node;
                }

                lines.Add(payload.IsNull ? string.Empty : payload.ToCanonicalText());
            }

            // An empty sequence joins no lines and becomes the empty string, which is what Section
            // 16.6 asks for without a separate case. The joined value replaces the sequence, so the
            // sequence's own shape-mark is the payload's: it is the same contribution, rewritten.
            return OverlayNode.Compose(
                NodeMarks.ForPayload(node.Marks.SequenceShape ?? node.Marks.Position),
                ScalarPayload.OfString(string.Join('\n', lines)),
                hasExplicitMapping: false,
                hasExplicitSequence: false,
                ImmutableDictionary<NamePart, OverlayNode>.Empty,
                ImmutableDictionary<long, SequenceItem>.Empty,
                node.Comments,
                node.SequenceHighWater);
        }

        /// <summary>Section 16.5 <c>key</c>: an ordered mapping becomes a sequence of records.</summary>
        private OverlayNode ToRecords(
            OverlayNode node,
            ImmutableArray<NamePart> relative,
            string field)
        {
            if (!node.Marks.ContainerIsMapping)
            {
                // Section 16.5: "The target must be an ordered mapping. ... If no mapping
                // projection exists, it is a blocking type error", and "applying key to a
                // sequence-only or scalar-only target is TYPE001".
                Refuse(KeyRule(relative), relative, "\u00A716.5", $"'key={field}' needs an ordered mapping, and this "
                    + "path projects a sequence or a scalar only.");
                return node;
            }

            var name = new OrdinaryPart([new LiteralToken(field)]);
            var sequence = ImmutableDictionary<long, SequenceItem>.Empty;
            var allocator = SequenceOrderingAllocator.From(node.SequenceHighWater);

            foreach (var (child, value) in node.OrderedChildren)
            {
                if (!TryRecord(child, value, name, field, relative, out var record))
                {
                    return node;
                }

                // Section 16.5 puts a child with no mapping projection under 'value' and leaves a
                // mapping child's fields at the top of its record, so the two shapes move the
                // child's own path to different depths.
                var wrapped = !value.Marks.ContainerIsMapping;

                // Section 16.5: "A child already carrying ordering-value provenance retains that
                // value and provenance through record construction. A child without ordering
                // provenance receives a fresh implicit ordering value in mapping order."
                if (OrderingValues.TryRead(child, out var ordering))
                {
                    if (wrapped)
                    {
                        Record(relative, child, [OrderingValues.ToNamePart(ordering), ValueField]);
                    }

                    sequence = sequence.SetItem(ordering, SequenceItem.Numbered(record));
                    continue;
                }

                if (!allocator.TryAllocate(out var allocated))
                {
                    Refuse(KeyRule(relative), relative, "\u00A75.4", $"'key={field}' ran out of ordering values.");
                    return node;
                }

                Record(
                    relative,
                    child,
                    wrapped
                        ? [OrderingValues.ToNamePart(allocated), ValueField]
                        : [OrderingValues.ToNamePart(allocated)]);
                sequence = sequence.SetItem(allocated, SequenceItem.Native(record));
            }

            return OverlayNode.Compose(
                // Section 16.5: "The transformed mapping becomes a sequence contribution carrying
                // the mapping projection's source mark."
                node.Marks.AsInferredSequence(),
                // "key operates on the mapping projection of an overlay and leaves any independent
                // scalar payload available to formats that can represent both."
                node.Payload,
                hasExplicitMapping: false,
                hasExplicitSequence: true,
                ImmutableDictionary<NamePart, OverlayNode>.Empty,
                sequence,
                // Comments bound to this node stay here; Section 16.5 moves only the comments bound
                // to each child, and those travel inside the record.
                node.Comments,
                Math.Max(node.SequenceHighWater, allocator.HighWaterMark));
        }

        /// <summary>Builds one Section 16.5 record from one mapping child.</summary>
        private bool TryRecord(
            NamePart child,
            OverlayNode value,
            NamePart field,
            string spelling,
            ImmutableArray<NamePart> relative,
            out OverlayNode record)
        {
            record = value;

            if (value.Children.ContainsKey(field))
            {
                // Section 16.5: "if the child already contains that field name, processing is
                // blocking TYPE001".
                Refuse(KeyRule(relative), relative, "\u00A716.5", $"the child '{Decoded(child)}' already contains a "
                    + $"field named '{spelling}', and the generated key field would replace it.");
                return false;
            }

            var independent = value.Payload is not null || !value.Sequence.IsEmpty
                || value.HasExplicitSequence;

            if (value.Marks.ContainerIsMapping)
            {
                // "A child with a mapping projection becomes a record containing those mapping
                // fields", plus "every independent scalar, null, or sequence projection of that
                // child is placed under a field named 'value'".
                record = independent
                    ? Detached(value).WithChild(ValueField, Independent(value))
                    : value;

                if (independent && value.Children.ContainsKey(ValueField))
                {
                    Refuse(KeyRule(relative), relative, "\u00A716.5", $"the child '{Decoded(child)}' already contains "
                        + "a field named 'value', which its independent scalar or sequence "
                        + "projection would have to take.");
                    return false;
                }
            }
            else
            {
                // "A child without a mapping projection becomes a record containing the generated
                // key field followed by 'value' holding the complete child overlay."
                record = OverlayNode.Empty(NodeMarks.At(value.Marks.Position))
                    .WithChild(ValueField, value);
            }

            // "The generated key field is inserted first as a string scalar containing the decoded
            // mapping-key text; scalar inference is never applied to this generated field." First
            // means first in Section 5.2 mapping order, and the earliest possible position mark is
            // what puts it there regardless of the name the scheme chose.
            record = record.WithChild(
                field,
                OverlayNode.OfPayload(
                    ScalarPayload.OfString(DecodedText(child)),
                    StableOrderingKey.First));

            return true;
        }

        /// <summary>The child's mapping projection alone, with its independent facets removed.</summary>
        private static OverlayNode Detached(OverlayNode value) =>
            OverlayNode.Compose(
                value.Marks.AsForcedMapping(),
                null,
                hasExplicitMapping: true,
                hasExplicitSequence: false,
                value.Children,
                ImmutableDictionary<long, SequenceItem>.Empty,
                // Section 16.5: "comments bound to the original child path move with the complete
                // generated record", so they stay on the record and not on its 'value' field.
                value.Comments,
                value.SequenceHighWater);

        /// <summary>The child's independent scalar, null, or sequence projection alone.</summary>
        private static OverlayNode Independent(OverlayNode value) =>
            OverlayNode.Compose(
                value.Marks.WithoutMapping(),
                value.Payload,
                hasExplicitMapping: false,
                hasExplicitSequence: value.HasExplicitSequence,
                ImmutableDictionary<NamePart, OverlayNode>.Empty,
                value.Sequence,
                ImmutableList<BoundComment>.Empty,
                value.SequenceHighWater);

        private static readonly NamePart ValueField =
            new OrdinaryPart([new LiteralToken("value")]);

        private static string Decoded(NamePart part) =>
            CanonicalPath.Of([part]) ?? string.Empty;

        private static string DecodedText(NamePart part) => part switch
        {
            OrdinaryPart ordinary when ordinary.LiteralText is { } text => text,
            _ => Decoded(part),
        };

        /// <summary>The Section 22 cardinality key of one refusal.</summary>
        /// <param name="rule">The rule that could not be applied.</param>
        /// <param name="instance">The concrete selector of the output instance it was applied in.</param>
        /// <param name="absolute">The absolute path it was applied at.</param>
        /// <returns>A key that separates exactly the occurrences Section 22 counts separately.</returns>
        /// <remarks>
        /// <para>
        /// Section 22 counts TYPE001 "once per path and applicable source/output instance". Section
        /// 15.2 evaluates <c>type</c> and <c>key</c> "against absolute stable pre-transformation
        /// paths in every output instance containing the path", and Section 14.1 lets nested output
        /// declarations "intentionally select overlapping data and create duplicate content in
        /// separate files", so one rule at one path can legitimately be refused twice. Those are two
        /// facts about two files, and a key omitting the instance would report the first and retire
        /// the second, leaving a run that names one of the files it failed to produce.
        /// </para>
        /// <para>
        /// The instance is identified by the concrete selector it was created from, which is this
        /// view's prefix. Section 14.1 makes the selector what creates an instance, so the two
        /// formats of <c>output=namespace,ini</c> share one and are refused once, which is also how
        /// PATH001 treats them.
        /// </para>
        /// <para>
        /// The parts are length-prefixed rather than joined by a separator. A canonical path escapes
        /// only the delimiter, <c>=</c>, <c>}</c>, <c>*</c> and line terminators, so any separator
        /// can also occur inside a part, and a key is a suppression rule: two keys that collide
        /// cost a diagnostic.
        /// </para>
        /// </remarks>
        private static string CardinalityKey(TransformRule rule, string? instance, string? absolute)
        {
            string?[] parts = [rule.Declaration.Source, $"{rule.Declaration.Line}", instance, absolute];

            return string.Concat(parts.Select(part => $"{part?.Length ?? -1}\u0000{part}"));
        }

        private void Refuse(
            TransformRule rule,
            ImmutableArray<NamePart> relative,
            string spec,
            string message)
        {
            var absolute = CanonicalPath.Of(prefix.AddRange(relative));

            report(
                DiagnosticCodes.Type001(
                    DiagnosticPhase.Planning,
                    spec,
                    $"'{rule.Declaration.Text}' cannot be applied at '{absolute}': {message}",
                    cardinalityKey: CardinalityKey(rule, CanonicalPath.Of(prefix), absolute),
                    source: rule.Declaration.Source,
                    line: rule.Declaration.Line,
                    path: absolute,
                    declaration: rule.Declaration.Text),
                rule.Order);
        }

        private TransformRule TypeRule(ImmutableArray<NamePart> relative) =>
            Effective(relative).TypeRule!;

        private TransformRule KeyRule(ImmutableArray<NamePart> relative) =>
            Effective(relative).KeyRule!;

        /// <summary>Rebuilds a node from its transformed children, or returns null if unchanged.</summary>
        private static OverlayNode? Rebuild(
            OverlayNode node,
            ImmutableArray<NamePart> relative,
            Func<OverlayNode, ImmutableArray<NamePart>, OverlayNode?> transform)
        {
            var rebuilt = node;
            var changed = false;

            foreach (var (name, child) in node.OrderedChildren)
            {
                var next = transform(child, relative.Add(name));

                if (ReferenceEquals(next, child))
                {
                    continue;
                }

                changed = true;
                rebuilt = next is null ? rebuilt.WithoutChild(name) : rebuilt.WithChild(name, next);
            }

            foreach (var (value, item) in node.OrderedSequence)
            {
                var name = OrderingValues.ToNamePart(value);

                if (node.Children.ContainsKey(name))
                {
                    continue;
                }

                var next = transform(item.Node, relative.Add(name));

                if (ReferenceEquals(next, item.Node))
                {
                    continue;
                }

                changed = true;
                rebuilt = next is null
                    ? rebuilt.WithoutSequenceItem(value)
                    : rebuilt.WithSequenceItem(value, item with { Node = next });
            }

            return changed ? rebuilt : null;
        }
    }
}
