using System.Collections.Immutable;
using Namespace2Xml.Budgets;
using Namespace2Xml.Cli;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Overlay;
using Namespace2Xml.Profiles;
using Namespace2Xml.Scalars;
using Namespace2Xml.Scheme;

namespace Namespace2Xml.Pipeline.Steps;

/// <summary>One input source, read into the parts Section 15.1 steps 5 and 7 separate.</summary>
/// <param name="Origin">How diagnostics name it.</param>
/// <param name="Contribution">Its parsed contribution.</param>
public sealed record InputContribution(ProfileSource Origin, ProfileContribution Contribution);

/// <summary>
/// Specification Section 15.1 steps 5 to 12: the input phase.
/// </summary>
/// <remarks>
/// Steps 6 and 7 do not appear as separate methods because
/// <see cref="NamespaceProfileReader"/> performs them as it reads: it lexes every value, which is
/// step 6, and returns templates and masks separately from concrete contributions, which is step 7.
/// Splitting them out would mean re-walking the records to assert work already done. The driver
/// still runs them as steps so that the order remains checkable, and their bodies are the identity.
/// </remarks>
public static class InputPhase
{
    /// <summary>Section 15.1 step 5: parse input documents into typed overlays.</summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="loader">The shared source loader.</param>
    /// <param name="budget">The invocation's global budget, already charged for scheme sources.</param>
    /// <param name="options">Step 2's product: the Section 16.8 input options.</param>
    /// <param name="substitutes">Step 3's product: the Section 16.7 mode at each declared path.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>Every admitted input contribution, in Section 7.3 stream order.</returns>
    public static StepOutcome<ImmutableArray<InputContribution>> ParseInputs(
        CommandLine command,
        SourceLoader loader,
        GlobalBudget budget,
        InputOptions options,
        SubstituteModeMap substitutes,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(substitutes);
        ArgumentNullException.ThrowIfNull(diagnostics);

        // Section 7.1 lists '.json', '.yaml', '.yml' and '.xml' and routes every other extension,
        // including none at all, to namespace-profile parsing, so there is no such thing as an
        // input file in an unsupported format: SourceLoader.StructuredFormat returns one of those
        // three names or null, and null is a namespace profile. The arm that refused a fourth
        // format could not be reached and is gone.

        // Section 7.3 fixes one ordered stream for the whole invocation: scheme files, then input
        // files, then variables. Scheme ordinals were 0 through Schemes.Length - 1, so inputs
        // continue from there and the global budget sees the stream in the order it names.
        var next = (long)command.Schemes.Length;
        var loaded = ImmutableArray.CreateBuilder<LoadedSource>();

        foreach (var input in command.Inputs)
        {
            var source = loader.LoadFile(
                input, next++, DiagnosticPhase.Input, diagnostics, options, structured: true);

            if (source is not null)
            {
                loaded.Add(source);
            }
        }

        for (var i = 0; i < command.Variables.Length; i++)
        {
            var source = loader.LoadVariable(
                command.Variables[i], i + 1, next++, diagnostics);

            if (source is not null)
            {
                loaded.Add(source);
            }
        }

        var admitted = SourceLoader.Admit(loaded, budget, _ => DiagnosticPhase.Input, diagnostics);
        var contributions = ImmutableArray.CreateBuilder<InputContribution>();
        var reconciled = Reconciled(admitted);

        foreach (var source in admitted)
        {
            if (!source.Admitted)
            {
                continue;
            }

            if (source.Document is { } document)
            {
                var native = StructuredProfileReader.Read(
                    reconciled.GetValueOrDefault(source.Ordinal, document),
                    source.Ordinal,
                    source.Origin,
                    substitutes,
                    diagnostics,
                    nativeMappings: source.Format is "JSON" or "YAML");

                contributions.Add(new InputContribution(source.Origin, native));
                continue;
            }

            contributions.Add(
                new InputContribution(
                    source.Origin,
                    NamespaceProfileReader.Read(
                        source.Records,
                        source.Ordinal,
                        source.Origin,
                        substitutes,
                        diagnostics)));
        }

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<ImmutableArray<InputContribution>>()
            : StepOutcome.Produced(contributions.ToImmutable());
    }

    /// <summary>Section 11.4's classification of the XML documents this run admitted.</summary>
    /// <param name="admitted">Every source, in source order.</param>
    /// <returns>
    /// The reconciled document of each XML source whose shape the merged classification changes.
    /// A source absent from the map keeps the document its reader built.
    /// </returns>
    /// <remarks>
    /// Section 11.4 evaluates mixedness and repeated-child classification "at concrete merge time
    /// across all input contributions to that element", and requires the result before addresses
    /// are exposed, since they are "never recomputed for an output view". The reconciliation
    /// therefore sits between reading and Section 15.1 step 6's projection rather than in the
    /// merge, which by then sees paths and not shapes.
    /// </remarks>
    private static Dictionary<long, StructuredNode> Reconciled(
        ImmutableArray<LoadedSource> admitted)
    {
        var sources = admitted
            .Where(source => source is
            {
                Admitted: true, Format: "XML", Document: not null,
            })
            .ToList();

        if (sources.Count < 2)
        {
            return [];
        }

        var documents = XmlClassification.Reconcile([.. sources.Select(source => source.Document!)]);
        var reconciled = new Dictionary<long, StructuredNode>();

        for (var i = 0; i < sources.Count; i++)
        {
            reconciled[sources[i].Ordinal] = documents[i];
        }

        return reconciled;
    }

    /// <summary>Section 15.1 step 8: fold within each contribution, then merge in source order.</summary>
    /// <param name="contributions">Step 5's product.</param>
    /// <param name="configuration">Step 4's product, whose merge directives govern the fold.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The merged overlay.</returns>
    public static StepOutcome<OverlayNode> MergeContributions(
        ImmutableArray<InputContribution> contributions,
        SchemeConfiguration configuration,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var merger = new OverlayMerger(
            DeclaredMerges(configuration),
            diagnostics,
            sourceCompatibility: DeclaredMerges(configuration));
        var mask = MasksOf(contributions);

        var merged = merger.MergeAll(
            contributions.Select(c => mask.Apply(c.Contribution.Overlay)));

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<OverlayNode>()
            : StepOutcome.Produced(merged);
    }

    /// <summary>The Section 16.10 strategy map the scheme's input <c>merge</c> directives declare.</summary>
    /// <param name="configuration">Step 4's product.</param>
    /// <returns>The map, which also answers which paths carry a directive at all.</returns>
    public static MergeStrategyMap DeclaredMerges(SchemeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var paths = configuration.InputMerges
            .Where(merge => merge.Path is not null)
            .Select(merge => KeyValuePair.Create(merge.Path!, merge.Strategy));

        var rootMerges = configuration.InputMerges
            .Where(merge => merge.Path is null)
            .Select(merge => merge.Strategy)
            .ToList();

        return MergeStrategyMap.Create(
            paths,
            rootMerges.Count == 0 ? MergeStrategy.Deep : rootMerges[^1],
            rootDeclared: rootMerges.Count > 0);
    }

    /// <summary>The union of every Section 8.6 mask the run declares.</summary>
    /// <param name="contributions">Step 5's product.</param>
    /// <returns>The run-wide mask.</returns>
    /// <remarks>
    /// Assembled across all sources before any is pruned. Section 8.6 suppresses a matching
    /// contribution "regardless of whether it appears before or after the ignore entry", so a mask
    /// in the last input has to reach a path contributed by the first.
    /// </remarks>
    public static ExclusionMask MasksOf(ImmutableArray<InputContribution> contributions) =>
        ExclusionMask.Of(
            contributions.SelectMany(c => c.Contribution.Masks).Select(m => m.Pattern));

    /// <summary>Section 15.1 step 9: expose ordering values and numeric mapping keys as path parts.</summary>
    /// <param name="merged">Step 8's product.</param>
    /// <param name="configuration">Step 4's product, whose merge directives govern Section 8.7's warning.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The overlay with ordering values exposed.</returns>
    /// <remarks>
    /// The merger folding a numeric mapping child into its twin sequence item carries no Section
    /// 16.10 strategies: this is not a literal-path merge but the step that makes the two addresses
    /// one node. It does report Section 8.7's compatibility warning, because this fold is where two
    /// sources' native arrays first meet when one wrote the containing item and the other wrote the
    /// numeric key.
    /// </remarks>
    public static StepOutcome<OverlayNode> ExposeOrderingValues(
        OverlayNode merged,
        SchemeConfiguration configuration,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(merged);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var exposer = new OrderingValueExposer(
            new OverlayMerger(
                MergeStrategyMap.Create([]),
                diagnostics,
                sourceCompatibility: DeclaredMerges(configuration)));

        var exposed = exposer.Expose(merged);

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<OverlayNode>()
            : StepOutcome.Produced(exposed);
    }

    /// <summary>Section 15.1 step 10: evaluate templates under the permanent exclusion masks.</summary>
    /// <param name="exposed">Step 9's product.</param>
    /// <param name="contributions">Step 5's product, which carries the templates and masks.</param>
    /// <param name="configuration">Step 4's product, whose merge directives govern each generated contribution.</param>
    /// <param name="budget">The invocation's global budget, which Section 23 charges this step against.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The overlay including every generated contribution.</returns>
    /// <remarks>
    /// The masks are applied again here rather than only at step 8. Step 9 turns ordering values
    /// and numeric mapping keys into path parts, so a path spelled with an ordering value first
    /// becomes reachable by a mask at this step; and Section 8.6 keeps masks "active throughout
    /// wildcard fixed-point evaluation", applying them "to every candidate when it appears".
    /// </remarks>
    public static StepOutcome<OverlayNode> EvaluateTemplates(
        OverlayNode exposed,
        ImmutableArray<InputContribution> contributions,
        SchemeConfiguration configuration,
        GlobalBudget budget,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(exposed);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var mask = MasksOf(contributions);
        var evaluator = new WildcardEvaluator(
            WildcardEvaluator.Validate(RulesOf(contributions), diagnostics),
            mask,
            new OverlayMerger(DeclaredMerges(configuration), diagnostics),
            budget,
            diagnostics);

        // Section 8.6 discards masked contributions before step 8 merges, so the pairs a successful
        // mask checked are already absent from what this step evaluates. They are charged from the
        // unmasked contributions first; the evaluator carries one considered set across both
        // passes, so Section 12.4's third condition still holds.
        if (!evaluator.ChargeMaskedCandidates(contributions.Select(c => c.Contribution.Overlay)))
        {
            return StepOutcome.Failed<OverlayNode>();
        }

        var evaluated = evaluator.Evaluate(mask.Apply(exposed));

        return diagnostics.HasBlockingError
            ? StepOutcome.Failed<OverlayNode>()
            : StepOutcome.Produced(evaluated);
    }

    /// <summary>The run's Section 12 wildcard rules, in the Section 12.4 worklist order.</summary>
    /// <param name="contributions">Step 5's product.</param>
    /// <returns>Every generative template and every wildcard-bearing mask, in source order.</returns>
    /// <remarks>
    /// Section 12.4 puts "all templates from all sources" in "one deterministic worklist ordered by
    /// source order", and counts permanent wildcard ignore masks among the categories that consume
    /// the shared candidate-check limit, so both kinds enter the same list. A mask whose pattern
    /// contains no wildcard is not a wildcard rule and is left out: it is an ordinary literal-path
    /// predicate that has already done its work at step 8.
    /// </remarks>
    private static ImmutableArray<WildcardRule> RulesOf(
        ImmutableArray<InputContribution> contributions)
    {
        var rules = ImmutableArray.CreateBuilder<WildcardRule>();

        foreach (var contribution in contributions)
        {
            foreach (var template in contribution.Contribution.Templates)
            {
                rules.Add(new WildcardRule(
                    template.Name,
                    template.Value,
                    template.Order,
                    template.Comments,
                    contribution.Origin.File,
                    contribution.Origin.Identity,
                    template.Line));
            }

            foreach (var declared in contribution.Contribution.Masks)
            {
                if (QualifiedNameLexer.ContainsWildcard(declared.Pattern))
                {
                    rules.Add(new WildcardRule(
                        declared.Pattern,
                        null,
                        declared.Order,
                        [],
                        contribution.Origin.File,
                        contribution.Origin.Identity,
                        declared.Line));
                }
            }
        }

        rules.Sort((left, right) => left.Order.CompareTo(right.Order));

        return rules.ToImmutable();
    }

    /// <summary>Section 15.1 step 11: project inferable mappings as explicit indexed sequences.</summary>
    /// <param name="evaluated">Step 10's product.</param>
    /// <returns>The overlay with every inferable mapping projected.</returns>
    /// <remarks>
    /// <para>
    /// The walk is a whole-tree one rather than a per-source one, because Section 8.7 infers over
    /// the merged model: <c>a.0=x</c> in one file and <c>a.1=y</c> in another form one inferable
    /// mapping that neither source can see alone.
    /// </para>
    /// <para>
    /// Projection is bottom-up, but inferability is not: a mapping qualifies on its children's
    /// names, which projecting those children cannot change. The order therefore only fixes which
    /// node object the two facets share.
    /// </para>
    /// <para>
    /// Section 15.1 makes a numeric mapping child and the sequence item at its value "one
    /// structural overlay node", and the walk reuses the projected child for its twin item rather
    /// than projecting the same subtree twice. No step downstream of this one can currently tell
    /// the two apart -- projection is pure, so a second projection of the same node is equal to the
    /// first, and a mutation that re-projects survives every test in the suite. It is kept for
    /// cost rather than for observable behaviour: after step 9 every numeric child shares a node
    /// with an item, so re-projecting doubles the work at each such level.
    /// </para>
    /// </remarks>
    public static StepOutcome<OverlayNode> InferSequences(OverlayNode evaluated)
    {
        ArgumentNullException.ThrowIfNull(evaluated);

        return StepOutcome.Produced(Project(evaluated));
    }

    private static OverlayNode Project(OverlayNode node)
    {
        var children = node.Children;

        foreach (var (name, child) in node.OrderedChildren)
        {
            children = children.SetItem(name, Project(child));
        }

        var sequence = node.Sequence;

        foreach (var (value, item) in node.OrderedSequence)
        {
            sequence = sequence.SetItem(
                value,
                item with
                {
                    Node = node.Children.TryGetValue(OrderingValues.ToNamePart(value), out var twin)
                        && ReferenceEquals(twin, item.Node)
                            ? children[OrderingValues.ToNamePart(value)]
                            : Project(item.Node),
                });
        }

        var inferred = IsInferable(node);

        if (inferred)
        {
            foreach (var (name, child) in children)
            {
                // Section 8.7: "explicit indexed contributions patch the sequence at their supplied
                // ordering values", so an item already at the value has its node replaced rather
                // than being displaced. Section 15.1 has the combined item keep "the ordering
                // provenance the sequence item already had"; only step 8's merge reads provenance,
                // and it has already run, so preserving it here is specified rather than observed.
                // Section 5.4 gives gaps and nonzero bases no special treatment, and a value
                // nothing supplied stays absent rather than becoming a null placeholder.
                OrderingValues.TryRead(name, out var value);

                sequence = sequence.SetItem(
                    value,
                    sequence.TryGetValue(value, out var existing)
                        ? existing with { Node = child }
                        : SequenceItem.Numbered(child));
            }

            children = ImmutableDictionary<NamePart, OverlayNode>.Empty;
        }

        return OverlayNode.Compose(
            inferred ? node.Marks.AsInferredSequence() : node.Marks,
            node.Payload,
            // Section 15.1: "inference replaces that contribution's mapping projection". The node
            // is a sequence afterwards, so the mapping facet it replaced must not still claim one.
            node.HasExplicitMapping && !inferred,
            node.HasExplicitSequence || inferred,
            children,
            sequence,
            node.Comments,
            node.SequenceHighWater);
    }

    /// <summary>Section 15.1 step 12: infer scalar kinds for remaining untyped payloads.</summary>
    /// <param name="projected">Step 11's product.</param>
    /// <returns>The overlay with every untyped payload settled.</returns>
    public static StepOutcome<OverlayNode> InferScalarKinds(OverlayNode projected)
    {
        ArgumentNullException.ThrowIfNull(projected);

        return StepOutcome.Produced(Settle(projected));
    }

    private static OverlayNode Settle(OverlayNode node)
    {
        var settled = node;

        if (node.Payload is { IsUntyped: true } payload)
        {
            settled = settled.WithPayload(
                ScalarInference.Infer(payload), node.Marks.PayloadMark ?? node.Marks.Position);
        }

        foreach (var (name, child) in node.OrderedChildren)
        {
            var inferred = Settle(child);

            if (!ReferenceEquals(inferred, child))
            {
                settled = settled.WithChild(name, inferred);
            }
        }

        foreach (var (ordering, item) in node.OrderedSequence)
        {
            var inferred = Settle(item.Node);

            if (!ReferenceEquals(inferred, item.Node))
            {
                settled = settled.WithSequenceItem(ordering, item with { Node = inferred });
            }
        }

        return settled;
    }

    /// <summary>
    /// Section 8.7: a mapping is sequence-inferable when it is nonempty and every surviving child
    /// name is a canonical ordering value.
    /// </summary>
    /// <remarks>
    /// "A surviving empty mapping remains a mapping", so emptiness is checked rather than left to
    /// the vacuous truth of the universal quantifier. Masks have already run, at steps 8 and 10, so
    /// the children seen here are the surviving ones the clause asks about.
    /// </remarks>
    private static bool IsInferable(OverlayNode node) =>
        !node.Children.IsEmpty && node.Children.Keys.All(IsCanonicalIndex);

    /// <summary>
    /// Section 8.7's canonical nonnegative decimal ordering value: <c>0</c> or a nonzero digit
    /// followed by decimal digits, with a value of at most 9,223,372,036,854,775,807.
    /// </summary>
    /// <remarks>
    /// Both exclusions are stated as preventing sequence interpretation, so returning
    /// <see langword="false"/> for a leading-zero or out-of-range spelling makes the whole mapping
    /// an ordinary one, which is what Section 8.7 asks for.
    /// </remarks>
    private static bool IsCanonicalIndex(NamePart part) =>
        part is OrdinaryPart { LiteralText: { } text }
        && text.Length > 0
        && (text.Length == 1 || text[0] != '0')
        && text.All(c => c is >= '0' and <= '9')
        && long.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out _);

    private static string Spell(NamePart part) =>
        part is OrdinaryPart { LiteralText: { } text } ? text : part.ToString();
}
