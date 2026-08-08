using System.Collections.Immutable;
using Namespace2Xml.Diagnostics;
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
    MergeStrategy FileMerge,
    WildcardCaptures Captures,
    int WildcardMatchOrder,
    DeclarationSite? FilenameDeclaration,
    DeclarationSite? FileMergeDeclaration,
    DeclarationSite? IniOptionsDeclaration,
    DeclarationSite? JsonOptionsDeclaration,
    DeclarationSite? YamlOptionsDeclaration,
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
/// <param name="KeyField">The Section 16.5 field name, or null when this rule is a <c>type</c>.</param>
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
    string? KeyField,
    StableOrderingKey Order,
    DeclarationSite Declaration);

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
public sealed record SchemeConfiguration(
    ImmutableArray<OutputInstance> Outputs,
    ImmutableArray<InputMerge> InputMerges,
    ImmutableArray<TransformRule> Transforms,
    ImmutableArray<SchemeEntry> Deferred);

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
                || (entry.Value.ContainsWildcard && entry.Directive != SchemeDirective.Filename))
            {
                // Section 15.1 step 1 resolves scheme-internal references before anything reads a
                // directive value. Compiling the text as written would silently treat "${x}" as a
                // literal file name.
                //
                // A wildcard is deferred for the same reason everywhere except 'filename', whose
                // Section 16.2 substitution is not a step 1 concern at all: "Wildcard captures may
                // be substituted into path segments", and the captures come from the Section 14.1
                // selector expansion at step 13. Deferring it there would refuse the one directive
                // the specification writes wildcards into.
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
                case SchemeDirective.FileMerge:
                    winners[(new SelectorKey(entry.Selector), entry.Directive)] = (entry, index);
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
            deferred.ToImmutable());
    }

    /// <summary>Sections 16.5 and 16.6, compiled into one source-ordered rule.</summary>
    private static void CompileTransform(
        SchemeEntry entry,
        DiagnosticBuffer diagnostics,
        ImmutableArray<TransformRule>.Builder transforms)
    {
        var written = entry.Value.LiteralText!;
        var site = new DeclarationSite(entry.Declaration, entry.Source, entry.Line);

        if (entry.Directive == SchemeDirective.Key)
        {
            var field = written.Trim();

            if (field.Length == 0)
            {
                Reject(entry, diagnostics, "\u00A716.5", "a 'key' directive names no field.");
                return;
            }

            transforms.Add(new TransformRule(entry.Selector, null, field, entry.Order, site));
            return;
        }

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
                Compile(winners, selector, SchemeDirective.FileMerge, diagnostics, CompileStrategy)
                    ?? MergeStrategy.Deep,
                WildcardCaptures.Empty,
                WildcardMatchOrder: 0,
                Site(winners, selector, SchemeDirective.Filename),
                Site(winners, selector, SchemeDirective.FileMerge),
                Site(winners, selector, SchemeDirective.IniOutputOptions),
                Site(winners, selector, SchemeDirective.JsonOutputOptions),
                Site(winners, selector, SchemeDirective.YamlOutputOptions),
                new DeclarationSite(
                    winner.Entry.Declaration, winner.Entry.Source, winner.Entry.Line)));
        }

        WarnUnbound(winners, instances, diagnostics);

        return instances.ToImmutable();
    }

    /// <summary>
    /// Section 15.2: a selector-qualified directive "that binds to no concrete output instance emits
    /// one scheme warning and is otherwise inert".
    /// </summary>
    private static void WarnUnbound(
        Dictionary<(SelectorKey Selector, SchemeDirective Directive), (SchemeEntry Entry, long Order)> winners,
        ImmutableArray<OutputInstance>.Builder instances,
        DiagnosticBuffer diagnostics)
    {
        var bound = instances.Select(instance => instance.Selector).ToHashSet();

        foreach (var (_, winner) in winners
            .Where(pair => pair.Key.Directive != SchemeDirective.Output
                && !bound.Contains(pair.Key.Selector))
            .OrderBy(pair => pair.Value.Order))
        {
            var entry = winner.Entry;

            diagnostics.Add(new BufferedDiagnostic(
                DiagnosticCodes.Warn009(
                    DiagnosticPhase.Scheme,
                    "\u00A715.2",
                    $"'{entry.Declaration}' binds to no output instance, because no 'output' "
                    + "declaration created one for that selector.",
                    cardinalityKey: $"{entry.Source}:{entry.Line}",
                    source: entry.Source,
                    line: entry.Line,
                    path: CanonicalPath.Of(entry.Selector),
                    declaration: entry.Declaration),
                entry.Order));
        }
    }

    private static T? Compile<T>(
        Dictionary<(SelectorKey Selector, SchemeDirective Directive), (SchemeEntry Entry, long Order)> winners,
        SelectorKey selector,
        SchemeDirective directive,
        DiagnosticBuffer diagnostics,
        Func<SchemeEntry, DiagnosticBuffer, T?> compile)
        where T : struct =>
        winners.TryGetValue((selector, directive), out var winner)
            ? compile(winner.Entry, diagnostics)
            : null;

    private static T? CompileRef<T>(
        Dictionary<(SelectorKey Selector, SchemeDirective Directive), (SchemeEntry Entry, long Order)> winners,
        SelectorKey selector,
        SchemeDirective directive,
        DiagnosticBuffer diagnostics,
        Func<SchemeEntry, DiagnosticBuffer, T?> compile)
        where T : class =>
        winners.TryGetValue((selector, directive), out var winner)
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
        winners.TryGetValue((selector, directive), out var winner)
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
            Reject(
                entry,
                diagnostics,
                "\u00A716.3",
                "a 'root' value wraps content in named levels, so it admits no wildcard.");
            return null;
        }

        return lexed.Name;
    }

    /// <summary>Section 16.4.</summary>
    private static string? CompileDelimiter(
        Dictionary<(SelectorKey Selector, SchemeDirective Directive), (SchemeEntry Entry, long Order)> winners,
        SelectorKey selector,
        ImmutableArray<OutputFormat> formats,
        DiagnosticBuffer diagnostics)
    {
        if (!winners.TryGetValue((selector, SchemeDirective.Delimiter), out var winner))
        {
            return null;
        }

        var entry = winner.Entry;
        var delimiter = entry.Value.LiteralText!;

        // Section 16.4 states its restrictions "for namespace output", and quoted-namespace and INI
        // output "instead apply their own validation and escaping after joining". Applying the
        // namespace rule to an instance that renders neither would reject a legal configuration.
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
