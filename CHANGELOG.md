# Changelog

All notable changes to the tool, to the contract, and to `CONTRIBUTING.md` are recorded here.

Each entry records what was added, what was **removed**, and — where applicable — **which inbound
report caused it**. The second and third of those are the point: an entry that only grows proves
nothing, and "we value feedback" is not evidence that a loop closed.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). The `contract-bundle` revision is
recorded separately from the package version, because the contract and the implementation move
independently.

## [Unreleased]

### Changed

- **A concrete output instance that selects nothing now warns.** A wildcard `output` selector that
  expanded to no instance already drew `WARN009`; a literal selector naming a path that does not
  exist produced a planned output, an empty file, exit 0 and complete silence. The two spellings of
  the same typo were therefore differently loud, and the quieter one is the more common — a `.`
  where a `-` belongs in a single-instance selector says nothing at all, at any verbosity, in any
  diagnostics format. §14.1 now states that a concrete instance whose selected view holds nothing
  emits `WARN009` as well.

  The empty file is still written and the exit code is still 0: §14.1 deliberately specifies the
  empty document, and an output that renders nothing is legal — a scheme may reasonably select a
  branch that a given input set does not populate. This is a warning about a *likely* mistake, not
  a rule about an invalid one. An explicit empty mapping or sequence is content, so it does not
  warn.

  Emptiness is judged on what step 14 selects, not on what survives step 16, so a view emptied by
  `type=ignore` is silent — that is a scheme doing what it was asked to do. The most valuable
  instance of the change is the one nobody asked for: a missing input file now draws both `WARN001`
  and `WARN009`, which is exactly the pair that distinguishes "I could not read your input" from
  "and consequently your output is empty". Bundle revision r45 → r46. Reported as
  **[#79](https://github.com/stop-cran/namespace2xml/issues/79)**. **Closes #79.**

- **§12.1's rule for capture substitution into a scheme directive value now covers the directives
  it always governed.** It was written as "from the captures its **selector** defines", but §15.2
  splits scheme directives into two families — selector-scoped (`output`, `filename`, `root`,
  `delimiter`, output options, `filemerge`) and path-scoped (`type`, `key`, output-view ignores) —
  and a `key` directive's wildcards live in a path, not a selector. The one general sentence the
  contract had therefore did not reach `key` at all, even though `key` substitutes captures, and
  `reg.*.key=*` visibly changes document shape by naming the generated field after each match. The
  clause now names both families and says explicitly that its list of `filename`, `root` and
  `delimiter` is the common cases rather than a closed list — which the following paragraph already
  implied by needing to *exclude* `type` and `output`. §16.3 and §16.5 now state the rule locally
  instead of leaving it to be inferred from §16.2's statement for `filename`.

  No behaviour changed: the corpus already pinned both cases, in
  `a-capture-substitutes-into-root-and-delimiter` and `a-capture-substitutes-into-a-key-field-name`.
  That is the point of the report — the behaviour was right, was tested, and was still not
  reachable from the clause a reader would consult, so the reporting agent confirmed it against the
  binary instead. Reported as
  **[#81](https://github.com/stop-cran/namespace2xml/issues/81)**. **Closes #81.**

### Fixed

- **A generated contribution merged as an earlier-or-later neighbour of a path rather than at its
  own position among the contributions there.** §12.4 makes every generated `(rule,match)` result "a
  separate contribution for every merge strategy" and merges it "at its deterministic rule/match
  position"; the fold compared the rule mark against the **latest** mark at the node and chose a
  side. One binary choice can express the two extremes and nothing in between.

  That is correct for `replace`, `deep` and `error`, each of which is decided by a maximum, and it
  was wrong for `append` — the one strategy under which every contribution survives into the result,
  so that a position among them is observable at all. Under `merge=append`, `a.p.0=first`, then the
  template `a.*.0=from-template`, then `a.p.0=second` published `[from-template, first, second]`
  where the contract requires `[first, from-template, second]`, and produced output identical to the
  run that lists the template first.

  A second shape needed no straddling at all. A later contribution that supplies no item — an
  explicitly empty native sequence, closing a list — still refreshes the node's container shape-mark
  under §4.4, so the node's latest mark and its latest **item** disagreed, and a generated value
  later than the only item was published before it.

  Each sequence item already carried the mark of the contribution that supplied it, so the position
  is found by partitioning the items on that mark and running §16.10's rebase once per group in
  source order. `KNOWN-LIMITS.md` §1.8 had estimated that fixing this would require retaining every
  source's contribution at a path; that estimate was wrong, exactly as §1.7's identical estimate had
  been, and the entry now says so.

  Two fixtures pin it, and the second exists because of the first's blind spot: §5.4 ordering values
  are exposed densely from zero, so a rendering shows the order but not the values, and a rebase
  that allocated `0`, `2`, `3` printed the same document. A reference to `a.p.1` is the instrument
  that sees them. Reported as
  **[#59](https://github.com/stop-cran/namespace2xml/issues/59)**. **Closes #59.**

- **`--verbosity` did nothing, and no operational message was ever written.** §6.2 gives the option
  seven thresholds and describes it as an output threshold that "never changes what the tool
  computes, only what it writes"; the rewrite parsed the value and then consulted it nowhere, so
  `--verbosity none` and `--verbosity critical` both printed every diagnostic, in text and in
  JSON. That is the larger half of the report, and it was found while confirming the smaller one:
  §6.2 also names two categories of operational message — per-file parsing progress at `trace` and
  pipeline-phase progress at `debug` — and §21.4 requires that "replacing an existing destination
  … is logged at information level", none of which existed. Both halves are now implemented, with
  §6.4.3's overriding rule intact: under `--diagnostics-format json` operational output is
  suppressed at every verbosity, and a threshold that filters every diagnostic still writes the two
  bytes `[]` and one LF rather than nothing. Reported as
  **[#78](https://github.com/stop-cran/namespace2xml/issues/78)**. **Closes #78.**

  Two consequences of §6.2 read against §22 are worth recording, because they look like bugs and
  are not: §22 defines exactly two diagnostic severities, so `critical` — "critical host/runtime
  failures only" — admits no diagnostic at all, and `trace`, `debug`, `information` and the default
  are indistinguishable over the severities that exist. They are derived in
  `VerbosityThreshold.Admits` rather than asserted, so that adding a third severity changes the
  answer without changing the reasoning.

  The gap the corpus should have caught it with was already claimed: acceptance items 61 and 82
  both cited verbosity behaviour, and no fixture passed `--verbosity` at all.
  `verbosity-none-still-emits-the-json-array-container` now carries item 82's `[]` claim.
  namespace2xml 2.4.0 honours `--verbosity none` and does emit operational messages, so — like #76
  and #77 — this was a regression in the rewrite rather than a §3.2 change.

- **A free-wildcard reference failed the run from an entry nothing could reach**, and two further
  defects in the same code fell out of fixing it. §14.4 says "missing, cyclic, ambiguous,
  free-wildcard, and non-scalar references in entries unreachable from every concrete output
  instance do not fail the run", and §22 counts `REFERENCE001` "once per reachable owning value".
  Four of those five members were evaluated at §15.1 step 15, where reachability is known, and
  behaved correctly; the free-wildcard case was refused by the value lexer in the input phase,
  before any output instance existed and therefore before reachability could be known. The one
  member of the list that names a wildcard was the one member that did not use the list. It also
  applied to only one of the construct's two spellings — `${a.*}` was refused at lex time while
  `${a.*[0]}` was carried forward — so two spellings of one specified concept had different
  lifetimes. Reported as
  **[#77](https://github.com/stop-cran/namespace2xml/issues/77)**, whose three controls placed the
  defect exactly, and now covered by
  `an-unreachable-free-wildcard-reference-does-not-fail-the-run`. namespace2xml 2.4.0 accepts the
  same profile and exits `0`, so this was a regression in the rewrite rather than a missing
  feature.

- **An unbound capture inside a template's reference ended the process on a stack trace.**
  `a.*[0].copy=${a.*[9]}` read `9` straight out of the bound capture set and threw
  `KeyNotFoundException`, which is neither an exit code §6.3 defines nor a diagnostic an author can
  act on. §13.3 states its test over the result — "after capture substitution, the resulting
  reference must contain no wildcard" — so a capture the template cannot bind now survives
  substitution and is refused as `REFERENCE001` against §13.3, matching what the corpus already
  pinned for the same construct in a non-template owner. Found while fixing #77.
  **Closes [#83](https://github.com/stop-cran/namespace2xml/issues/83)**; the choice of code is
  recorded as an open ambiguity in
  [#84](https://github.com/stop-cran/namespace2xml/issues/84).

- **A reference in a template's value silently corrupted its positional captures.** §12.1 says
  "legacy unnamed captures are substituted positionally", and the counter ran across the whole
  value — except that a value carrying a reference took a second code path which rebuilt each
  wildcard as a one-token value, restarting the counter at zero every time. `a.*.*.v=${a.k}-*-*`
  against captures `p` and `q` wrote `K-p-p` instead of `K-p-q`: wrong output, exit `0`, no
  diagnostic. The existing gate for the positional rule passed throughout because its value carried
  no reference. The two paths now share one counter, so they cannot disagree again. Found while
  fixing #77. **Closes [#82](https://github.com/stop-cran/namespace2xml/issues/82)**.

### Changed

- **A refusal from a closed set now names the set.** Issue 80 reported spending eleven attempts on
  `output=quoted-namespace`: the refusal said the value was "not one of the Section 16.1 output
  formats" without listing them, and `--help` offered no enumeration at all — worse, its English
  prose named the format `quoted-namespace`, a spelling the parser rejects, so the one place a
  reader could look actively misled them. Eight refusals across `SchemeCompiler`, `SchemeReader`,
  `StructuredSchemeReader` and `TypeSet` now append the accepted values, and `--help` gains a
  `SCHEME BASICS` section enumerating the seven Section 16.1 formats and the fifteen Section 15
  directives. Both lists are derived from the same tables the parsers match against, and
  `AcceptedValueTests` compares them against the specification's own order, so a format added to
  the parser and forgotten in the list fails the build. `--verbosity` and `--diagnostics-format`
  already enumerated their values; the unrecognized *option name* now points at `--help`.
  **Closes [#80](https://github.com/stop-cran/namespace2xml/issues/80).**

### Fixed

- **`type=string` bound to a path and was then ignored in JSON and YAML.** §16.6 says the
  directive "forces scalar rendering as a string in the selected output view", and both document
  formats rendered the inferred scalar instead: `s.a=8443` with `s.a.type=string` emitted the JSON
  number `8443`. `TypeSet.IsString` was defined and read by nothing. Only the XML projection
  consulted the §16 transform table at serialization time; `DocumentProjection` was never handed
  it. namespace2xml 2.4.0 renders this case correctly, so the defect was a regression in the
  rewrite rather than a missing feature, and the release would have shipped a directive that
  silently did nothing. Reported by exploratory testing against `3.0.0-preview.3` and now covered
  by `type-string-forces-document-scalar-rendering`, which also pins the two §13.2 rules that pull
  the other way: the directive does not travel forward along a reference to the referring node, nor
  backward to the referent. The transform table is now a required constructor parameter, so a
  future projection that forgets it does not compile.
  **Closes [#76](https://github.com/stop-cran/namespace2xml/issues/76).**

- **A Section 15 scheme rejection spelled its `source` with whatever separator the caller typed.**
  §6.4.3 fixes `source` as a relative path with `/` separators for anything inside the invocation's
  input set, and every other diagnostic gets that spelling from the loader. The XML-scheme refusal
  reports *before* the file is read, so it never reached the loader and quoted the raw argument:
  `-s schemes\scheme.xml` and `-s schemes/scheme.xml` named the same file and produced different
  diagnostic bytes. A regression introduced with the refusal itself, reported by exploratory
  testing against `3.0.0-preview.3`, and now covered by
  `diagnostic-source-spelling-is-independent-of-separator`.
- Three documentation defects found by the same round, all of them drift this project introduced
  rather than long-standing errors. `README.md`'s first example printed output the tool does not
  produce, in all three formats at once. `docs/format-xml.md` said the document element comes from
  the output selector; §16.3 says it comes from the selected view, and more than one top-level
  member is `TYPE001` until `root` supplies one. `docs/format-yaml.md` still described wildcard
  YAML mapping keys as refused with exit `70`, which the §10.4 resolution shipped in
  `3.0.0-preview.3` had already removed.

### Added

- **`--help` and `--version` now name every document an agent needs, release-pinned.** `--help`
  carries a seven-entry document index — the index itself, the specification, the diagnostic
  codes, the known limits, the reporting guide, the agent guide, and where to file — and says
  what each is for. `--version` gains `diagnostics`, `known-limits` and `documentation` fields
  alongside the existing `specification`. §6.4.1 fixes a *minimum* field set, so this needs no
  amendment.

  This closes a gap the exploratory round found repeatedly: `llms.txt` already indexed the whole
  documentation set, and nothing anywhere pointed at it. Agents reported reconstructing the
  document list by guessing at paths. Every link is pinned to the release tag, so the index an
  agent reads describes the binary it is running rather than a later branch.
- **The package carries the documentation set it indexes.** `llms.txt`, `AGENTS.md`,
  `CONTRIBUTING.md`, `KNOWN-LIMITS.md`, `docs/diagnostics.md`, `docs/usage-methodology.md`, the
  five format guides, and the remaining `spec/` artifacts now ship in the `.nupkg` beside the
  specification, at the paths `llms.txt` names them by, so the index resolves offline.
  `docs/migration-2.x-to-3.0.md` is deliberately omitted: 300 KB about the version this one
  replaces is the least useful document to carry and the most expensive.
- Gates for all of the above: the help text and the version output are each checked separately
  for the documents they must name — together, deleting a link from one passes while the other
  still carries it — and the project file is checked against `llms.txt` so a document added to
  the index cannot silently miss the package, or land at a path its relative links do not
  resolve to.

## [3.0.0-preview.3] - 2026-08-11

### Contract

- `contract-bundle` `r44+a91f25bf49ec`.
- §10.4: **A wildcard template over a sequence or an empty container is settled**, and with it
  the last path out of the tool that §6.3 did not define. A sequence beneath a wildcard key is
  extracted through its items' ordering values, so `a.*.b: [x, y]` declares exactly what
  `a.*.b.0=x` and `a.*.b.1=y` declare in namespace form and a native template and its namespace
  spelling are one rule written twice. An empty mapping or an empty sequence has no entries for
  entry-by-entry extraction to find and is `PARSE001` against §10.4, once per failing source.

  Both shapes previously exited `70` with an empty diagnostic stream — under
  `--diagnostics-format json` the machine-readable output of a failed run was literally `[]`.
  **Exit `70` is now unreachable from any input, and §6.3's `0` and `1` are total.** The
  refusal's stated premise — that a native sequence item's ordering value cannot be allocated
  before the destination is known — is answered by §12.4: "a generated contribution reserves or
  allocates ordering values only when it is generated." What the premise concealed was a
  question about §5.4 provenance, which §10.4 still does not state and which is filed as
  [#75](https://github.com/stop-cran/namespace2xml/issues/75).
- §15: **XML is no longer a scheme file format.** Scheme files use the case-insensitive `.json`,
  `.yaml` and `.yml` extensions, every other extension including none at all is a namespace
  profile, and a scheme file named `.xml` is `PARSE001` against §15 in the scheme phase, once
  per failing source, reported before the file is read. §15 had named XML once, about secure
  parsing, and never said how an XML document projects to a qualified directive path — an element
  and an attribute are distinct component kinds under §11.4 and only one can be an ordinary name
  part, the single root element sits where no JSON or YAML top-level key does, and §12.2 spells a
  capture `*`, which is not a legal XML name, so a wildcard selector cannot be written at all.
  **Caused by [#72](https://github.com/stop-cran/namespace2xml/issues/72).**

  Measuring 2.4.0 is what settled the direction, and it is the opposite of
  [#66](https://github.com/stop-cran/namespace2xml/issues/66). Given
  `<app><output>namespace</output></app>` saved as `s.xml`, 2.4.0 opens the file, produces no
  directives, writes nothing, and reports `Success! Exiting...` at exit `0`. Controls: the same
  text as `s.txt` reports a namespace parse error at exit `1`, so the extension causes the
  silence rather than the content; and a working `app.output=namespace` scheme over the same
  profile does write its file. **XML scheme files were never supported.** Excluding them therefore
  narrows nothing — it makes an existing silent no-op legible — and it is the only
  forward-compatible answer, because a specified error can become support later without breaking
  anyone whereas a guessed projection pinned by fixtures cannot be corrected without doing so.

  Naming §15 rather than §8.1 is the substance of the change. Reading the file as a namespace
  profile reports the right code against the wrong rule: it cites §8.1 at a document whose author
  never wrote it to that contract, and advises adding an `=` to syntax already correct for the
  contract they were aiming at.
- §12.1: **a `type` value and an `output` value are excluded from capture substitution.**
  §16.6 closes the type names and §16.1 closes the output formats, so a capture could complete
  either only by accident of the matched data. Capture recognition is now disabled in both values
  whatever the selector defines, an unescaped `*` in either is literal text, and it falls to the
  ordinary §16.1 or §16.6 value check that rejects it as `SCHEME001` in the scheme phase at the
  line the declaration was written on. The exclusion belongs to the directive and not to the
  declaration, so `cfg.*.output=*` and `cfg.output=*` are the same error. **Caused by
  [#74](https://github.com/stop-cran/namespace2xml/issues/74).** The accident is not hypothetical:
  2.4.0 substitutes into both, and the two new fixtures measure what that produces. With
  `cfg.*.type=arr*y` over a profile holding `cfg.a` alone, the capture text `a` completes
  `array` and 2.4.0 exits 0 having silently applied `type=array`; add `cfg.b` and the same
  scheme line yields `arrby`, which reaches `Enum.Parse` unguarded and terminates the process
  with exit `-532462766`. One declaration is a working directive or a process crash depending on
  which data it matches. `cfg.*.output=*` is sharper still, because `output` **creates** the
  output instance rather than binding to one, so the §14.1 expansion that supplies every other
  instance-scoped directive's captures runs after the value is read: 2.4.0 spliced the matched path
  part `json` into the format and wrote `json.json`, choosing both the format and the
  destination from the data.
- §8.2, §9.1, §10.4, §19.3 and §19.4: **a JSON or YAML mapping key now carries the §11.4 markers**
  instead of being "one ordinary literal component" that never acquires an XML node kind. `@x` is
  the attribute `x`, `#0` is a content component, `Q{uri}x` is a qualified element, and a leading
  backslash escapes `@`, `#`, `Q` or `\` and suppresses marker recognition for the whole key.
  Marker recognition commits as it does everywhere else, so a bare `"@"` or a `"#01"` is blocking
  `PARSE001` rather than silently becoming text. §19.3 escapes an ordinary component whose own text
  begins with a marker, so every key this tool writes reads back as the component it was written
  from. **Caused by [#51](https://github.com/stop-cran/namespace2xml/issues/51)**, which reported a
  duplicate-key document; investigating it found the larger defect underneath. The old rule made
  JSON and YAML the only formats that could not name an attribute, a content node, or a qualified
  element — so **the tool could not read its own JSON output back into XML** (blocking `XML002`,
  measured), and an XML attribute was unreachable from a JSON overlay while `.properties` reached
  it fine. That contradicts §4.4's guarantee that any value can be overridden, and it is the
  scenario the tool exists for: a large XML base specialized by a short list of environment
  overrides. 2.4.0 is no better and rather worse here — it wrote `#01=v` into a namespace file,
  where `#` starts a comment, and reported success.
  - **Removed**: the §8.2 sentence "JSON and YAML mapping keys are always one ordinary literal
    component and never acquire XML node kind from marker-shaped text", and the corresponding
    clauses in §9.1 and §10.4.
  - **Accepted cost**: a foreign JSON document whose keys are `@odata.type` or `@context` now
    contributes attributes rather than ordinary members, and `{"@": 1}` is `PARSE001` unless
    written `{"\\@": 1}`. This is a breaking change for JSON and YAML input, which 3.0 is the
    release to make.
  - §19.3 keeps `FLAT001` for a mapping-key collision, now as a backstop rather than the common
    case: escaping removes the ordinary-versus-attribute collision that #51 reported, and what
    remains is two components that are genuinely distinct and spell one key regardless. Emitting
    both would produce a duplicate-key document that §3.3 forbids and that this specification's own
    reader rejects. `KNOWN-LIMITS.md` §1.14 recorded that gap and is removed.
  - §26 gains acceptance item 87 and four fixtures, one of which was re-anchored and renamed from
    `…-json-yaml-marker-keys-stay-literal` to `…-an-escaped-json-key-stays-literal` because the
    rule it pinned is no longer the rule.
- §3.2 gains two causes of legacy behavior that is deliberately not preserved, both found by
  measuring 2.4.0 while implementing
  [#70](https://github.com/stop-cran/namespace2xml/issues/70) rather than by reading its source.
  Neither was covered by an existing bullet, and both are the kind of divergence a migrating scheme
  meets silently:
  - text a resolved scheme reference contributes being treated as scheme-written path, "so that a
    separator held by the referenced directive creates directory hierarchy and can compose a
    destination another output instance has already claimed". In 2.4.0 a `filename` referencing
    another selector's `dir/leaf.conf` writes into a `dir` directory, and where the two selectors
    then collide the baseline **appends** one output to the other — two selectors merged into one
    file, exit 0, no diagnostic. §16.2 already required the opposite; §3.2 now names the cause so
    that the divergence is triageable from the compatibility policy alone.
  - a scheme directive being discarded when its value could not be resolved, "so that an
    unresolvable or cyclic reference silently selects the default destination instead of failing".
    A typo in a scheme reference changes where output is written and reports success.
- §11.4 gains `WARN011`: a later unmarked component that is the simple alias of an XML component
  already at the node adds an ordinary sibling rather than overriding, and now says so. The model is
  unchanged — §8.2 scopes the alias index to references and scheme paths, deliberately — so this is
  an addition to the diagnostic surface, not to the transformation. **Caused by
  [#56](https://github.com/stop-cran/namespace2xml/issues/56)**, and the amendment rather than a fix
  is the decision: `a.x=v` against `<a x="base"/>` **overrode the attribute in 2.4.0**, measured,
  so a migrating profile gets a wrong target where it used to get the right one. The warning is
  withheld when both components arrive in one contribution — nothing was migrated there — and when
  the later one is written `Q{}x`, which §11.4 already has bypass the alias index outright.
- §16.9 now says `NewLineOnAttributes` "places every attribute on its own line, including the
  first", where it previously said "each attribute after the first". **Caused by
  [#53](https://github.com/stop-cran/namespace2xml/issues/53)**, which measured the divergence and
  routed the decision to review rather than to whichever side was cheaper. The clause moved rather
  than the code because obeying the old wording meant not using `XmlWriterSettings` and hand-writing
  start tags, taking attribute escaping, namespace declaration placement and mixed content back from
  `XmlWriter` for a cosmetic, opt-in flag. `KNOWN-LIMITS.md` §1.18 is kept as the record of that
  reasoning rather than deleted.

### Added

- **`WARN010` is emitted (§3.2, §8.7, §22).** A JSON or YAML mapping whose keys are all canonically
  numeric is projected as a sequence, and that projection is now reported at the cardinality §22
  states: **once per source contribution, canonical mapping path, and output instance.** Both
  qualifiers earn their place. Naming the source means each document that wrote keys into the
  mapping is listed, because a single warning at the path would tell an operator that *some* file
  had lost its keys without saying which one to edit — and in the output those files are
  indistinguishable, each having contributed one array element. Scoping to the output instance
  means `type=mapping` silences the warning only where it applies, so a model rendered to two
  destinations keeps the keys in one and is still warned about the other.
  Two exclusions follow from the same rule and are pinned: a native sequence raises nothing, having
  had no inference applied to it, and a namespace-format contribution to the same node raises
  nothing, because a numeric path segment makes no shape claim to contradict.
  **Caused by [#58](https://github.com/stop-cran/namespace2xml/issues/58)**; `KNOWN-LIMITS.md` §1.7
  is resolved and acceptance item 68 is now `required`, with five assertions and two fixtures.

  This was the last registry code without a call site. The audit that tracks them was itself stale:
  it named `SCHEME002` as the other survivor, and `SCHEME002` had acquired a call site during
  M4–M8 — verified here by running it, not by re-reading the audit. Every code in
  `spec/diagnostics.registry.json` is now reachable, so any future audit hit is a defect rather
  than an expected baseline.

  `KNOWN-LIMITS.md` §1.7 had explained the gap as needing "per-source provenance the overlay does
  not retain", and said §1.8 needed the same change. Both halves were wrong, and the estimate
  outlived the two previews that shipped without the warning. The warning needs one fact — "a
  native JSON or YAML document wrote a mapping here" — decided at read time and carried on the node
  as a set; it does not need the general per-contribution model §1.8 wants. §1.8's cross-reference
  is corrected and its own estimate is now marked unverified.

  - **Removed**: `KNOWN-LIMITS.md` §1.7 as a live limit, and the five places in `docs/format-json.md`,
    `docs/format-yaml.md` and `docs/format-xml.md` that told readers a warning was not emitted.
    `docs/format-xml.md` was also telling readers that an ambiguous unmarked scheme selector
    "resolves to the element component rather than warning", which had stopped being true.

- **A conformance fixture's expected diagnostic stream had been captured rather than authored.**
  `type-mapping-suppresses-warn010-per-output-instance` claimed acceptance item 68 while asserting
  an *empty* stream, and its own `legacy.md` prose said, correctly, that the un-suppressed instance
  "does raise it". The empty file recorded the tool's silence at the time it was written. It is
  replaced with the stream §22 requires. This is the failure mode `AGENTS.md` names as the easiest
  to commit quietly, and it is worth recording that it was committed: a fixture can assert less
  than its title, its requirement claim and its own prose all say it does, and nothing fails.

- **A scheme directive's value may contain references (§15.1 step 1).**
  A directive can be assembled from another directive's value, as in
  `cfg.filename=${cfg.root}-final.conf`. Resolution reads the §15.2 winner rather than the nearest
  declaration above it, so a later override is what a reference sees, and forward references work.
  A missing reference is `REFERENCE002`, a cycle is `REFERENCE003` reported once per canonically
  distinct cycle, and the chain is bounded by `--max-reference-depth` (`LIMIT001`) — the same three
  rules §13 states for input values, applied in the `scheme` phase. The feature is narrower than it
  looks: §15's "the final qualified-name part identifies a directive" means a scheme reference can
  only name another *directive*, so `${a.name}` is missing even when `a.name` is in the input.
  **Caused by [#70](https://github.com/stop-cran/namespace2xml/issues/70)**; `KNOWN-LIMITS.md`
  §1.4.1 is removed.

  Implementing it surfaced a clause that a naive splice would have violated silently. §16.2 says a
  reference's "resulting text is opaque segment data: `/` or `\` supplied by a reference is encoded
  and never creates a directory", and resolving a reference into an ordinary literal destroys the
  only thing that distinguishes referenced text from written text. 2.4.0 does exactly that — a
  `filename` referencing another selector's `dir/leaf.conf` writes into a `dir` directory, and where
  that collides with the referenced selector's own destination the baseline **appends one output to
  the other**, merging two selectors into one file with no diagnostic. The resolved text now carries
  its provenance to §16.2 composition, and the opacity survives a further reference to the value
  that holds it.

- **A wildcard token in a native JSON or YAML key is a wildcard template again (§10.4, §9.2).**
  A mapping key carrying an unescaped `*` or `*[identifier]` is extracted entry by entry before
  structural merging and expanded in the §12.4 fixed point, so `a: {'*': {c: XXX}}` enriches every
  record of a later `a:` sequence, and the capture is substituted into the template's own value —
  `c: v-*` yields `v-db` and `v-web`. `\*` keeps a literal asterisk, and §16.7 `substitute=None`
  reaches native keys to reach the same result by the other route, which 2.4.0 did not do because
  `substitute` was namespace-input only. **Caused by
  [#57](https://github.com/stop-cran/namespace2xml/issues/57)**, the only case in the corpus where
  2.4.0 satisfied the specification and this implementation did not; `KNOWN-LIMITS.md` §1.1 and
  §1.2 shrink accordingly. Two shapes beneath a wildcard key are **still declined** with exit `70`
  — a sequence, whose §5.4 ordering value cannot be allocated before the destination is known, and
  an empty mapping, which has no scalar leaf and expresses the mapping presence §10.4 denies a
  template. §10.4 shows neither shape, so refusing is narrower than guessing; both refusals are
  pinned by fixtures that change at the commit which settles them.
  Eight conformance fixtures cover the capability, every one measured against 2.4.0 — which
  **diverges on six of them**, including emitting a literal `*` key *unescaped* (its own output,
  re-read by its own reader, turns data into a rule) and exiting `0` with **no output file at all**
  for a sequence under a wildcard key. Sibling order follows §5.3, not §10.4's worked example,
  which contradicts it; that is filed separately as
  [#73](https://github.com/stop-cran/namespace2xml/issues/73) and one fixture deliberately uses the
  argument order under which both readings agree, so a decision on #73 cannot take the capability
  with it.

- **A wildcard capture is substituted into every directive value the specification asks it to
  (§12.1).** The clause says "a scheme directive's value is decided the same way, from the captures
  its selector defines"; this build performed it for `filename` alone. It now performs it wherever
  the captures are bound at the point the value is read: `root`, `delimiter`, `output`, `filemerge`
  and the four output-option directives take them from the §14.1 expansion at pipeline step 13, and
  `key` takes them from its own per-path match at step 16, so one `n*.key=*_id` rule names a
  different field at every path it matches.
  **Closes [#71](https://github.com/stop-cran/namespace2xml/issues/71)**, with one directive left
  and its reason recorded: §16.6 closes `type` to a keyword set and §22 fixes the phase its
  rejection is reported in, so substituting late would move an observable the contract pins. See
  `KNOWN-LIMITS.md` §1.4.2.
- **The literal half of §12.1 was wrong and is fixed.** "In a scheme whose selector contains no
  wildcard, `*` in a `filename`, `root`, or `delimiter` value is literal text" — but `a.root=r*`
  was a blocking `SCHEME001`. The value lexer did decide the asterisk was text; §16.3's compiler
  then re-lexed that text under the *name* grammar, where a bare `*` is a wildcard token, and
  rejected it. A decision made once has to be carried, not remade by the next grammar to see the
  same characters. `KNOWN-LIMITS.md` had recorded this case as already correct, **unverified**;
  it was not.
- **Three refusals became precise scheme errors.** Auditing what a correct build would do with a
  wildcard in each directive's value showed that `merge`, `substitute` and the three input-option
  directives never needed the refusal at all: §16.8 and §16.10 forbid a wildcard-qualified
  input-option or `merge` *selector* outright, and §16.7 closes `substitute` to four keywords. Each
  now reports the rule it broke at the line it was written on. One of them had been claiming in its
  refusal text that "the same declaration with no `*` in its value runs" — which was false, because
  the selector was the defect.
- `conformance/a-capture-substitutes-into-root-and-delimiter`,
  `conformance/a-capture-repeats-for-a-shorter-name-and-is-ignored-when-unused`,
  `conformance/a-capture-substitutes-into-a-key-field-name` and
  `conformance/an-asterisk-in-a-directive-value-under-a-literal-selector-is-text`, with
  `tools/mutate-capture-substitution.ps1` proving all four red against six mutations. 2.4.0 differs
  on all four: it drops a `root` whose value contains an asterisk **silently**, ignores a
  wildcard-qualified `key` entirely, and takes only the last selector part as a default file name.
  It does substitute captures into `root` and `delimiter` when the selector expands, so the
  regression is narrower than the refusal it replaced implied.

- **Scheme files written as JSON or YAML (§15) are read.** A structured scheme projects to the same
  directive stream a namespace-profile scheme does: a mapping that has properties is a path and is
  recursed, and **everything else is a declaration site**, so no value is silently walked past. A
  `*` key keeps its §10.4 wildcard-template meaning, and captures substitute into `filename` as they
  do from a profile. §15's value sentence is enforced at each declaration: a sequence, an empty
  mapping, a null and an empty string are each a blocking `SCHEME001` naming that declaration's own
  line and column.
  **Closes [#66](https://github.com/stop-cran/namespace2xml/issues/66)**, and the close corrects
  its framing. The issue was filed as a missing feature; measuring namespace2xml 2.4.0 showed it
  **reads JSON and YAML schemes correctly today**, differing only in §24's output bytes. The
  refusal was therefore a **regression**, and the exit-`70` message that called it unimplemented was
  wrong about the baseline as well as about the contract. XML scheme files remain refused, now for
  the honest reason: §15 names them once and never says what one projects to, tracked as
  [#72](https://github.com/stop-cran/namespace2xml/issues/72).
- `conformance/a-json-scheme-nests-directive-paths`,
  `conformance/a-yaml-scheme-key-carries-a-wildcard-template` and
  `conformance/a-container-value-in-a-structured-scheme-is-scheme001`. The first two are
  **compatibility fixtures**: 2.4.0 agrees with them character for character inside the payload, so
  they pin a claim rather than a change. The third is the divergence — 2.4.0 exits 0, logs
  `Success!` and writes nothing at all when three directives carry unusable values, which is the
  failure mode §15's value sentence exists to prevent.
- **The `substitute` directive (§16.7) is implemented.** It was the larger of the two remaining
  exit-`70` refusals. `substitute` selects, at the node it matches, whether references and value
  wildcards are interpreted in an entry's **name**, its **value**, both, or neither, with the four
  modes `All`, `Key`, `Value` and `None`; the deprecated 2.x spelling `keyOnly` is accepted as `Key`
  and reported once per scheme as `WARN002`. §13.4's exemption comes with it: a native JSON, YAML or
  XML string matched by `Key` or `None` is preserved exactly after native decoding, with no
  Appendix A.5 transducer pass — implemented by not lexing the native string at all, because a lex
  with interpretation merely switched off still consumes `\*` and `\${`.
  **Closes [#65](https://github.com/stop-cran/namespace2xml/issues/65)**, whose framing was wrong on
  two counts and is corrected in the close: `substitute` does not rewrite values, it withholds
  interpretation from them, and the ordering it called unsettled is fixed by §15.1 step 6.
- `conformance/substitute-none-disables-interpretation-at-the-matched-node`,
  `conformance/substitute-key-preserves-a-native-string-exactly` and
  `conformance/substitute-keyonly-is-a-deprecated-alias` pin the three discriminations a plausible
  wrong implementation fails: that the directive scopes to the node it matches rather than the
  subtree beneath it, that the name and value columns of §16.7's table are independent, and that the
  deprecated alias is honoured *and* reported. All three verdicts are measured: 2.4.0 **differs** on
  each, agreeing on every semantic decision and diverging only in §19.1 output encoding — plus, in
  the `Key` case, by never having run §8.3's native-string transducer at all.
- **A pattern in a `substitute` directive matches a template by its spelling.** A declared path that
  is itself a template carries a wildcard token, and the ordinary matcher requires the concrete side
  of a match to be literal text, so before this no `substitute` pattern could reach a template — and
  the name column of §16.7's table, whose only reachable subjects are names with something to
  interpret, was dead. A wildcard-bearing declared path is now literalized before matching, so a
  wildcard component contributes the characters that wrote it. This is what 2.x did, measured:
  `QualifiedNameMatchDictionary.TryMatch` string-matched the component's rendered text. Legacy's
  reverse branch, which let a concrete `a.x.b` govern a declared `a.*.b`, is deliberately **not**
  carried: §15.1 step 6 speaks of the declared pre-expansion path.

- **§15.2's simple alias now applies to scheme paths, and `SCHEME002` has a call site.** A directive
  written `a.x.type=…` binds an attribute written `a.@x` or a qualified element `a.Q{uri}x`, which is
  the 2.x spelling the clause grants "for compatibility and convenience" — measured: 2.4.0 does
  reach an attribute that way. A marked component still selects one outright, so `a.@x` binds the
  attribute alone and `a.Q{}x` the element alone. Where an unmarked component reaches both, the run
  is refused with `SCHEME002` naming the two canonical alternatives rather than picking one.
  **Caused by [#56](https://github.com/stop-cran/namespace2xml/issues/56)**, and no contract change
  was needed: §15.2 has said this since r1 and the implementation had not caught up.
- `conformance/scheme-an-unmarked-directive-reaches-an-attribute-through-the-simple-alias` and
  `conformance/scheme-an-ambiguous-simple-alias-is-blocking` pin the alias, both marked escapes, and
  the blocking ambiguity. Their differential verdicts are measured: the first **crashes** 2.4.0 on
  the `Q{}` line, and the second is where 2.4.0 is at its worst — given
  `<r><a x="1"><x>2</x></a><b x="3"><x>4</x></b></r>` and one ambiguous wildcard directive it exits
  `0` and writes `<a x="" /><b x="" />`, destroying both attributes' values, both child elements and
  both elements' text without a word. That silent loss is what makes the refusal worth the friction.
- `conformance/scheme-a-wildcard-does-not-reach-an-xml-component-through-the-alias` pins the
  boundary of that alias: a **wildcard** does not consult the index, because the index is a lookup
  by written name and a wildcard writes none. A wildcard reaching an attribute and an element of one
  name is a pattern matching two components, not an ambiguous alias — and `SCHEME002`'s remedy, to
  mark the component and name one outright, is the one thing a wildcard cannot do. The fixture is a
  regression test: the wider reading turned `r.a.*.type=ignore` over `<a x="1"><x>2</x></a>` into a
  blocking error, and turned the real 606-line `logback.xml` from
  [#24](https://github.com/stop-cran/namespace2xml/issues/24) from a clean 1243-line run into
  `TYPE001`. 2.4.0 **crashes** on it with an unhandled `InvalidOperationException`.
- `conformance/xml-an-unmarked-alias-warns-only-when-it-follows-an-xml-component` pins `WARN011`'s
  positive case and **both** of its withholdings in one invocation, so a future implementation
  cannot satisfy the warning by emitting it everywhere. Its differential verdict is measured:
  2.4.0 **crashes** on the `Q{}` line, having no such production at all.
- `conformance/empty-container-versus-scalar-picks-the-later-shape` pins §4.4's exclusive-shape
  contest for an **empty** container in both source orders, for a JSON and a YAML destination.
  **Caused by [#68](https://github.com/stop-cran/namespace2xml/issues/68)**. The corpus previously
  pinned only the non-empty form, and the empty case is the one where a wrong answer is hardest to
  see: the winning shape carries no content, so nothing in the file distinguishes a correct empty
  mapping from a dropped one.
- `conformance/xml-newline-on-attributes` pins the §16.9 attribute layout, which **no fixture
  exercised at all** — the corpus gap that let the divergence survive, and the half of #53 worth
  closing first. Its differential verdict is measured, not assumed: 2.4.0 **crashes** on
  `cfg.e.@a=1` with an unhandled `XmlException`, after opening the destination, leaving a zero-byte
  file behind.

- `conformance/key-on-a-sequence-only-target-is-type001` pins §16.5's "applying `key` to a
  sequence-only or scalar-only target is `TYPE001`", which no fixture asserted. It exists to hold up
  an argument rather than to catch a suspected defect: it is one of the two rules that makes §16.5's
  *other* clause — the merge with an independent sequence projection already at the node —
  unreachable, and [#61](https://github.com/stop-cran/namespace2xml/issues/61) now proposes deleting
  that clause on those grounds. An amendment resting on a behaviour needs the behaviour pinned. Its
  differential verdict is measured, with a control: 2.4.0 exits `0` and writes the sequence
  unchanged, **byte-identical to the same run with the `key` line deleted**, so the directive was not
  applied differently but ignored outright.
- `tools/check-known-limits-issues.ps1` makes `KNOWN-LIMITS.md`'s own stated invariant — "every entry
  that owes a resolution names the issue that owns it" — checkable, and runs in the `lint` job. A
  plain `[#59](…)` link must name an **open** issue; a `[#58 (closed)](…)` link must name a
  **closed** one. The check is bidirectional, so it catches an entry that outlived its defect *and* a
  `(closed)` annotation applied too early, and a reader can now see which citations are live work
  without following any of them. Proven red before it was trusted: it found **eight** stale links
  where reading the file by hand had found six.
- `conformance/xml-a-comment-among-element-only-children-is-addressable` pins §11.4's assignment of
  content tokens by "every parent, including element-only parents" together with §17.4's rule that
  comments alone do not make a parent mixed-content. No fixture covered that combination — the one
  added for #60 uses mixed content, where element children are addressed positionally anyway, so it
  could not distinguish the two rules. Its differential verdict is measured: 2.4.0 **differs**,
  writing `<r b="" d="" />` and ignoring the directive entirely.

### Fixed

- **A destination `filemerge=replace` no longer destroys an item at a path it did not name.**
  §17.5 requires each output contribution to carry its "complete per-path high-water map,
  including marks raised by items hidden by output projection", and says `replace` "discards the
  visible accumulated projection without lowering the destination high-water mark". The preview
  kept that mark only for paths the replacement itself named. Where it did not, a later
  contribution recreating the path allocated from zero, and a further contribution addressing an
  explicit ordering value then landed on top of it: two items became one, silently, at exit `0`.
  **Caused by [#62](https://github.com/stop-cran/namespace2xml/issues/62).**

  The mark is now retained on a node that carries nothing else, and every such carrier no
  contribution recreated is removed once the fold ends — otherwise an exclusive destination
  rendered the removed path back as an empty mapping, and an XML destination failed outright. The
  carrier's position mark is the one that loses every §5.2 comparison, because §17.5 enumerates
  the high-water mark as the only thing surviving a replacement: a recreated path takes the
  position of the contribution that recreated it, not of the one the replacement discarded.

  The fix is narrower than the issue assumed. The recorded objection — that materialising a node
  to hold the mark puts a path §16.10 has just removed back where wildcards, references and
  selectors find it — is true of the step-8 input merge and not of the step-18 destination fold,
  which §17.5 says "operates on fully transformed contribution models after `type`, `key`, and
  `root` have been applied". Retention is enabled for the fold alone.

- **A native wildcard template over a sequence or an empty container no longer refuses the
  run.** The preview returned the out-of-contract exit `70` for a YAML or JSON template whose
  value was a sequence, and for one whose value was an empty mapping. A sequence now generates,
  extracted through its items' ordering values; an empty mapping and an empty sequence are
  `PARSE001` at exit `1`, once per failing source. **This was the last exit-`70` path**, so
  §6.3's `0` and `1` now cover every invocation. The refusal was also the only failure mode that
  produced an empty diagnostic stream: `--diagnostics-format json` emitted `[]`, a
  machine-readable failure carrying no machine-readable reason.

- **An XML scheme file no longer refuses the run.** The preview returned the out-of-contract exit
  `70` for `-s scheme.xml`. It is now an ordinary `PARSE001` at exit `1` anchored at §15,
  emitted once per failing source so two XML schemes report two diagnostics, and the extension is
  matched case-insensitively so `.XML` is rejected too.

- **A capture-shaped `type` or `output` value no longer refuses the run.** The preview returned
  the out-of-contract exit `70` for `cfg.*.type=arr*y` and `cfg.*.output=*`, naming "a wildcard
  capture substituted into a directive value" as a capability it lacked. §12.1 now says both values
  are literal text, so each is an ordinary `SCHEME001` at exit `1` reported in the scheme phase
  at the declaration's own line. Nothing is published, including the well-formed instances that
  share the scheme file. This was the first of three exit-`70` paths removed for this release;
  §6.3 defines only `0` and `1`, so no released build may return `70`.

- **`merge=append` silently destroyed contributions.** §16.10 makes a contribution sequence-eligible
  either as a native sequence or as "a nonempty all-in-range-canonical-numeric mapping", but `append`
  read only the first of those on the accumulator side: it seeded the result from the earlier node's
  sequence projection, which is empty when the earlier contribution is spelled as an ordering-value
  mapping, and then rebased the later items onto it. Every item the earlier contribution supplied was
  absent from the result. It survived end to end in the common case only because the merged node also
  retains the earlier node's children and §8.7 re-infers a sequence from them — but where those
  children are not retained, as under a §12.4 wildcard rule, the items were gone. Three input files
  under `a.p.merge=append` published two items where the contract requires three, at exit `0` with an
  empty diagnostic stream. Both sides are now read through the same route.
  `conformance/wildcard-generation-appends-beside-every-contribution` pins it. The neighbouring
  `wildcard-generation-merges-at-rule-position` could not: both of its roots use `merge=replace`,
  which discards the earlier value by design, so the corpus was invariant under the defect.
- Two unit tests asserted the *internal* sequence projection after an `append` and had silently
  encoded the defect above, expecting the earlier contribution's items to be absent from it. Their
  own documentation quotes §16.10 on rebasing the **later** items, which the corrected expectations
  still satisfy; what they additionally asserted was that `append` had nothing to append to.
- **A capture in an `output` value terminated the process with an unhandled
  `NullReferenceException`** and exit `-532462766`, which §6.3 forbids without qualification. Every
  other instance-scoped directive takes its captures from the §14.1 expansion at pipeline step 13,
  but `output` is what *creates* the instances that expansion binds to, so its value was read with
  nothing bound and a null dereference followed. It now reaches the same documented refusal as
  `type`, and the contract question both raise is
  [#74](https://github.com/stop-cran/namespace2xml/issues/74). Found by re-testing a
  `KNOWN-LIMITS.md` claim that carried a **verified** marker and asserted the opposite; the marker
  recorded that someone had once looked, not that the claim was still true.
- The refusal message for a capture in a directive value said this build "substitutes captures into
  `filename` alone". It substitutes into `root`, `delimiter`, `filemerge`, the four output-option
  directives and `key` as well, and the message now names them. An author reading the old text
  would have concluded six working capabilities were missing.
- `KNOWN-LIMITS.md` §1.3 contradicted itself about XML comments among element-only children,
  claiming in one paragraph that such a comment "has no address at all" and cannot be reached by
  `type=ignore`. Both halves are false and were measured to be: the comment takes a content token
  whose index counts its element siblings, `a.#1.type=ignore` removes it, and `${a.#1}` returns
  `REFERENCE005` — proving the address exists. It is the *element* children that have no positional
  address there. Pinned by the fixture added above so the file cannot drift back.
- `conformance/xml-canonical-addresses/legacy.md` understated the 2.4.0 defect it records. Measured
  across nine shapes, 2.4.0 discards **all** XML element text content on import, unconditionally and
  silently at exit `0` — `<r><b>1</b></r>` yields `b=` and `<r>hello</r>` yields `=`. Attributes
  survive. The note had said text was dropped only when attributes or children were also present.
- Two conformance cases carried a **fabricated** legacy observation, and both were published in the
  generated `docs/migration-2.x-to-3.0.md`: `json-and-yaml-render-one-exclusive-shape` said "JSON
  and YAML output did not exist" in 2.4.0, and `json-output-options-and-escaping` said "there was no
  JSON output". 2.4.0 emits both formats. Measured against the pinned package, its actual
  shortcomings are different and more interesting — it made the same §4.4 shape choices *silently*,
  ignored every `jsonoutputoptions` flag without reporting the unrecognized directive, ended JSON
  files without a final newline, and wrote `Environment.NewLine` rather than LF. Both observations
  are replaced with what was measured. The verdicts themselves were correct, so the differential
  lane stayed green: it checks the verdict, and cannot check the prose.


### Changed

- The `TYPE001` raised when an XML view has several top-level members now names
  `xmlinputoptions=NormalizeFormattingWhitespace` when the surplus members are formatting
  indentation, instead of reporting a root count that is true but unactionable. The hint is
  withheld from a genuinely multi-rooted view, where that mode would not help. §6.4.3 makes
  `message` prose that is never compared, so this needs no contract revision.
  **Caused by [#40](https://github.com/stop-cran/namespace2xml/issues/40)**, and it is the most
  likely explanation of [#24](https://github.com/stop-cran/namespace2xml/issues/24).
- `--help` and `README.md` gained a section on reading XML that was formatted for humans. The
  whitespace modes were already documented in `docs/format-xml.md`,
  `docs/usage-methodology.md` and `KNOWN-LIMITS.md`, and absent from the two surfaces a reader
  meets first — which is what made an ordinary indented file a silent trap rather than a
  documented trade.

## [3.0.0-preview.2] - 2026-08-09

The preview that first transforms **every input format and every output format the specification
defines**, end to end. `3.0.0-preview.1` read namespace profiles only.

### Contract

- `contract-bundle` `r37+2d644be6926e`, covering `docs/specification.md` and
  `spec/diagnostics.registry.json`. Seven revisions since `r30`.
- §11.4 now defines *format-agnostic alias* and makes `Q{}local-name` the explicit canonical
  spelling of an unqualified element, so a path can be written that bypasses the alias index.
  **Caused by [#43](https://github.com/stop-cran/namespace2xml/issues/43)**, which observed that the
  term was used but never defined and that the spelling matched nothing.
- §12.2 no longer makes an inconsistent repeated capture a silent nonmatch; it is `WILDCARD001`, as
  §22 and Appendix B already said. **Caused by
  [#44](https://github.com/stop-cran/namespace2xml/issues/44)**, a contradiction between three
  clauses.
- §17.1 defines what an explicit later ordering value *patches*. **Caused by
  [#45](https://github.com/stop-cran/namespace2xml/issues/45)**.
- The `rule` diagnostic member has a normative value, so `WILDCARD002` can be given a fixture at
  all. **Caused by [#46](https://github.com/stop-cran/namespace2xml/issues/46)**.
- §22 lists diagnostic members **per condition** rather than per code, which is what a fixture
  author needs. **Caused by [#47](https://github.com/stop-cran/namespace2xml/issues/47)** — the
  report that blocked an acceptance item until it was resolved.
- §11.4 records the content-token placement question rather than leaving it to whoever implemented
  it first.
- Appendix C gained the run-B token-vector rules and the legacy differential lane (C.6).

### Added

- **Input front ends for JSON (§9), YAML (§10) and XML (§11)**, each with the §15.1 projection and
  secure default parsing. XML brings canonical addressing for attributes, namespaces, repeated
  children, mixed content, comments and CDATA; retained comments as ordered content nodes; the
  §16.8 input options and §11.7's `NormalizeFormattingWhitespace` compatibility mode.
- **§12 wildcard template evaluation**, §8.6 permanent exclusion masks and §8.7 numeric-map sequence
  inference, with the wildcard fixed point bounded and the bound's spender named.
- **§13 reference resolution**, including cycle canonicalisation with an injective ring identity.
- **Step 16 path-scoped view transformations** — §16.5 `key` and §16.6 `type` — and wildcard output
  selector expansion into concrete instances.
- **Output planning**: §16.2 destinations composed from the template, the §17.5 destination
  collision fold, and per-path high-water marks carried through a replace.
- **Rendering for every output format**: namespace, quoted namespace, INI, JSON, YAML and XML,
  including the four XML node kinds a `type` directive can name and the §19.5 sequence projection.
- **§21 validation and secure publication**, publishing through handle-relative no-follow filesystem
  opens rather than path-based ones.
- **A differential lane against namespace2xml 2.4.0** (Appendix C.6). Every fixture carries a
  measured verdict and prose saying *why* it diverges, and `docs/migration-2.x-to-3.0.md` is
  generated from those notes rather than written.
- **Format guides** for all five formats, and `docs/usage-methodology.md`, which now carries a
  worked cross-format specialization pipeline and the fixture-pinning discipline.
- **A tool-install gate** that installs the packaged tool on all three platforms in CI, so a package
  that cannot be installed fails before a tag rather than after one.
- Change-protocol rule **C7: evidence must be able to fail**. A gate nobody has watched go red is
  not a gate.
- `KNOWN-LIMITS.md` §1.1–§1.20 and §2.1: every limit that owes a resolution now names the issue that
  owns it, so the file states current behaviour and the tracker holds the argument.

### Fixed

- `--max-depth` beyond what this build can walk is now `CLI001` rather than a stack overflow the
  runtime gives no opportunity to report.
- The unspellable-path diagnostic fallback is injective; two distinct paths no longer print alike.
- `WARN009` no longer fires for a `filename` bound to a wildcard output match, where the filename
  was also being ignored. **Caused by
  [#50](https://github.com/stop-cran/namespace2xml/issues/50)**.
- Exact scheme directives now bind to the instance a wildcard literalizes.
- `merge=append` no longer silently accepts a non-sequence accumulator.
- The output root is derived *after* step 16, not before it.
- YAML positions are reported on §22 lines rather than YAML 1.1 lines.
- Sixteen input-reader and YAML-writer defects found by dual-model independent review across M2 and
  M5 — seven in the readers, nine in the YAML writer over two hardening passes.
- The cross-OS determinism gate hashes dotfile outputs, which it had been silently under-reporting.

### Changed

- A destination diagnostic is numbered by the §21.3 order, and a refused transform or destination
  fold is reported once per output instance rather than once per contribution.
- An acceptance item that no fixture can express is now discharged by a **named gate** instead of
  being left uncovered; 85 of 86 items are covered, and the manifest says where the last one is.
- **Release notes are the released version's own section**, prefixed with its contract revision and
  a tag-pinned specification link. The workflow passed the entire changelog, which buried each
  version under every earlier one and would have got worse with every release. The step runs before
  the nuget.org push and fails if the section is missing, so a changelog omission stops the release
  instead of leaving a published package with nothing to explain it.

### Removed

- `KNOWN-LIMITS.md` entries that had become false, including a claim that XML output had not landed,
  a `multiline` directive that does not exist, and a `substitute` described as "parsed and not
  applied" when it is refused with exit `70`. A limits list that overstates the gaps is as
  misleading as one that hides them.

## [3.0.0-preview.1] - 2026-08-06

### Contract

- `contract-bundle` `r30+35e144372ca0`, covering `docs/specification.md` and
  `spec/diagnostics.registry.json`.
- The specification is now committed to the repository at `docs/specification.md`, is shipped inside
  the NuGet package, and is the hashed root of the contract bundle. Previously it lived outside the
  repository, which made "which contract does this binary implement" unanswerable.
- Added §6.4, the structured diagnostics contract: the `--diagnostics-format` option, the argument
  pre-scan that resolves the encoding before validation can fail, the text encoding, and the
  canonical JSON encoding with a closed schema and a fixed byte layout.
- Rewrote §11.1 to state which bound governs each XML aspect, added `--max-xml-attributes`, and
  closed the entity-expansion question: because document type definitions are prohibited, decoded
  length can never exceed encoded length, so an implementation must not impose an expansion budget.
- Extended §22 so that a diagnostic carries its phase and the specification anchor it enforces, and
  so that the specification and the registry form one versioned contract bundle that `--version`
  reports.
- Added acceptance items 81–85 covering the above.

### Added

- Complete rewrite of the implementation for 3.0. The 2.x code is removed rather than migrated.
- `--diagnostics-format json`: the whole diagnostic stream on standard error as one canonical JSON
  array, written once at exit, with operational messages suppressed.
- `--version` now prints machine-readable `<field>: <value>` lines including the `contract-bundle`
  revision, the specification digest and the registry digest.
- Portable conformance corpus under `conformance/`, with a harness whose own self-tests include
  cases that must fail, so a comparer that never fails cannot ship unnoticed.
- Contract drift gates: the diagnostics registry, the diagnostic stream schema and the acceptance
  manifest are all generated from the specification, and CI fails if the committed artifacts do not
  match a regeneration.
- Determinism is measured rather than asserted: the corpus is hashed on Linux, Windows and macOS and
  the hashes must be identical.
- `AGENTS.md`, `llms.txt`, issue forms and the report protocol in `CONTRIBUTING.md`, making the tool
  discoverable and arguable-with by automated agents.
- Symbol packages, SourceLink and build provenance attestation on every release.

### Changed

- Target framework is now `net10.0`.
- **Releases are published from tags only.** The 2.x workflow published to nuget.org on every push to
  `master`, which meant any commit reaching the default branch was published under the trusted
  package name without a separate reviewed act.
- Publication uses a credential that expires — nuget.org trusted publishing, exchanging the run's
  OIDC identity for a short-lived key — rather than a stored API key.
- Direct dependencies reduced from twelve to four.

### Removed

- The 2.x implementation in its entirety.
- The `.NET 9` workflow.

---

## Contract revision log

| Revision | Date | Change | Caused by |
|---|---|---|---|
| `r1` | 2026-08 | Initial bundle: specification plus generated diagnostics registry. | — |
| `r2` | 2026-08 | Appendix C run-B token-vector rules; `expected-diagnostics.json` absence means no stream. | Fixture authoring for the informational modes, which the earlier text could not express. |
| `r3`–`r30` | 2026-08 | Not logged individually. These revisions were taken during the pre-preview specification work, before the first tag; each is recoverable from the history of `spec/contract-bundle.json`. Per-revision logging resumes below. | — |
| `r31`–`r37` | 2026-08 | §11.4 alias definition and `Q{}local-name`; §12.2 repeated-capture `WILDCARD001`; §17.1 patch semantics; normative `rule` member; §22 members listed per condition; §11.4 content-token placement question; Appendix C.6 legacy differential lane. | [#43](https://github.com/stop-cran/namespace2xml/issues/43), [#44](https://github.com/stop-cran/namespace2xml/issues/44), [#45](https://github.com/stop-cran/namespace2xml/issues/45), [#46](https://github.com/stop-cran/namespace2xml/issues/46), [#47](https://github.com/stop-cran/namespace2xml/issues/47). |

## `CONTRIBUTING.md` revision log

| Revision | Date | Added | Removed | Caused by |
|---|---|---|---|---|
| 1 | 2026-08 | Initial: ownership chain, rules C1–C6, four-route feedback protocol, two worked examples. | — | — |
| 2 | 2026-08 | Rule C7, evidence must be able to fail; §7 judgment list for specializing a foreign document and pinning depended-on behaviour. | — | Gate verification work that found three kinds of false survivor in the mutation harness. |

## 2.x

See the [2.x releases](https://github.com/stop-cran/namespace2xml/releases?q=v2). That line is
superseded and receives no further changes.
