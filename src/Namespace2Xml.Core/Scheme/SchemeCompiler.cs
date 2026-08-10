using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
using Namespace2Xml.Inputs;
using Namespace2Xml.Output;
using Namespace2Xml.Overlay;
using Namespace2Xml.Pipeline;
using Namespace2Xml.Profiles;

namespace Namespace2Xml.Scheme;

/// <summary>
/// A selector used as a dictionary key, including the root selector.
/// </summary>
/// <param name="Name">The selector, or <see langword="null"/> for the root.</param>
/// <remarks>
/// Appendix A.2 spells a qualified name as one or more components, so the root selector cannot be
/// an empty <see cref="QualifiedName"/>, and a dictionary cannot take a null key. Wrapping both in
/// one value keeps Section 15.2's "one source-ordered override stream" per selector expressible as
/// a single lookup.
/// </remarks>
public readonly record struct SelectorKey(QualifiedName? Name)
{
    /// <summary>The root selector, which Section 16.2 names <c>output</c> by default.</summary>
    public static SelectorKey Root => default;

    /// <inheritdoc/>
    /// <remarks>
    /// Section 17.5 compares the UTF-8 bytes of this text as the fold order's final tie-breaker,
    /// and Section 6.4.3 puts it in a diagnostic's <c>path</c> member, so it must be the Section
    /// 19.1 encoding: only that spelling is injective, and a tie-breaker that can tie is not one.
    /// </remarks>
    public override string ToString() => CanonicalPath.Of(Name) ?? string.Empty;
}

/// <summary>
/// Where a scheme declaration was written, for the Section 22 diagnostic members that name it.
/// </summary>
/// <param name="Text">The written directive text.</param>
/// <param name="Source">The source name diagnostics report.</param>
/// <param name="Line">The one-based line it was written on.</param>
public readonly record struct DeclarationSite(string Text, string Source, int Line);

/// <summary>
/// One Section 15.2 concrete output instance and the configuration bound to it.
/// </summary>
/// <param name="Selector">The selector that produced the instance.</param>
/// <param name="Formats">
/// The Section 16.1 formats, in declaration order. Empty when <c>ignore</c> won, which suppresses
/// the instance without removing data from any other.
/// </param>
/// <param name="DeclarationOrder">Source order of the winning <c>output</c> declaration.</param>
/// <param name="FilenameTemplate">
/// The Section 16.2 relative path as written, or null for the default file names. It is a template
/// rather than text because Section 16.2 allows wildcard captures inside path segments, and the
/// captures are not known until Section 14.1 has expanded the selector.
/// </param>
/// <param name="Root">The Section 16.3 wrapping path, or null when content is not wrapped.</param>
/// <param name="Delimiter">The Section 16.4 delimiter, or null for each format's own default.</param>
/// <param name="IniOptions">The Section 16.9 INI options, defaulted when undeclared.</param>
/// <param name="JsonOptions">The Section 16.9 JSON options, defaulted when undeclared.</param>
/// <param name="YamlOptions">The Section 16.9 YAML options, defaulted when undeclared.</param>
/// <param name="XmlOptions">The Section 16.9 XML options, defaulted when undeclared.</param>
/// <param name="FileMerge">The Section 16.11 destination-collision strategy.</param>
/// <param name="Captures">
/// The Section 14.1 captures the selector expansion bound, empty for a selector that contained no
/// wildcard. Section 16.2 substitutes them into <see cref="FilenameTemplate"/>.
/// </param>
/// <param name="WildcardMatchOrder">
/// The instance's position among the concrete instances one wildcard declaration expanded into,
/// which is the third component of the Section 17.5 fold key. Zero for a literal declaration.
/// </param>
/// <param name="FilenameDeclaration">
/// The site of the winning <c>filename</c> declaration, or null when the instance takes a Section
/// 16.2 default name. A Section 16.2 or 21.1 rejection is a condition about <i>that</i>
/// declaration, not about the <c>output</c> declaration that created the instance, and Section 22
/// requires the member to name "one scheme declaration ... its canonical spelling".
/// </param>
/// <param name="FileMergeDeclaration">
/// The site of the winning <c>filemerge</c> declaration, or null when the instance takes the Section
/// 16.11 default. A <c>filemerge=error</c> rejection is a condition about <i>that</i> declaration:
/// the two rejected contributions may share one <c>output</c> declaration, so naming it would not
/// tell a reader which setting refused the fold.
/// </param>
/// <param name="IniOptionsDeclaration">
/// The site of the winning <c>inioutputoptions</c> declaration, or null when the instance takes the
/// Section 16.9 defaults. Unlike <c>filename</c>, <c>root</c> and <c>delimiter</c>, an INI option set
/// has no null value to mean "not declared" — a scheme may explicitly write the defaults — so the
/// Section 15.2 override stream needs the site to know whether this member won anything.
/// </param>
/// <param name="JsonOptionsDeclaration">
/// The site of the winning <c>jsonoutputoptions</c> declaration, or null when the instance takes the
/// Section 16.9 defaults, for the same reason as <paramref name="IniOptionsDeclaration"/>.
/// </param>
/// <param name="YamlOptionsDeclaration">
/// The site of the winning <c>yamloutputoptions</c> declaration, or null when the instance takes the
/// Section 16.9 defaults, for the same reason as <paramref name="IniOptionsDeclaration"/>.
/// </param>
/// <param name="XmlOptionsDeclaration">
/// The site of the winning <c>xmloutputoptions</c> declaration, or null when the instance takes the
/// Section 16.9 defaults, for the same reason as <paramref name="IniOptionsDeclaration"/>.
/// </param>
/// <param name="Declaration">
/// The written text of the winning <c>output</c> declaration, its source, and the line it was
/// written on. Section 22 supplies a diagnostic's <c>source</c>, <c>line</c>, and
/// <c>declaration</c> members when the condition has those facts, and a condition about a
/// declaration has all three; carrying them keeps that decision out of the hands of what this
/// record happens to hold.
/// </param>
public sealed record OutputInstance(
    SelectorKey Selector,
    ImmutableArray<OutputFormat> Formats,
    long DeclarationOrder,
    InterpretedValue? FilenameTemplate,
    QualifiedName? Root,
    string? Delimiter,
    IniOutputOptions IniOptions,
    JsonOutputOptions JsonOptions,
    YamlOutputOptions YamlOptions,
    XmlOutputOptions XmlOptions,
    MergeStrategy FileMerge,
    WildcardCaptures Captures,
    int WildcardMatchOrder,
    DeclarationSite? FilenameDeclaration,
    DeclarationSite? FileMergeDeclaration,
    DeclarationSite? IniOptionsDeclaration,
    DeclarationSite? JsonOptionsDeclaration,
    DeclarationSite? YamlOptionsDeclaration,
    DeclarationSite? XmlOptionsDeclaration,
    DeclarationSite Declaration)
{
    /// <summary>
    /// The Section 16.2 relative path with this instance's captures substituted, or null for the
    /// default file names.
    /// </summary>
    /// <remarks>
    /// Section 16.2 makes substituted text "opaque segment data", so this is the string the
    /// portable segment algorithm consumes and not a path that has already been split.
    /// </remarks>
    public string? Filename => FilenameTemplate is null
        ? null
        : WildcardSubstitution.Apply(FilenameTemplate, Captures);
}

/// <summary>
/// One Section 16.10 literal-path input merge directive, as compiled at pipeline step 4.
/// </summary>
/// <param name="Path">The path the strategy governs, or null when it governs the root.</param>
/// <param name="Strategy">The strategy.</param>
public sealed record InputMerge(QualifiedName? Path, MergeStrategy Strategy);

/// <summary>
/// One compiled Section 16.5 or 16.6 path-scoped transformation.
/// </summary>
/// <param name="Path">
/// The absolute stable pre-transformation path the rule matches, or null for the model root. It may
/// contain wildcards: Section 16.5 supports "wildcard-qualified <c>key</c> directives", and Section
/// 15.2 puts "exact and wildcard declarations" in one source-ordered override stream.
/// </param>
/// <param name="Types">The Section 16.6 type set, or null when this rule is a <c>key</c>.</param>
/// <param name="KeyField">
/// The Section 16.5 field name, or null when this rule is a <c>type</c>. It is a template rather
/// than text because Section 12.1 substitutes the captures the rule's own path bound, and those are
/// not known until the rule is matched against an absolute path at step 16.
/// </param>
/// <param name="Order">Source order, which Section 15.2 makes the only precedence.</param>
/// <param name="Declaration">Where the directive was written, for the Section 22 members.</param>
/// <remarks>
/// Section 15.2 evaluates these "independently against absolute stable pre-transformation paths in
/// every output instance containing the path", so unlike an <see cref="OutputInstance"/> setting a
/// rule carries no selector and is not bound to one instance. A rule that matches a path visible in
/// three instances transforms it in all three.
/// </remarks>
public sealed record TransformRule(
    QualifiedName? Path,
    TypeSet? Types,
    InterpretedValue? KeyField,
    StableOrderingKey Order,
    DeclarationSite Declaration);

/// <summary>
/// One winning output-instance-scoped directive declaration, kept for Section 15.2's cross-selector
/// "one source-ordered override stream" resolution at pipeline step 13.
/// </summary>
/// <param name="Selector">The written selector, possibly a wildcard.</param>
/// <param name="Directive">The directive kind.</param>
/// <param name="Order">The declaration's source order, which Section 15.2 orders precedence by.</param>
/// <param name="Entry">The raw entry, retained for its value and its declaration site.</param>
/// <remarks>
/// Section 15.2 puts "exact and wildcard declarations that literalize to the same concrete
/// selector" in one override stream, so the winning declaration at (<c>a.x</c>, <c>filename</c>)
/// competes for the concrete instance <c>a.x</c> produced from the wildcard
/// <c>a.*.output=namespace</c>. That cross-selector comparison cannot be made in this phase — the
/// concrete selectors do not exist until step 13 expands wildcards — so this record carries the
/// per-directive winners forward for Section 14.1's expansion pass to bind against.
/// </remarks>
public sealed record InstanceOptionWinner(
    SelectorKey Selector,
    SchemeDirective Directive,
    long Order,
    SchemeEntry Entry);

/// <summary>
/// The compiled scheme: Section 15.1 steps 2 through 4, for the directives this build implements.
/// </summary>
/// <param name="Outputs">The concrete output instances, in declaration order.</param>
/// <param name="InputMerges">The literal-path input merge directives, in source order.</param>
/// <param name="Transforms">The Section 16.5 and 16.6 path-scoped rules, in source order.</param>
/// <param name="Deferred">
/// Recognized directives this build does not compile. They are carried rather than dropped so the
/// driver can refuse the run instead of silently ignoring configuration the user wrote.
/// </param>
/// <param name="InstanceOptions">
/// The winning per-instance-scoped directive declarations, keyed by (selector, directive) and kept
/// in source order for Section 15.2's cross-selector override stream. The compiler binds a winner
/// to the output instance carrying its <em>exact</em> selector; pipeline step 13 additionally binds
/// each winner to every concrete instance whose selector its written selector literalizes to,
/// which is where the wildcard-plus-exact override in Section 15.2's fourth bullet becomes visible.
/// </param>
public sealed record SchemeConfiguration(
    ImmutableArray<OutputInstance> Outputs,
    ImmutableArray<InputMerge> InputMerges,
    ImmutableArray<TransformRule> Transforms,
    ImmutableArray<SchemeEntry> Deferred,
    ImmutableArray<InstanceOptionWinner> InstanceOptions);

/// <summary>
/// Compiles Section 15 directives into the configuration the pipeline reads.
/// </summary>
/// <remarks>
/// <para>
/// Section 15.2 makes precedence purely source-ordered: "a later matching directive overrides an
/// earlier matching directive for the same effective setting", and "pattern specificity does not
/// alter precedence". Compilation therefore keeps one winner per (selector, setting) rather than
/// scoring candidates.
/// </para>
/// <para>
/// Configuration is output-instance-scoped. A directive for selector <c>a</c> does not configure an
/// independently created <c>a.x</c> instance, so the selector is the key and no prefix relation
/// between selectors is consulted.
/// </para>
/// </remarks>
public static class SchemeCompiler
{
    /// <summary>Compiles one run's scheme entries.</summary>
    /// <param name="entries">Every scheme source's entries.</param>
    /// <param name="diagnostics">The buffer compilation diagnostics accumulate in.</param>
    public static SchemeConfiguration Compile(
        ImmutableArray<SchemeEntry> entries,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        // Section 15.2 orders directives by source only, and Section 4.7 keys already order entries
        // across sources. The index in that order is the declaration order Section 17.5 folds by.
        // OrderBy is a documented stable sort; ImmutableArray.Sort is not, and two entries written
        // on one line share a key, so an unstable sort would make the winner depend on the sorting
        // algorithm rather than on source order.
        var ordered = entries.IsDefault
            ? ImmutableArray<SchemeEntry>.Empty
            : [.. entries.OrderBy(entry => entry.Order)];

        var winners = new Dictionary<(SelectorKey Selector, SchemeDirective Directive), (SchemeEntry Entry, long Order)>();
        // Section 15.2: "A later matching directive overrides an earlier matching directive for the
        // same effective setting." An input 'merge' has one effective setting per path, so two
        // declarations at one path are an override, not two settings. Appending both would make
        // "merge=append" then "merge=replace" at the same path an unhandled duplicate-key exception
        // downstream, which Section 6.3 forbids as a way for a user-caused condition to surface.
        var mergeWinners = new Dictionary<SelectorKey, (SchemeEntry Entry, long Order)>();
        var transforms = ImmutableArray.CreateBuilder<TransformRule>();
        var deferred = ImmutableArray.CreateBuilder<SchemeEntry>();

        for (var index = 0; index < ordered.Length; index++)
        {
            var entry = ordered[index];

            if (entry.Value.ContainsReference
                || (entry.Value.ContainsWildcard && DeferredWildcardDirective(entry.Directive)))
            {
                // Section 15.1 step 1 resolves scheme-internal references before anything reads a
                // directive value. Compiling the text as written would silently treat "${x}" as a
                // literal file name.
                //
                // Section 12.1 makes a '*' in a scheme value a positional capture substitution
                // wherever the selector defines an unnamed capture, so a wildcard is not deferred
                // for any directive whose captures are known where its value is read: the
                // output-instance-scoped ones take them from the Section 14.1 expansion at step 13,
                // and 'key' takes them from its own per-path match at step 16.
                deferred.Add(entry);
                continue;
            }

            switch (entry.Directive)
            {
                case SchemeDirective.Merge:
                    mergeWinners[new SelectorKey(entry.Selector)] = (entry, index);
                    break;

                case SchemeDirective.Type:
                case SchemeDirective.Key:
                    // Section 15.2 evaluates these against absolute paths in every instance rather
                    // than binding them to one selector, so every rule is kept in source order and
                    // the winner is decided per matched path at step 16.
                    CompileTransform(entry, diagnostics, transforms);
                    break;

                case SchemeDirective.Output:
                case SchemeDirective.Filename:
                case SchemeDirective.Root:
                case SchemeDirective.Delimiter:
                case SchemeDirective.IniOutputOptions:
                case SchemeDirective.JsonOutputOptions:
                case SchemeDirective.YamlOutputOptions:
                case SchemeDirective.XmlOutputOptions:
                case SchemeDirective.FileMerge:
                    winners[(new SelectorKey(entry.Selector), entry.Directive)] = (entry, index);
                    break;

                case SchemeDirective.XmlInputOptions:
                case SchemeDirective.JsonInputOptions:
                case SchemeDirective.YamlInputOptions:
                    // Section 15.1 step 2 already compiled these, before the inputs they govern
                    // were parsed. Nothing later configures an output instance from one.
                    break;

                case SchemeDirective.Substitute:
                    // Section 15.1 step 3 already compiled these, before the input values they
                    // govern were lexed at step 6. Nothing later configures an output instance
                    // from one, and reaching the default arm would refuse the whole run.
                    break;

                default:
                    deferred.Add(entry);
                    break;
            }
        }

        return new SchemeConfiguration(
            BuildInstances(winners, diagnostics),
            CompileInputMerges(mergeWinners, diagnostics),
            transforms.ToImmutable(),
            deferred.ToImmutable(),
            CollectInstanceOptions(winners));
    }

    /// <summary>
    /// Whether a Section 12.1 capture substitution in this directive's value has to wait, because
    /// nothing has bound the captures where the value is read.
    /// </summary>
    /// <param name="directive">The directive carrying the value.</param>
    /// <remarks>
    /// <para>
    /// <c>type</c> is matched per path at step 16 exactly as <c>key</c> is, so its captures do
    /// exist where its value is read. What keeps it here is Section 16.6, which closes its value
    /// to a keyword set, together with Section 22, which places the rejection of an unrecognized
    /// one in the scheme phase. Parsing the value late would move that diagnostic to another
    /// phase, and a capture can complete a type keyword only by accident, so the trade favours the
    /// fixed phase.
    /// </para>
    /// <para>
    /// <c>output</c> is here for a different reason: it is the directive that <em>creates</em> an
    /// output instance rather than one bound to an instance that already exists, so the step 13
    /// expansion the other instance-scoped directives take their captures from has nothing to bind
    /// its value against. Its formats are read while the instances are still being built. Without
    /// this arm the value's null literal text reaches <c>TryCompileOutput</c> and the run dies with
    /// a <c>NullReferenceException</c>, which Section 6.3 forbids as a way for a user-caused
    /// condition to surface.
    /// </para>
    /// <para>
    /// No other directive reaches here. Section 15.1 compiles <c>merge</c>, <c>substitute</c> and
    /// the input-options directives at steps 2 through 4, before any selector has been expanded,
    /// but each also rejects the declaration on its own terms — Sections 16.8 and 16.10 forbid the
    /// wildcard selector outright, and Section 16.7 closes its value — and a precise scheme error
    /// naming the rule that was broken is worth more than a refusal to run.
    /// </para>
    /// </remarks>
    private static bool DeferredWildcardDirective(SchemeDirective directive) =>
        directive is SchemeDirective.Type or SchemeDirective.Output;

    /// <summary>
    /// Every winning per-instance-scoped directive declaration, in source order.
    /// </summary>
    /// <param name="winners">The compiled winners, keyed by (selector, directive).</param>
    /// <remarks>
    /// The <c>output</c> declarations that create instances are excluded — they are what pipeline
    /// step 13 expands, not what it binds against. Every other member of the Section 15.2
    /// output-instance-scoped set is included so the expansion pass can bind exact and wildcard
    /// declarations to the same concrete instance under one source-ordered override stream.
    /// </remarks>
    private static ImmutableArray<InstanceOptionWinner> CollectInstanceOptions(
        Dictionary<(SelectorKey Selector, SchemeDirective Directive), (SchemeEntry Entry, long Order)> winners) =>
        [
            .. winners
                .Where(pair => pair.Key.Directive != SchemeDirective.Output)
                .OrderBy(pair => pair.Value.Order)
                .Select(pair => new InstanceOptionWinner(
                    pair.Key.Selector, pair.Key.Directive, pair.Value.Order, pair.Value.Entry)),
        ];

    /// <summary>Sections 16.5 and 16.6, compiled into one source-ordered rule.</summary>
    private static void CompileTransform(
        SchemeEntry entry,
        DiagnosticBuffer diagnostics,
        ImmutableArray<TransformRule>.Builder transforms)
    {
        var site = new DeclarationSite(entry.Declaration, entry.Source, entry.Line);

        if (entry.Directive == SchemeDirective.Key)
        {
            // A template's emptiness cannot be decided here: Section 12.1 substitutes captures that
            // step 16 binds, so 'key= *' is empty only for a capture that matched nothing. The check
            // that survives compile time is the one about text that is already final.
            if (entry.Value.LiteralText is { } literal && literal.Trim().Length == 0)
            {
                Reject(entry, diagnostics, "\u00A716.5", "a 'key' directive names no field.");
                return;
            }

            transforms.Add(new TransformRule(entry.Selector, null, entry.Value, entry.Order, site));
            return;
        }

        var written = entry.Value.LiteralText!;

        if (!TypeSet.TryParse(written, out var types, out var failure))
        {
            Reject(entry, diagnostics, "\u00A716.6", failure!);
            return;
        }

        // Section 15.3 accepts the legacy values with "one warning per scheme". They are recognized
        // here rather than in the reader because only this directive's own section gives a value
        // meaning, and the reader deliberately "does not interpret directive values".
        if (written.Split(',').Any(token => TypeSet.IsLegacyNoOp(token.Trim())))
        {
            diagnostics.Add(new BufferedDiagnostic(
                DiagnosticCodes.Warn002(
                    DiagnosticPhase.Scheme,
                    "\u00A715.3",
                    $"'{SchemeDirectives.Spelling(SchemeAlias.LegacyTypeValue)}' are deprecated "
                    + "legacy 'type' values and are treated as no-ops.",
                    cardinalityKey: $"{entry.Source}:{SchemeAlias.LegacyTypeValue}",
                    source: entry.Source,
                    line: entry.Line,
                    declaration: entry.Declaration),
                entry.Order));
        }

        if (types.Values.Count == 0)
        {
            // Every value written was a Section 15.3 no-op, so the directive does nothing at all.
            // Keeping it as a rule would let it win the Section 16.6 set replacement and clear an
            // earlier directive, which is the one thing "treated as no-ops" rules out.
            return;
        }

        transforms.Add(new TransformRule(entry.Selector, types, null, entry.Order, site));
    }

    private static ImmutableArray<InputMerge> CompileInputMerges(
        Dictionary<SelectorKey, (SchemeEntry Entry, long Order)> mergeWinners,
        DiagnosticBuffer diagnostics)
    {
        var merges = ImmutableArray.CreateBuilder<InputMerge>(mergeWinners.Count);

        foreach (var (_, winner) in mergeWinners.OrderBy(pair => pair.Value.Order))
        {
            CompileInputMerge(winner.Entry, diagnostics, merges);
        }

        return merges.ToImmutable();
    }

    private static ImmutableArray<OutputInstance> BuildInstances(
        Dictionary<(SelectorKey Selector, SchemeDirective Directive), (SchemeEntry Entry, long Order)> winners,
        DiagnosticBuffer diagnostics)
    {
        var instances = ImmutableArray.CreateBuilder<OutputInstance>();

        foreach (var (key, winner) in winners.Where(pair => pair.Key.Directive == SchemeDirective.Output)
            .OrderBy(pair => pair.Value.Order))
        {
            var selector = key.Selector;

            if (!TryCompileOutput(winner.Entry, diagnostics, out var formats))
            {
                continue;
            }

            instances.Add(new OutputInstance(
                selector,
                formats,
                winner.Order,
                CompileRef(winners, selector, SchemeDirective.Filename, diagnostics, CompileFilename),
                CompileRef(winners, selector, SchemeDirective.Root, diagnostics, CompileRoot),
                CompileDelimiter(winners, selector, formats, diagnostics),
                Compile(winners, selector, SchemeDirective.IniOutputOptions, diagnostics, CompileIniOptions)
                    ?? IniOutput.Default,
                Compile(winners, selector, SchemeDirective.JsonOutputOptions, diagnostics, CompileJsonOptions)
                    ?? JsonOutput.Default,
                Compile(winners, selector, SchemeDirective.YamlOutputOptions, diagnostics, CompileYamlOptions)
                    ?? YamlOutput.Default,
                Compile(winners, selector, SchemeDirective.XmlOutputOptions, diagnostics, CompileXmlOptions)
                    ?? XmlOutput.Default,
                Compile(winners, selector, SchemeDirective.FileMerge, diagnostics, CompileStrategy)
                    ?? MergeStrategy.Deep,
                WildcardCaptures.Empty,
                WildcardMatchOrder: 0,
                Site(winners, selector, SchemeDirective.Filename),
                Site(winners, selector, SchemeDirective.FileMerge),
                Site(winners, selector, SchemeDirective.IniOutputOptions),
                Site(winners, selector, SchemeDirective.JsonOutputOptions),
                Site(winners, selector, SchemeDirective.YamlOutputOptions),
                Site(winners, selector, SchemeDirective.XmlOutputOptions),
                new DeclarationSite(
                    winner.Entry.Declaration, winner.Entry.Source, winner.Entry.Line)));
        }

        // Section 15.2's cross-selector "one source-ordered override stream" and its Section 22
        // warning for unbound directives are pipeline-step-13 concerns: they need the concrete
        // literalized selectors that only wildcard expansion produces. Each winner is carried on
        // <see cref="SchemeConfiguration.InstanceOptions"/> so that pass can re-resolve them.

        return instances.ToImmutable();
    }

    /// <summary>
    /// Compiles one winning per-instance-scoped directive against a concrete output instance's
    /// formats, and returns the typed value. A rejection emits <c>SCHEME001</c> and yields nothing.
    /// </summary>
    /// <param name="winner">The winning declaration.</param>
    /// <param name="formats">The concrete output instance's formats.</param>
    /// <param name="captures">The Section 14.1 captures the winning declaration's match bound.</param>
    /// <param name="diagnostics">The buffer diagnostics accumulate in.</param>
    /// <returns>The typed value, discriminated by <see cref="InstanceOptionWinner.Directive"/>.</returns>
    /// <remarks>
    /// Pipeline step 13 calls this once per concrete instance and winning directive after Section
    /// 15.2's cross-selector override stream has picked a winner. Section 16.4 makes delimiter
    /// validation depend on the formats a concrete instance renders — a delimiter legal for INI is
    /// not necessarily legal for namespace — so the compilation cannot happen at scheme-compile
    /// time for a directive that a wildcard <c>output</c> expansion eventually binds to a
    /// concrete instance whose formats live on a different declaration.
    /// </remarks>
    internal static object? CompileInstanceOption(
        InstanceOptionWinner winner,
        ImmutableArray<OutputFormat> formats,
        WildcardCaptures captures,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(captures);

        // Section 12.1 substitutes the selector's captures into the directive's value. 'filename'
        // is the exception: Section 16.2 makes substituted text "opaque segment data", so its
        // template is carried to 'OutputInstance.Filename' and substituted where the portable
        // segment algorithm can see the boundary between generated and written text.
        var entry = winner.Directive == SchemeDirective.Filename || !winner.Entry.Value.ContainsWildcard
            ? winner.Entry
            : winner.Entry with
            {
                Value = new InterpretedValue(
                    [new LiteralValueToken(WildcardSubstitution.Apply(winner.Entry.Value, captures))]),
            };

        return winner.Directive switch
        {
            SchemeDirective.Filename => CompileFilename(entry, diagnostics),
            SchemeDirective.Root => CompileRoot(entry, diagnostics),
            SchemeDirective.Delimiter => CompileDelimiterValue(entry, formats, diagnostics),
            SchemeDirective.IniOutputOptions => CompileIniOptions(entry, diagnostics),
            SchemeDirective.JsonOutputOptions => CompileJsonOptions(entry, diagnostics),
            SchemeDirective.YamlOutputOptions => CompileYamlOptions(entry, diagnostics),
            SchemeDirective.XmlOutputOptions => CompileXmlOptions(entry, diagnostics),
            SchemeDirective.FileMerge => CompileStrategy(entry, diagnostics),
            _ => throw new ArgumentOutOfRangeException(
                nameof(winner),
                winner.Directive,
                "Section 15.2's per-instance override stream carries only the directives listed on "
                + "'SchemeConfiguration.InstanceOptions'."),
        };
    }

    private static T? Compile<T>(
        Dictionary<(SelectorKey Selector, SchemeDirective Directive), (SchemeEntry Entry, long Order)> winners,
        SelectorKey selector,
        SchemeDirective directive,
        DiagnosticBuffer diagnostics,
        Func<SchemeEntry, DiagnosticBuffer, T?> compile)
        where T : struct =>
        winners.TryGetValue((selector, directive), out var winner) && !Pending(winner.Entry)
            ? compile(winner.Entry, diagnostics)
            : null;

    private static T? CompileRef<T>(
        Dictionary<(SelectorKey Selector, SchemeDirective Directive), (SchemeEntry Entry, long Order)> winners,
        SelectorKey selector,
        SchemeDirective directive,
        DiagnosticBuffer diagnostics,
        Func<SchemeEntry, DiagnosticBuffer, T?> compile)
        where T : class =>
        winners.TryGetValue((selector, directive), out var winner) && !Pending(winner.Entry)
            ? compile(winner.Entry, diagnostics)
            : null;

    /// <summary>
    /// The site of the winning declaration of <paramref name="directive"/>, or null when the
    /// selector declared it nowhere.
    /// </summary>
    /// <param name="winners">The compiled winners, keyed by selector and directive.</param>
    /// <param name="selector">The selector whose declaration is wanted.</param>
    /// <param name="directive">The directive whose declaration is wanted.</param>
    private static DeclarationSite? Site(
        Dictionary<(SelectorKey Selector, SchemeDirective Directive), (SchemeEntry Entry, long Order)> winners,
        SelectorKey selector,
        SchemeDirective directive) =>
        winners.TryGetValue((selector, directive), out var winner)
            ? new DeclarationSite(winner.Entry.Declaration, winner.Entry.Source, winner.Entry.Line)
            : null;

    private static QualifiedName? Compile(
        Dictionary<(SelectorKey Selector, SchemeDirective Directive), (SchemeEntry Entry, long Order)> winners, SelectorKey selector,
        SchemeDirective directive,
        DiagnosticBuffer diagnostics,
        Func<SchemeEntry, DiagnosticBuffer, QualifiedName?> compile) =>
        winners.TryGetValue((selector, directive), out var winner) && !Pending(winner.Entry)
            ? compile(winner.Entry, diagnostics)
            : null;

    /// <summary>Section 16.1.</summary>
    private static bool TryCompileOutput(
        SchemeEntry entry,
        DiagnosticBuffer diagnostics,
        out ImmutableArray<OutputFormat> formats)
    {
        formats = [];

        var written = Split(entry.Value.LiteralText!);
        var parsed = ImmutableArray.CreateBuilder<OutputFormat>(written.Length);
        var ignores = 0;

        foreach (var name in written)
        {
            if (string.Equals(name, OutputFormats.Ignore, StringComparison.OrdinalIgnoreCase))
            {
                ignores++;
                continue;
            }

            if (!OutputFormats.TryParse(name, out var format))
            {
                Reject(
                    entry,
                    diagnostics,
                    "\u00A716.1",
                    $"'{name}' is not one of the Section 16.1 output formats.");
                return false;
            }

            // Section 17.5: "Duplicate formats within one 'output' declaration, such as
            // 'output=json,json', are blocking scheme errors rather than self-collisions." Left
            // unchecked the two entries become two contributions to one destination, which
            // Section 17.5 would then fold under 'filemerge' and warn about — a file merged with
            // itself, reported as a collision between a declaration and itself.
            if (parsed.Contains(format))
            {
                Reject(
                    entry,
                    diagnostics,
                    "\u00A717.5",
                    $"'{name}' appears more than once in this "
                    + "declaration, and Section 17.5 makes a duplicate format within one 'output' "
                    + "declaration a blocking scheme error rather than a self-collision.");
                return false;
            }

            parsed.Add(format);
        }

        // Section 16.1: "'ignore' must appear alone in its declaration."
        if (ignores > 0 && (ignores > 1 || parsed.Count > 0))
        {
            Reject(
                entry,
                diagnostics,
                "\u00A716.1",
                "'ignore' must appear alone in its declaration, because it is the negative "
                + "declaration and has nothing to be combined with.");
            return false;
        }

        formats = parsed.ToImmutable();
        return true;
    }

    /// <summary>Section 16.2.</summary>
    private static InterpretedValue? CompileFilename(SchemeEntry entry, DiagnosticBuffer diagnostics)
    {
        _ = diagnostics;

        // Section 21.1 validates the path itself at pipeline step 14, after filename expansion,
        // because only then is every substituted capture present. Rejecting a written path here
        // would check a different string from the one that reaches the filesystem.
        //
        // The value is kept whole rather than reduced to its text: Section 16.2 substitutes the
        // selector's wildcard captures into it, and LiteralText is null for exactly the values that
        // need it. Taking the text here would silently turn a wildcard filename into no filename
        // at all, and the instance would land on its default name with no diagnostic.
        return entry.Value;
    }

    /// <summary>Section 16.3.</summary>
    private static QualifiedName? CompileRoot(SchemeEntry entry, DiagnosticBuffer diagnostics)
    {
        // Section 16.3 gives the root value the name grammar, not the value grammar: "'\.'
        // represents a literal dot inside one root name part". Appendix A.3 leaves '\.' intact
        // because it is not a value escape, so the scalar reaching here still carries it.
        var lexed = QualifiedNameLexer.Lex(entry.Value.LiteralText!);

        if (lexed.Name is null)
        {
            Reject(entry, diagnostics, "\u00A716.3", lexed.Fault!.Value.Message);
            return null;
        }

        if (QualifiedNameLexer.ContainsWildcard(lexed.Name))
        {
            // Section 12.1: "in a scheme whose selector contains no wildcard, '*' in a 'filename',
            // 'root', or 'delimiter' value is literal text." Where the selector does define
            // captures, CompileInstanceOption has already substituted them, so what arrives here is
            // data. Neither case is a pattern — Section 16.3 gives 'root' no matching semantics at
            // all — so the asterisk is literal in both, and Section 21 escapes it on the way out.
            return QualifiedNameLexer.Literalize(lexed.Name);
        }

        return lexed.Name;
    }

    /// <summary>Section 16.4.</summary>
    private static string? CompileDelimiter(
        Dictionary<(SelectorKey Selector, SchemeDirective Directive), (SchemeEntry Entry, long Order)> winners,
        SelectorKey selector,
        ImmutableArray<OutputFormat> formats,
        DiagnosticBuffer diagnostics) =>
        winners.TryGetValue((selector, SchemeDirective.Delimiter), out var winner)
            && !Pending(winner.Entry)
            ? CompileDelimiterValue(winner.Entry, formats, diagnostics)
            : null;

    /// <summary>
    /// Whether this declaration's value still holds a Section 12.1 capture substitution, so nothing
    /// may read it until pipeline step 13 binds the captures.
    /// </summary>
    /// <param name="entry">The winning declaration.</param>
    /// <remarks>
    /// Every wildcard-valued directive reaches <see cref="CompileInstanceOption"/> at step 13
    /// through <c>InstanceOptions</c>, which is where its captures exist. Compiling the template
    /// here as well would parse text that is not the directive's value — and for a value that is
    /// nothing but a substitution, no text at all.
    /// </remarks>
    private static bool Pending(SchemeEntry entry) => entry.Value.ContainsWildcard;

    /// <summary>
    /// Section 16.4, given a specific delimiter declaration and the formats it applies against.
    /// </summary>
    /// <param name="entry">The winning delimiter declaration.</param>
    /// <param name="formats">The concrete instance's formats.</param>
    /// <param name="diagnostics">The buffer diagnostics accumulate in.</param>
    /// <remarks>
    /// Section 16.4 states its restrictions "for namespace output", and quoted-namespace and INI
    /// output "instead apply their own validation and escaping after joining". Applying the
    /// namespace rule to an instance that renders neither would reject a legal configuration.
    /// A wildcard <c>output</c> declaration and an exact-selector <c>delimiter</c> declaration can
    /// meet on a concrete instance whose formats live on the <em>output</em> declaration, so the
    /// validation is deliberately extracted from the winners-lookup helper.
    /// </remarks>
    private static string? CompileDelimiterValue(
        SchemeEntry entry,
        ImmutableArray<OutputFormat> formats,
        DiagnosticBuffer diagnostics)
    {
        var delimiter = entry.Value.LiteralText!;

        if (!formats.Contains(OutputFormat.Namespace))
        {
            return delimiter;
        }

        if (!NamespaceEncoder.IsValidDelimiter(delimiter, out var reason))
        {
            Reject(entry, diagnostics, "\u00A716.4", reason!);
            return null;
        }

        return delimiter;
    }

    /// <summary>Section 16.9.</summary>
    private static IniOutputOptions? CompileIniOptions(SchemeEntry entry, DiagnosticBuffer diagnostics)
    {
        var options = IniOutputOptions.None;

        foreach (var name in Split(entry.Value.LiteralText!))
        {
            if (!IsName(name)
                || !Enum.TryParse<IniOutputOptions>(name, ignoreCase: true, out var flag)
                || !Enum.IsDefined(flag)
                || flag == IniOutputOptions.None)
            {
                Reject(
                    entry,
                    diagnostics,
                    "\u00A716.9",
                    $"'{name}' is not one of the Section 16.9 'PortableIni1' options.");
                return null;
            }

            options |= flag;
        }

        if (!options.TryValidate(out var contradiction))
        {
            Reject(entry, diagnostics, "\u00A716.9", contradiction!);
            return null;
        }

        // Section 16.9: "When a replacement omits every flag from a mutually exclusive mode group,
        // that group's documented default is reapplied." The multiline group is the only INI group
        // with a default; comments are off unless a marker is selected.
        if (!options.HasFlag(IniOutputOptions.EscapeMultiline))
        {
            options |= IniOutputOptions.RejectMultiline;
        }

        return options;
    }

    /// <summary>Section 16.9.</summary>
    private static JsonOutputOptions? CompileJsonOptions(SchemeEntry entry, DiagnosticBuffer diagnostics)
    {
        var options = JsonOutputOptions.None;

        foreach (var name in Split(entry.Value.LiteralText!))
        {
            if (!IsName(name)
                || !Enum.TryParse<JsonOutputOptions>(name, ignoreCase: true, out var flag)
                || !Enum.IsDefined(flag)
                || flag == JsonOutputOptions.None)
            {
                Reject(
                    entry,
                    diagnostics,
                    "\u00A716.9",
                    $"'{name}' is not one of the Section 16.9 JSON output options.");
                return null;
            }

            options |= flag;
        }

        if (!options.TryValidate(out var contradiction))
        {
            Reject(entry, diagnostics, "\u00A716.9", contradiction!);
            return null;
        }

        // Section 16.9: "When a replacement omits every flag from a mutually exclusive mode group,
        // that group's documented default is reapplied." Layout is the only JSON group with a
        // default; 'EscapeNonAscii' is independent and off unless named.
        if (!options.HasFlag(JsonOutputOptions.Compact))
        {
            options |= JsonOutputOptions.Indent;
        }

        return options;
    }

    /// <summary>Section 16.8 step 2: the root-level input options.</summary>
    /// <param name="entries">Every scheme directive, in source order.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    /// <returns>The compiled options, or the defaults when nothing named one.</returns>
    /// <remarks>
    /// Section 16.8: "Selector-qualified input-option directives are blocking scheme errors because
    /// input parsing occurs before output instances exist." That is a different fault from the
    /// Section 15.2 <c>WARN009</c> a selector-qualified <em>output</em> directive gets when nothing
    /// binds it, and the reason is stated: an input option can never bind, at any selector, because
    /// the thing it would qualify does not exist yet.
    /// </remarks>
    public static InputOptions CompileInputOptions(
        ImmutableArray<SchemeEntry> entries, DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var xml = XmlInput.Default;

        foreach (var entry in entries)
        {
            var format = entry.Directive switch
            {
                SchemeDirective.XmlInputOptions => "XML",
                SchemeDirective.JsonInputOptions => "JSON",
                SchemeDirective.YamlInputOptions => "YAML",
                _ => null,
            };

            if (format is null)
            {
                continue;
            }

            if (entry.Selector is not null)
            {
                Reject(
                    entry,
                    diagnostics,
                    "\u00A716.8",
                    $"'{entry.Declaration}' qualifies an input-option directive with a selector. "
                    + "Section 16.8 makes that a blocking scheme error, because input parsing "
                    + "occurs before output instances exist.");
                continue;
            }

            if (entry.Value.LiteralText is not { } text)
            {
                Reject(
                    entry,
                    diagnostics,
                    "\u00A716.8",
                    $"'{entry.Declaration}' does not name a literal option set.");
                continue;
            }

            // Section 16.8: "The later complete directive wins." A directive that named something
            // outside its set is not complete, so it replaces nothing.
            if (entry.Directive == SchemeDirective.XmlInputOptions)
            {
                if (CompileXmlInputOptions(entry, text, diagnostics) is { } compiled)
                {
                    xml = compiled;
                }

                continue;
            }

            CheckNamed(entry, text, format, diagnostics);
        }

        return new InputOptions(xml);
    }

    /// <summary>Section 16.8 XML.</summary>
    private static XmlInputOptions? CompileXmlInputOptions(
        SchemeEntry entry, string text, DiagnosticBuffer diagnostics)
    {
        var options = XmlInputOptions.None;

        foreach (var name in Split(text))
        {
            if (!IsName(name)
                || !Enum.TryParse<XmlInputOptions>(name, ignoreCase: true, out var flag)
                || !Enum.IsDefined(flag)
                || flag == XmlInputOptions.None)
            {
                Reject(
                    entry,
                    diagnostics,
                    "\u00A716.8",
                    $"'{name}' is not one of the Section 16.8 XML input options.");
                return null;
            }

            options |= flag;
        }

        if (!options.TryValidate(out var contradiction))
        {
            Reject(entry, diagnostics, "\u00A716.8", contradiction!);
            return null;
        }

        // Section 16.8: an option set naming neither member of the group reapplies the default,
        // which is what an empty 'xmlinputoptions=' asks for.
        return options == XmlInputOptions.None ? XmlInput.Default : options;
    }

    /// <summary>
    /// Checks a Section 16.8 option set whose every value is enabled by default.
    /// </summary>
    /// <param name="entry">The directive.</param>
    /// <param name="text">Its value.</param>
    /// <param name="format">The format its section names, for the message.</param>
    /// <param name="diagnostics">This step's buffer.</param>
    private static void CheckNamed(
        SchemeEntry entry, string text, string format, DiagnosticBuffer diagnostics)
    {
        var permitted = entry.Directive == SchemeDirective.JsonInputOptions
            ? "Strict"
            : "PreserveComments";

        foreach (var name in Split(text))
        {
            if (name.Length > 0
                && !string.Equals(name, permitted, StringComparison.OrdinalIgnoreCase))
            {
                Reject(
                    entry,
                    diagnostics,
                    "\u00A716.8",
                    $"'{name}' is not one of the Section 16.8 {format} input options; "
                    + $"'{permitted}' is the only one, and it is enabled by default.");
                return;
            }
        }
    }

    /// <summary>Section 16.9.</summary>
    private static XmlOutputOptions? CompileXmlOptions(SchemeEntry entry, DiagnosticBuffer diagnostics)
    {
        var options = XmlOutputOptions.None;

        foreach (var name in Split(entry.Value.LiteralText!))
        {
            if (!IsName(name)
                || !Enum.TryParse<XmlOutputOptions>(name, ignoreCase: true, out var flag)
                || !Enum.IsDefined(flag)
                || flag == XmlOutputOptions.None)
            {
                Reject(
                    entry,
                    diagnostics,
                    "\u00A716.9",
                    $"'{name}' is not one of the Section 16.9 XML output options.");
                return null;
            }

            options |= flag;
        }

        if (!options.TryValidate(out var contradiction))
        {
            Reject(entry, diagnostics, "\u00A716.9", contradiction!);
            return null;
        }

        // Section 16.9: "When a replacement omits every flag from a mutually exclusive mode group,
        // that group's documented default is reapplied." XML has three such groups, and
        // 'NewLineOnAttributes' is independent and off unless named.
        if (!options.HasFlag(XmlOutputOptions.NoIndent))
        {
            options |= XmlOutputOptions.Indent;
        }

        if (!options.HasFlag(XmlOutputOptions.CDataAsText))
        {
            options |= XmlOutputOptions.PreserveCData;
        }

        if (!options.HasFlag(XmlOutputOptions.NoDeclaration))
        {
            options |= XmlOutputOptions.Declaration;
        }

        return options;
    }

    /// <summary>Section 16.9.</summary>
    private static YamlOutputOptions? CompileYamlOptions(SchemeEntry entry, DiagnosticBuffer diagnostics)
    {
        var options = YamlOutputOptions.None;

        foreach (var name in Split(entry.Value.LiteralText!))
        {
            if (!IsName(name)
                || !Enum.TryParse<YamlOutputOptions>(name, ignoreCase: true, out var flag)
                || !Enum.IsDefined(flag)
                || flag == YamlOutputOptions.None)
            {
                Reject(
                    entry,
                    diagnostics,
                    "\u00A716.9",
                    $"'{name}' is not one of the Section 16.9 YAML output options.");
                return null;
            }

            options |= flag;
        }

        if (!options.TryValidate(out var contradiction))
        {
            Reject(entry, diagnostics, "\u00A716.9", contradiction!);
            return null;
        }

        // Section 16.9: an omitted mode group reapplies its documented default.
        if (!options.HasFlag(YamlOutputOptions.DiscardComments))
        {
            options |= YamlOutputOptions.PreserveComments;
        }

        return options;
    }

    private static MergeStrategy? CompileStrategy(SchemeEntry entry, DiagnosticBuffer diagnostics)
    {
        var written = entry.Value.LiteralText!.Trim();

        if (!IsName(written)
            || !Enum.TryParse<MergeStrategy>(written, ignoreCase: true, out var strategy)
            || !Enum.IsDefined(strategy))
        {
            Reject(
                entry,
                diagnostics,
                entry.Directive == SchemeDirective.Merge ? "\u00A716.10" : "\u00A716.11",
                $"'{written}' is not one of 'deep', 'replace', 'append', or 'error'.");
            return null;
        }

        return strategy;
    }

    /// <summary>
    /// Section 15.1 step 3: compiles the <c>substitute</c> directives into their path patterns.
    /// </summary>
    /// <param name="entries">Step 2's product, in source order.</param>
    /// <param name="diagnostics">The scheme phase's buffer.</param>
    /// <returns>The effective Section 16.7 mode for every declared path.</returns>
    /// <remarks>
    /// This runs before <see cref="Compile(ImmutableArray{SchemeEntry}, DiagnosticBuffer)"/>, so the
    /// wildcard and reference values that method defers have not been separated out yet and are
    /// rejected here instead. Section 15.1 permits a name wildcard in the pattern, which is why the
    /// path is not checked the way an input <c>merge</c> path is; a wildcard in the <em>value</em>
    /// is a different thing, and Section 16.7 spells the value as one of four names.
    /// </remarks>
    public static SubstituteModeMap CompileSubstitutes(
        ImmutableArray<SchemeEntry> entries,
        DiagnosticBuffer diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var declarations = ImmutableArray.CreateBuilder<(QualifiedName? Pattern, SubstituteMode Mode)>();

        foreach (var entry in entries)
        {
            if (entry.Directive != SchemeDirective.Substitute)
            {
                continue;
            }

            var written = entry.Value.LiteralText?.Trim();

            if (written is null
                || !IsName(written)
                || !SubstituteModes.TryRecognize(written, out var mode, out var alias))
            {
                Reject(
                    entry,
                    diagnostics,
                    "\u00A716.7",
                    $"'{entry.Value.LiteralText ?? entry.Declaration}' is not one of 'All', "
                    + "'Key', 'Value', or 'None'.");
                continue;
            }

            if (alias == SchemeAlias.KeyOnly)
            {
                // Section 15.3 accepts the alias with "one warning per scheme", and the registry
                // keys WARN002 by alias category and scheme rather than by occurrence.
                diagnostics.Add(new BufferedDiagnostic(
                    DiagnosticCodes.Warn002(
                        DiagnosticPhase.Scheme,
                        "\u00A715.3",
                        $"'{SchemeDirectives.Spelling(SchemeAlias.KeyOnly)}' is a deprecated alias "
                        + $"for {SchemeDirectives.Replacement(SchemeAlias.KeyOnly)}.",
                        cardinalityKey: $"{entry.Source}:{SchemeAlias.KeyOnly}",
                        source: entry.Source,
                        line: entry.Line,
                        declaration: entry.Declaration),
                    entry.Order));
            }

            declarations.Add((entry.Selector, mode));
        }

        return SubstituteModeMap.Create(declarations.ToImmutable());
    }

    private static void CompileInputMerge(
        SchemeEntry entry,
        DiagnosticBuffer diagnostics,
        ImmutableArray<InputMerge>.Builder merges)
    {
        // Section 16.10: "Input 'merge' directives required at pipeline step 4 must use literal
        // paths and must not contain wildcards or references." A reference-bearing value was
        // deferred before reaching here; a wildcard in the path is rejected here.
        if (entry.Selector is { } path && QualifiedNameLexer.ContainsWildcard(path))
        {
            Reject(
                entry,
                diagnostics,
                "\u00A716.10",
                "an input 'merge' path is required at pipeline step 4, before any name graph "
                + "exists to expand a wildcard against.");
            return;
        }

        if (CompileStrategy(entry, diagnostics) is { } strategy)
        {
            merges.Add(new InputMerge(entry.Selector, strategy));
        }
    }

    private static string[] Split(string value) =>
        value.Split(',').Select(part => part.Trim()).ToArray();

    /// <summary>
    /// Whether a token is spelled as a name rather than as a number.
    /// </summary>
    /// <remarks>
    /// <see cref="Enum.TryParse{T}(string, bool, out T)"/> also accepts the underlying integer, so
    /// <c>inioutputoptions=4</c> would otherwise select <c>RejectMultiline</c> and <c>merge=0</c>
    /// would select <c>deep</c>. Sections 16.9 and 16.10 spell their values as names, and a scheme
    /// that names an enum's storage rather than its meaning would silently change with the enum.
    /// </remarks>
    private static bool IsName(string token) =>
        token.Length > 0 && token.All(char.IsAsciiLetter);

    private static void Reject(
        SchemeEntry entry,
        DiagnosticBuffer diagnostics,
        string spec,
        string message) =>
        diagnostics.Add(new BufferedDiagnostic(
            DiagnosticCodes.Scheme001(
                DiagnosticPhase.Scheme,
                spec,
                message,
                cardinalityKey: $"{entry.Source}:{entry.Line}",
                source: entry.Source,
                line: entry.Line,
                path: CanonicalPath.Of(entry.Selector),
                declaration: entry.Declaration),
            entry.Order));
}
