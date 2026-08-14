# Known limits

**Describes the `v3` branch at contract bundle `r90+e172e0ba4d2a`. Dated 2026-08.**

This file tracks the branch, and the branch runs ahead of the last published preview:
`3.0.0-preview.3` carries `r44+a91f25bf49ec`, `3.0.0-preview.2` carries `r37+2d644be6926e`, and
`3.0.0-preview.1` carries `r30+35e144372ca0`. Where an entry is marked *(resolved)* the fix may be
absent from the binary you are running. Compare the `contract-bundle` line of `--version` against the revision
above before concluding an entry does or does not apply to you — an entry marked *(resolved)* is a
statement about a bundle, not about every binary named `3.0.0-preview`. Those entries exist for that
comparison alone: **when 3.0.0 ships, every *(resolved)* entry is deleted**, because a released
binary that contains the fix leaves nothing for the entry to warn anyone about. Their arguments are
already in `CHANGELOG.md`, where the history belongs.

This file exists because a project that claims completeness cannot receive feedback: every gap reads
as user error, and the reporter concludes they are holding it wrong. During the preview this list is
long, and that is correct. It shrinks as milestones land.

If something you need is here, **say so** — an entry on this list is a statement of current state,
not a refusal. Adding your case to the relevant thread is what moves it.

Every entry that owes a resolution **names the issue that owns it**, so that this file is a map of
current behaviour and the tracker is where the work is argued. A link written plainly, as `[#59]`,
is live work; a link written `[#58 (closed)]` is history, cited by an entry that no longer owes
anything. `tools/check-known-limits-issues.ps1` asserts both directions against the tracker on every
CI run, so an entry cannot quietly outlive the issue that owned it — which is exactly how §1.9 came
to be published as a limit months after it was fixed.

Three kinds of entry name no live issue and that is not an oversight: one documenting a choice the
specification explicitly permits (§1.5), one stating the boundary of what a piece of evidence proves
(§4.1), and one recording a boundary that is already decided and fixture-pinned, where the argument
is the value and there is nothing left to track (§1.10). Anything with no user-visible symptom — a
corpus gap, an internal to-do — is not written here at all; it lives only as an issue.

---

## 1. Implementation completeness

The 3.0 rewrite lands in milestones that follow the specification's own pipeline order. A stage that
has not landed is **not implemented**, and the tool exits with a non-normative status rather than
pretending to succeed.

The tool currently transforms **every input format and every output format the specification
defines**, end to end: namespace-profile, JSON, YAML and XML input, overlaying, references,
templates, output planning, and publication of namespace, quoted-namespace, INI, JSON, YAML and XML
destinations. **One** capability remains partly unbuilt, and it is refused rather than approximated.
A second row below is refused for a different reason: §15 permits an XML scheme file and never says
what one means, so there is nothing yet to build.

Those rows were missing from this list until they were found by auditing the refusal sites in the
source against the list rather than the other way round. That is worth recording where it happened:
a limits list assembled from what its authors remembered omitting is not a limits list, and the
audit is now the way this table is maintained.

| Area | State | Specification |
|---|---|---|
| Command line, informational modes, diagnostics encoding | Implemented | §6, §6.4 |
| Contract bundle reporting | Implemented | §22 |
| Namespace-profile input parsing, encoding detection, budgets | Implemented | §7–§9 |
| JSON **input** | Implemented, with the reductions in §1.1 | §7.1, §9, §15.1 |
| YAML **input** | Implemented, with the reductions in §1.2 | §7.1, §10, §15.1 |
| XML **input** | Implemented, with the reductions in §1.3 | §7.1, §11, §15.1 |
| Scheme parsing, `output`, `filename`, `root`, `delimiter`, `merge`, `filemerge` | Implemented | §16 |
| Overlaying, precedence, mapping order after override | Implemented | §5, §10 |
| Scalar inference and canonical numeric text | Implemented | §18 |
| Output planning, destination paths, collision folding | Implemented | §17 |
| Rendering: namespace, quoted namespace, INI | Implemented | §19.1–§19.2, §19.6 |
| Rendering: JSON, YAML | Implemented | §19.3–§19.4 |
| Publication and the validation gate | Implemented | §21 |
| References and value wildcards | Implemented | §13 |
| Templates and masks | Implemented for namespace input | §8.6, §12 |
| Wildcard output selectors | Implemented | §14 |
| Path-scoped view transformations: `type`, `key`, `substitute` | Implemented, with the gap in §1.11 | §16.5–§16.7 |
| Ordered sequences from numeric paths | Implemented | §8.7, §5.4 |
| Rendering: XML | Implemented | §19.5 |
| **Scheme files** written as JSON or YAML | Implemented | §15, §9.1, §10.4 |
| **Scheme files** written as XML | *(resolved)* Excluded by §15 — see §1.4 | §15 |
| **References in a scheme value** | Implemented | §15.1 step 1 |
| **A capture substituted into a directive value** | Implemented; §12.1 excludes `type` and `output` — see §1.4.2 | §12.1 |

A preview binary returns exit status `70` when an invocation needs a capability one of the rows above
does not mark `Implemented`.
That status is deliberately outside the contract: `0` and `1` are normative, and a
preview must never return either for work it did not do. It is a **refusal**, not a diagnostic — the
run decides no outcome at all, publishes nothing, and says on standard error which capability it
lacked. A step that could not do its job never passes its input through, because a plausible wrong
file is worse than no file.

A refusal must also name the capability the invocation actually needed. The `#71` refusal named the
*directive* until this was audited, so `app.*.key=n*me` reported that "the Key directive is not
implemented" — while `app.*.key=name` ran to completion in the same build. Telling an author to stop
using a directive that works is worse than the silence the refusal replaced, and
`TransformationTests.ARefusalNamesTheWildcardRatherThanTheDirectiveThatCarriesIt` now runs both
declarations in one test so the two cannot drift apart again.

That audit is also what shrank the refusal to the single row it now covers. Asking, for each
directive in turn, what a correct build would do with a wildcard in its value turned three of the
six into ordinary scheme errors that were already specified elsewhere: §16.8 and §16.10 forbid a
wildcard *selector* on the input-option and `merge` directives outright, and §16.7 closes
`substitute` to four keywords. Each of those now reports the rule it broke, at the line it was
written on, instead of refusing the run — and one of them had been claiming in its refusal text
that "the same declaration with no `*` in its value runs", which was false, because the wildcard
selector was the actual defect.

### 1.1 Reductions inside JSON input

JSON input is complete for §9 syntax, the §18 typed-scalar rules and the §15.1 projection, and this
one case is declined within it.

- **A wildcard in a native key is a template**, extracted entry by entry under §9.2 and expanded in
  the §12.4 fixed point exactly as the YAML half in §1.2, and `\*` for a literal asterisk works.
  A sequence beneath a wildcard key is extracted through its items' ordering values, and an empty
  container beneath one is `PARSE001`; both are settled in §1.2, which covers both formats because
  they share a reader.

A reference nested inside a native sequence is a second case, but it cannot be reached: any
unresolved value declines the whole invocation under §13, so no path exists today by which the
overlay is consulted. It is recorded in the source at `StructuredProfileReader.BuildSequence` as a
prerequisite for §15.1 step 15 rather than as a limit you can encounter.

### 1.2 Reductions inside YAML input

YAML input implements the whole of §10.1's `RestrictedYaml1` schema and every §10.2 and §10.3
refusal, and shares the §15.1 projection with JSON. These cases are declined or unfinished within
it.

- **Anchors, aliases, tags and merge keys are refused rather than retained.** A comment attached to
  a construct that §10.2 declines never reaches the model, because the document does not.
- **A wildcard in a key is a template**, extracted before structural merging and expanded in the
  §12.4 fixed point, so §10.4's enrichment works and the 2.4.0 regression recorded here is closed.
  `\*` for a literal asterisk works, and §16.7 `substitute=None` makes the key literal by the same
  route. *(resolved)* Two shapes beneath a wildcard key were declined with exit `70` and no output,
  a sequence and an empty mapping; §10.4 now settles both, so neither refuses and exit `70` is
  unreachable. A sequence is extracted through its items' ordering values — `a.*.b: [x, y]` is the
  same rule as `a.*.b.0=x` and `a.*.b.1=y` written in namespace form — and an empty mapping or an
  empty sequence, having no entries for entry-by-entry extraction to find, is `PARSE001` against
  §10.4, once per failing source. Pinned by
  `conformance/a-native-wildcard-template-over-a-sequence-extracts-by-ordering-value` and
  `conformance/a-native-wildcard-template-over-an-empty-container-is-parse001`.

  The reading that made the sequence look under-determined was that a native sequence item takes
  its ordering value from the destination's §5.4 high-water mark and the destination is unknown
  until §12.4 expansion. §12.4 answers that directly — a generated contribution "reserves or
  allocates ordering values … only when it is generated" — so the timing was never the obstacle.
  What remained was provenance: extracting through ordering values makes the items canonical
  numeric mapping children, which §5.4 calls explicit, where the source spelled a native sequence,
  which §5.4 calls implicit. That was settled as explicit, on the ground that §10.4 already has
  extraction flatten native shape into namespace-shaped entries for the mapping ancestors above —
  they "do not contribute mapping-presence marks" — and that the alternative would make a native
  template and its namespace spelling two different rules. §10.4 now carries the worked example
  against a **non-empty** destination, which is the only shape in which the choice is observable,
  and names `merge=append` as the way to add to a destination's items rather than replace the ones
  the template addresses. Pinned by
  `conformance/a-native-sequence-template-overrides-the-destination-items-it-addresses`, its
  namespace-spelling companion, and
  `conformance/a-sequence-template-appends-when-the-target-path-says-append`. Closed as
  [#75 (closed)](https://github.com/stop-cran/namespace2xml/issues/75).

  A residue about **ordering, not capability**, is likewise settled. §10.4's worked example once
  printed the generated key after the record's own keys while introducing the template as the first
  input, contradicting §5.3's rule that generated entries "inherit the rule's precedence position". This
  build implements §5.3 and always did; the example was amended to introduce the data file first,
  so its printed result is valid under §5.3, and it now says outright that sibling order is §5.3's
  to decide. Closed as [#73 (closed)](https://github.com/stop-cran/namespace2xml/issues/73).
  `conformance/a-yaml-wildcard-key-enriches-each-record-of-an-earlier-file` was written to fix the
  enrichment under an argument order where both readings agreed, so that the decision could not
  take the capability with it; it is now that worked example, byte for byte.

One §10.1 clause is under-determined and this preview chose a reading. §10.1 lists merge keys among
the constructs `RestrictedYaml1` excludes without saying whether an unsupported merge key is an
error or an ordinary key, and the adjacent clause — "every plain or quoted scalar mapping key is
treated as a string without scalar tag resolution" — can be read either way. **A plain `<<` is
refused**, on the ground that a merge key silently becoming data is the same hidden override §9.3
rejects for duplicate keys; a quoted `"<<"` is accepted as an ordinary key. If you are relying on
either reading, say so, because the clause should be amended rather than left to an implementation.

### 1.3 Reductions inside XML input

XML input implements every §11.1 prohibition and bound, the §11.2 subset, the §11.3 mixed-content
projection, the §11.4 canonical addresses, and the §11.6 coalescing rule, and shares the §15.1
projection with JSON and YAML. These cases are declined or unfinished within it.

- **`NormalizeFormattingWhitespace` is implemented**, and this entry records what enabling it costs.
  §11.7 defines it as "an explicit opt-in compatibility mode" that "weakens the normalized
  same-format round-trip guarantee", which is not a warning about the implementation but a property
  of the mode: XML has no universal test for insignificant whitespace without a schema, so a
  document whose indentation *was* data comes back different. One `WARN007` per input document
  reports that the run took the trade. The mode discards a whitespace-only text run only when its
  element has element children, none of its runs is CDATA, none is under `xml:space="preserve"`, and
  none holds non-whitespace text — because §11.7 preserves "whitespace in mixed content", so an
  element holding any real text keeps every run it has, including the one before its end tag that
  looks exactly like indentation.

  Under the default `PreserveWhitespace`, which "retains every text node", **an indented document is
  mixed content**: `<a>\n  <b>1</b>\n</a>` addresses as `a.#0`, `a.#1.b` and `a.#2`, not as `a.b`.
  That is not a defect — it is what retaining every text node means — but it is the reason
  `xmlinputoptions=NormalizeFormattingWhitespace` exists, and pointing this tool at XML a human
  wrote without it will surprise you. It surprises overrides too: a profile line written against
  `a.b` lands beside the real element rather than on it, silently. That was reported as
  [#40 (closed)](https://github.com/stop-cran/namespace2xml/issues/40), which asked for the
  surprise to be documented rather than for the addressing to change — the addressing is what
  §11.4 specifies — and was closed by
  [docs/usage-methodology.md](docs/usage-methodology.md) §3, which works the case through. The
  fixture `xml-canonical-addresses` pins both spellings.
- **A promoted sequence item's place in the serialized XML stream is now pinned.** This entry
  recorded a gap that §11.4 itself carried as a non-normative open question, and both are resolved.
  §11.4 evaluates mixedness and repeated-child classification "across all input contributions to
  that element", and this tool does: a mixed contribution re-addresses an element-only
  contribution's children as content nodes, and a contribution that repeats a name makes another
  contribution's singleton an item of that one sequence. The addresses were always settled; where a
  concatenated item was *drawn* was not, because §11.4 gave that job to the content token — "content-token
  values determine placement in the parent's serialized stream only" — and never related a second
  document's tokens to the first's. An item promoted out of another document carried a token from
  that document's counter and could be drawn among the items it follows in the address space.

  What settled it was noticing that the two destinations disagreed. Against two documents
  contributing `one`, `mid`, `two` and `three`, the namespace view reported `b.0=one`, `b.1=two`,
  `b.2=three` while the XML view emitted `one`, `three`, `mid`, `two` — so `b.2` named the last
  item for a reference and the second element for a reader. §11.4 now makes canonical index order
  govern the serialized stream where the two disagree, which is a rule about consistency between
  views rather than a new ordering policy, and a single document is unaffected because its own
  counter is already monotone. `a-promoted-item-follows-its-canonical-index` pins both views in one
  run. **verified**

- **Comments are retained**, and this entry records what that costs elsewhere rather than a gap.
  §11.5 keeps them "as ordered comment nodes", explicitly not "forced into a 'leading comment for
  the next value' representation because a comment may occur between mixed-content nodes or after
  the final child", and §4.5 says the same from the other side: "standalone XML comments remain
  ordered content nodes and are not reassigned to adjacent values". A comment therefore takes an
  ordinary §11.4 content token — `<a>t<!--c-->u</a>` addresses its text runs as `#0` and `#2` with
  the comment at `#1` — and is never a §4.5 bound comment. §17.4's "comments alone do not make a
  parent mixed-content" holds too, so `<a><b/><!--c--><d/></a>` keeps element-name addressing for
  `b` and `d` and puts the comment at `a.#1`.

  What this costs is every format except XML. §19.5 is the only renderer that "emits retained XML
  comments"; §19.3 "renders comments nowhere", and although §19.4 emits YAML comments "in
  normalized positions" and §20 emits namespace comments "where their association can be
  represented", a comment holding a content-token slot is associated with no value — giving it one
  is the reassignment §11.5 and §4.5 both forbid. So **an XML comment survives an XML-to-XML run
  and nothing else**, and every other destination reports one summarized §3 `WARN003` naming how
  many it dropped. That warning is counted under its own feature category, separate from the §4.5
  bound comments a YAML or INI source contributes, because the two are different source concepts.

  One reduction remains inside XML itself, and it is narrower than an earlier revision of this
  entry claimed. That revision said `type=ignore` and the §11.4 conversions "reach a comment in
  mixed content but not one sitting among element-only children", which contradicted the sentence
  immediately before it — the one placing the comment of `<a><b/><!--c--><d/></a>` at `a.#1` — and
  was false. A comment takes a content token wherever it sits, the token index counts its element
  siblings, and the address is real: against that document `${a.#1}` resolves onto the comment as
  `REFERENCE005`, and `a.#1.type=ignore` removes it and exits `0` with an empty diagnostic stream.
  Move the comment first, as `<a><!--c--><b/></a>`, and it answers to `a.#0` instead. What `#n` does
  not reach is an **element** child: `${a.#2}` in the first document is `REFERENCE002`, because
  §11.4 gives an element-only child a content token for placement without making it an address. So
  the reduction is that an element cannot be selected positionally, and it costs a comment nothing.
- **CDATA is retained**, and this entry records what that costs elsewhere rather than a gap. §11.6
  keeps CDATA distinct so XML output can re-emit it; the spelling now rides on the scalar payload,
  so §4.4's last-wins replacement carries it and the two cannot disagree about which text is
  CDATA. What does *not* follow it is any other format: JSON, YAML and the flat formats have no
  such spelling, so `PreserveCData` is meaningful only on an XML destination, and a value that
  reaches XML by way of another format arrives as ordinary text.
- **Element-only children carry a content token for placement but no separately addressable one.**
  §11.4 says every parent assigns ordering values "including element-only parents", and that
  element-only children retain element-name addressing "while also carrying their content-token
  ordering value for deterministic placement". The placement half now works:
  `<a><b>1</b><c>2</c><b>3</b></a>` round-trips unchanged, its `<c>` still between the two `<b>`
  elements,   A namespace-profile override at `a.b.1` changes the third element's text without
  moving it. What is still missing is the *address*: an element-only child cannot be selected
  positionally, so `a.#1` addresses whichever content node holds that token and never the element
  beside it. A comment among element-only children **is** addressed, contrary to what this entry
  used to say — see the correction under "Comments are retained" above.

  A second reduction is in how a token survives merging. §11.4 assigns values "while concrete XML
  contributions merge", and says a payload converted from a non-XML source "receives a fresh
  implicit content-token ordering value at its source position". This preview keeps the token of
  whichever contribution owns the §5.2 position mark and gives an untokened contribution no token
  at all, which places it after every tokened sibling rather than at its source position. That is
  right for the ordinary case — an XML input with namespace-profile overrides layered on, where the
  overrides land on nodes that already exist — and wrong when a later document adds a *new* child
  to an element an XML document already wrote: the new child is appended rather than placed. Two
  XML documents contributing different children to one element likewise interleave by neither
  document's order, since their allocators are independent.

  An earlier revision of this file claimed here that "document order is preserved — it is what the
  §5.2 position marks record". That was wrong for exactly the interleaved case above, and it is
  recorded rather than quietly deleted because a reassurance is worth less than nothing when it is
  the thing that stops you checking. It was corrected only after a fixture was authored that
  demonstrated the reordering.
- **The content-token alias for a sole text node is not materialized.** §11.4 calls the element path
  and its sole text or CDATA content-token path "two canonical addresses for one scalar identity".
  This preview exposes the element path only, so `<b>two</b>` is addressable as `b` and not as
  `b.#0`.
- **Processing instructions are discarded**, with one `WARN006` per document that carried any.
  §11.8 places them outside the preservation contract.

### 1.4 *(resolved)* XML scheme files were refused with exit `70`

`v3.0.0-preview.1` and `v3.0.0-preview.2` decline a `-s` file whose name ends in `.xml`,
with exit `70` and no output, because §15 named XML among the scheme formats and never said how an
XML document projects to directive paths. §6.3 defines only `0` and `1`, so that status could not
ship.

§15 now **excludes** `.xml` by name. Scheme files use the case-insensitive `.json`, `.yaml` and
`.yml` extensions, every other extension including none at all is a namespace profile, and an
`.xml` scheme is `PARSE001` against §15 in the scheme phase, once per failing source, reported
before the file is read. `conformance/an-xml-scheme-file-is-not-a-scheme-format` pins it.
[#72 (closed)](https://github.com/stop-cran/namespace2xml/issues/72).

The exclusion is not a narrowing, and measuring 2.4.0 is what settled that. Given
`<app><output>namespace</output></app>` saved as `s.xml`, 2.4.0 opens the file, produces no
directives, writes nothing, and reports `Success! Exiting...` at exit `0`. Two controls locate
it: the same text saved as `s.txt` reports a namespace parse error at exit `1`, so the silence
comes from the extension rather than the content, and a working `app.output=namespace` scheme over
the same profile does write its file. **XML scheme files were never supported** — the extension
selected a reader that produced nothing, and the run reported success for work it did not do. This
matters because the opposite was true for JSON and YAML: refusing those was a real loss of function,
which is [#66 (closed)](https://github.com/stop-cran/namespace2xml/issues/66) and §1.4's earlier
text got the two cases the same way round only by accident.

Excluding is also the only forward-compatible answer. A specified error can become support later
without breaking anyone; a guessed projection, pinned by fixtures and then corrected, cannot.

The extension decides, not the content — a scheme written in namespace-profile syntax but saved as
`scheme.xml` is still rejected, and one saved as `scheme.ini`, `scheme.sh` or with no extension
at all is read as a namespace profile, exactly as §7.1 treats input files.

### 1.4.2 *(resolved)* A capture in a `type` or `output` value was refused with exit `70`

`v3.0.0-preview.1` and `v3.0.0-preview.2` decline `a.*.type=arr*y` and `a.*.output=*` with
exit `70`, naming "a wildcard capture substituted into a directive value" as a capability they
lack. §12.1 said a scheme directive's value is decided from the captures its own pattern defines,
without exception, so the refusal was the build declining to follow its own contract.

§12.1 now **excludes** both values from capture substitution: §16.6 closes the type names and §16.1
closes the output formats, so a capture could complete either only by accident of the matched data.
An unescaped `*` in either is literal text and falls to the ordinary §16.1 or §16.6 value check,
which rejects it as `SCHEME001` in the scheme phase at the declaration's own line. The exclusion
belongs to the directive, so `cfg.*.output=*` and `cfg.output=*` are the same error.
`conformance/an-asterisk-in-a-type-value-is-scheme001` and
`conformance/an-asterisk-in-an-output-value-is-scheme001` pin it.
[#74 (closed)](https://github.com/stop-cran/namespace2xml/issues/74).

"Only by accident of the matched data" is measurable, and 2.4.0 measures it. With
`cfg.*.type=arr*y` over a profile holding `cfg.a` alone, the capture text `a` completes
`array` and 2.4.0 exits `0` having silently applied a type directive nobody wrote; add `cfg.b`
and the same line yields `arrby`, which reaches `Enum.Parse` unguarded and terminates the process
at exit `-532462766`. One declaration is a working directive or a crash depending on which data it
matches. `output` is sharper still, because it **creates** the output instance rather than binding
to one, so the §14.1 expansion that supplies every other instance-scoped directive's captures runs
after the value is read: 2.4.0 spliced the matched path part `json` into the format and wrote
`json.json`, choosing format and destination from the data.

Substitution into every other directive value is unaffected: `filename`, `root`, `delimiter`,
`filemerge` and the four output-option directives take their captures from the §14.1 expansion at
step 13, and `key` takes them from its own per-path match at step 16.

### 1.5 `--max-depth` has a hard safety ceiling of 4096

§6.2 permits a build to document a hard safety ceiling on a limit option and makes a larger value
`CLI001`. This build imposes one on `--max-depth`, at **4096** — eight times the §6.2 default of
512, and equal to the `--max-reference-depth` default. Every other `--max-*` option is unrestricted
beyond the §6.2 grammar.

The reason is that several pipeline phases walk the document tree by recursion, so document nesting
costs stack. Before the ceiling existed, `--max-depth 100000` with a 2,000-deep document did not
report anything: the process died with a stack overflow (`0xC00000FD`), which §6.3 does not define
as an outcome, which no diagnostic can describe because the runtime does not permit one to be
written, and which a caller sees only as an exit status. **verified**

The pipeline runs on a thread with a 16 MiB stack so the ceiling is honoured rather than merely
declared: a 4,095-deep XML document completes and exits `0`, and equivalent JSON and YAML documents
do the same. **verified**

If you have a real document deeper than 4096, that is a finding worth reporting — the honest fix is
to convert the recursive phases to explicit traversal, and a real document is the evidence that
justifies it.

### 1.6 `Q{}local-name` narrows addressing per reference, and per component in a scheme path

§11.4 says a `Q{}local` component and an unmarked `local` component **are the same component** and
address the same overlay node, and that the marker "does not narrow the component — it narrows the
addressing". Both halves now hold. The lexer yields an ordinary component for an empty URI and
records that the marker was written; `OrdinaryPart.IsExplicitlyCanonical` is excluded from equality,
so `a.Q{}b=1` and `a.b=2` remain one node and the second overrides the first, while §13.1 can still
see that the path was addressed canonically. **verified**

What the marker does is opt a reference out of the §13.1 simple alias index, so that an attribute
`@x` or a content token `#n` cannot compete with an element `x` for the same unmarked spelling.
`${a.b}` against a model carrying both `a.@b` and `a.b` reports `REFERENCE004` naming two
candidates, and `${a.Q{}b}` selects the element. The fixture
`an-empty-qualifier-escapes-the-alias-ambiguity` pins it. **verified**

§13.1 decides canonical addressing per reference rather than per component — "a reference containing
an unescaped XML `Q{...}`, `@`, or `#n` typed address component is canonical" — so `${a.Q{}t.x}`
takes the whole reference off the index, including the unmarked final part. That is what the clause
says; if you wanted the marker to bind only to its own component, that is a specification amendment
and not a defect here.

**§15.2 is worded per *component*, and a scheme path behaves accordingly**: "an explicitly marked
`Q{}`, `@`, or `#n` component selects only that XML component. An unmarked component uses the simple
alias index." So `a.Q{}x.@y` aliases nowhere at `x` and binds the attribute at `@y`, where the same
spelling as a reference would have been canonical throughout. The difference is in the two clauses,
not in this build. **verified**

### 1.7 *(resolved)* `WARN010` is not emitted

Section 3.2 requires "exactly one compatibility warning for each source contribution, canonical
mapping path, and output instance where a JSON or YAML mapping inferred at step 11 remains projected
as a sequence". `v3.0.0-preview.1` and `v3.0.0-preview.2` implemented the inference and not the
warning: `{"a":{"2":"x","7":"y"}}` rendered as the dense sequence §8.7 specifies, with an empty
diagnostic stream where one warning per contributing source was owed.

The warning is now emitted, with the cardinality §22 states. Every document that wrote keys into an
inferred mapping is named individually, because a single warning at the path would tell an operator
that some document had lost its keys without saying which one to edit. Two exclusions fall out of
the same rule and are pinned: a native sequence raises nothing, having had no inference applied to
it, and a namespace-format contribution to the same node raises nothing, because a numeric path
segment makes no shape claim to contradict. `type=mapping` suppresses the warning per output
instance rather than per node, so a model rendered twice can keep the keys in one output and be
warned about the other. `conformance/warn010-fires-once-per-native-source-contribution` and
`conformance/type-mapping-suppresses-warn010-per-output-instance` pin those five properties, and
acceptance item 68 is now `required`. [#58 (closed)](https://github.com/stop-cran/namespace2xml/issues/58).

The previous text explained the gap by "per-source provenance the overlay does not retain", and
said §1.8 needed the same change. Both halves were wrong in a way worth recording. The warning does
not need the general provenance §1.8 wants — it needs one specific fact, "a native JSON or YAML
document wrote a mapping here", which is decided at read time and can be carried on the node as a
set. Diagnosing a missing feature by naming the largest change that would supply it is how a
tractable item comes to look like a blocked one; the estimate outlived the two releases that shipped
without the warning.

### 1.8 *(resolved)* A wildcard contribution merged as one earlier-or-later value, not interleaved

§12.4 makes every generated `(rule,match)` result "a separate contribution for every merge strategy"
and merges it "at its deterministic rule/match position". Through `v3.0.0-preview.3` the generated
value was folded in as a single earlier-or-later neighbour of what was already at the path, by
comparing the rule mark against the latest mark at the node.

That is correct whenever the existing contributions all precede or all follow the rule, and it is
also correct for `replace`, `deep` and `error`, which are each decided by a maximum. It was wrong
for `append`, the one strategy under which every contribution survives into the result and a
position among them can therefore be seen.

Two shapes showed it. Three input files under `merge=append` — `a.p.0=first`, then the template
`a.*.0=from-template`, then `a.p.0=second` — published `[from-template, first, second]` where §12.4
requires `[first, from-template, second]`, output identical to the run that lists the template
*first*: both extremes were right and every rule position between them collapsed onto the earlier
one. Separately, a later contribution that supplies **no** item — an explicitly empty native
sequence — still refreshes the node's shape-mark under §4.4, so the node's mark and its items
disagreed even with nothing straddling anything, and a generated value later than the only item was
published before it.

Each sequence item already carries the mark of the contribution that supplied it, so the fix needed
no new provenance: the position is found by partitioning the items on that mark and running §16.10's
rebase once per group in source order. The estimate recorded here — that full fidelity would mean
retaining each source's contribution at a path — was wrong, as §1.7's identical estimate had been
before it. `conformance/a-generated-contribution-sorts-between-the-sources-that-straddle-it` pins
the straddling shape and its §5.4 ordering values, and
`conformance/a-later-empty-sequence-does-not-reorder-a-generated-contribution` pins the second.
[#59 (closed)](https://github.com/stop-cran/namespace2xml/issues/59).

The earlier probe for this entry also destroyed the first contribution outright, which was a
separate defect with a separate cause and is likewise **fixed**: `append` seeded its accumulator
from the earlier node's sequence projection alone, which is empty for a contribution spelled as an
ordering-value mapping, so the earlier items were dropped from the result.
`conformance/wildcard-generation-appends-beside-every-contribution` pins every contribution
surviving.

### 1.9 *(resolved)* A canonical reference to an XML comment position

§13.1 says "a canonical reference directly addressing an XML comment path fails as a non-scalar
reference", which §22 codes as `REFERENCE005`.

**`v3.0.0-preview.2` shipped this entry claiming, as `verified`, that the build reported
`REFERENCE002` instead. That claim was false when it was published.** It was written against an
earlier build and was not revisited when the cause was removed. It also misattributed the cause:
it named `BoundComment`, which carries §4.5 bound comments for non-XML formats and is not on this
path at all. An XML comment is a `ContentPart` child holding a `ScalarPayload` whose
`IsValue` is false, and it has been addressable since comments became ordered content nodes.

`ReferenceResolver` distinguishes the two: a node whose payload is not a value is `REFERENCE005`,
and a `#n` naming nothing is `REFERENCE002`. `conformance/a-reference-to-an-xml-comment-is-not-a-value`
pins both against one node, one digit apart, so the codes cannot converge again unnoticed. The
message now names the comment rather than describing it as a structured node.
[#60 (closed)](https://github.com/stop-cran/namespace2xml/issues/60).

This entry is kept rather than deleted because a published limit that was never true is worth more
as a correction than as an absence. A reader who acted on preview.2's text — writing off
`REFERENCE002` on a `#n` path as the known conflation, when it meant the path was genuinely
absent — was misled, and deleting the entry would leave them no way to find that out. The process
failure is the one this file is most exposed to: `KNOWN-LIMITS.md` records what is *not* verified,
so nothing in the verification loop re-checks its claims, and an entry can outlive its defect
silently. Removing a limit is part of the fix, not paperwork after it.

### 1.10 A scheme path's alias covers `@` and `Q{uri}`, and not `#n`

§15.2 says "an unmarked component uses the simple alias index for compatibility and convenience;
if ordinary and XML components make that alias ambiguous at a matched location, selector expansion
at pipeline step 13 emits blocking `SCHEME002`". A scheme path now folds an attribute `@x` and a
qualified element `Q{uri}x` to the unmarked spelling `x`, so a 2.x scheme that wrote `a.x.type=…`
binds to the attribute written `a.@x`, and `SCHEME002` has a call site. A marked component still
selects one component outright: `a.@x` binds the attribute only, and `a.Q{}x` the element only.
**verified**

A **wildcard** component does not consult the index. The alias is a lookup by written name — it maps
the name an author wrote to the XML components that name could have meant — and a wildcard writes no
name. `§15.2` also opens by keeping "the same typed component model as canonical data paths", which
is what §8.6 and §12 apply to a wildcard over data, so folding here would make `*` mean one thing in
a scheme and another in a profile. The ambiguity clause settles it: its remedy is to mark the
component and name one outright, which a wildcard cannot do, because it was written to match both.
Pinned by `conformance/scheme-a-wildcard-does-not-reach-an-xml-component-through-the-alias`.
**verified**

§13.1's other two rewrites — removing a `#n` wrapper before a child element, and removing a terminal
text or CDATA `#n` — are **not** applied to a scheme path. They change a path's length, so a scheme
component would have to match a variable number of concrete components, and §11.4 holds an element
and its sole content token to be "two canonical addresses for one scalar identity, **not** two
candidates in the simple-alias ambiguity index" — folding the terminal token would instead make
every directive on a mixed-content element ambiguous with its own text and raise `SCHEME002` where
the specification wants none. A directive on a content token must therefore be
written `a.#1`, which is unambiguous. **verified**

The alias applies to `type`, `key`, `ignore`, `multiline` and the other path-scoped directives bound
at step 16. Wildcard **output selector** expansion at step 13 still addresses the concrete name graph
literally, so `output` and `filename` selectors do not reach an attribute through an unmarked
component. An output selector names a subtree root and an attribute is a leaf, so nothing is lost
that a canonical spelling could not say; it is recorded here because §15.2 does not draw that line
itself.

The *data* surface beside it is a separate rule and is unchanged: §8.2 scopes the alias index to
references and scheme paths, so a contribution written `a.x=v` against an attribute `@x` still
creates an ordinary sibling rather than overriding. Since contract revision r39 it emits `WARN011`
naming both components and the canonical spelling that would have overridden, and it is pinned by
`conformance/xml-a-2-x-style-attribute-override-adds-a-sibling-element`,
`conformance/xml-an-attribute-from-xml-input-is-overridden-through-its-canonical-address` and
`conformance/xml-an-unmarked-alias-warns-only-when-it-follows-an-xml-component`. The first of those
measured 2.4.0 accepting `r.a.x=dev` against `<a x="base">` and overriding the attribute, which is
what makes this a migration hazard and not merely a specification detail. Write `a.@x`.
[#56 (closed)](https://github.com/stop-cran/namespace2xml/issues/56) owned both halves and both
have landed.

This entry names no live issue, and that is the third case the preamble's rule allows beside §1.5
and §4.1: every boundary above is a **decided** one, argued from a clause and pinned by a fixture,
so there is nothing outstanding to track. It stays because a reader hitting one of them needs the
argument, not a tracker link — the shape of what the alias does and does not reach is exactly what
a 2.x scheme runs into.

### 1.11 A directive bound beneath a node that a later step-16 pass reshapes became inert *(resolved)*

Resolved by [#49 (closed)](https://github.com/stop-cran/namespace2xml/issues/49). §15.1 now states
what happens, and it is the opposite of what this build did: a reshaping pass "re-addresses the
surviving descendants of that node", and a directive bound beneath one "is re-addressed together
with the value it bound to, exactly as Section 17.5 re-addresses the per-path high-water map".

The build bound every rule once against the pre-transformation paths, which was right, and then had
each pass look its rules up by *spelling* against the previous pass's output, which was not. A rule
bound at `a.b.c` was unreachable to a later pass once an earlier one renamed `b` — `type=array` at
`a` turns `b` into the ordering value `0`, and `key` at `a` puts it in a record — so the rule
applied to nothing and reported nothing, because the §15.2 warning counts a rule that bound, and
this one had bound.

The measurement that closed it found three loss sites, not the one the report named: the `multiline`
pass, the `key` pass, and the serializer's own read of the table for the explicit scalar and XML
node kinds of §16.6. All three were silent. `type=string` beneath a `type=array` ancestor, and
`type=string` on either child shape of a `key` transformation, were lost the same way.

`type=ignore` remains the stated exception, because it removes descendants rather than moving them;
§16.6 keeps its rule that a directive matching only a descendant of an ignored path "is inert and
emits the unbound-directive warning". `conformance/a-directive-under-a-converted-mapping-follows-its-value`
and `conformance/a-directive-under-a-generated-record-follows-its-value` pin both halves of the new
rule, the second of them across the two record shapes §16.5 builds.

### 1.12 *(resolved)* §16.5's merge with an independent sequence replaced, and stopped saying so

§16.5 says of the sequence a `key` transformation produces that it "combines with it as the later
contribution under the Section 17.1 sequence rules". This build replaced rather than merged, and the
replaced items left no diagnostic behind.

Feed one node a sequence from one input and a mapping from another — `{"reg": [{"x": 9}]}` beside
`reg.alpha.x=1` and `reg.beta.x=2` — and add `reg.key=name`. The `{"x": 9}` item was absent from the
output and the diagnostic stream was empty. Remove `key` from that same scheme and the omission is
announced: `TYPE002` says the flat output renders one container shape and that the sequence items go
unemitted. Adding a `key` directive therefore converted a **reported** omission into a **silent**
one, which is the reverse of what a directive added to improve an output should do.

The node is reachable because the shape contest is decided **per destination during planning**, not
in the model before step 16. Both projections survive in the overlay tree — `merge=error` at that
path fires `TYPE001`, counting two contributions — and each output format then resolves the contest
for itself. So `key` runs with the independent sequence still present, which is exactly the
situation the clause describes.

An earlier revision of this entry asserted the opposite, that no node can hold both shapes at once,
and marked it verified. It was reasoning from §4.4's exclusivity to a resolution point the
specification does not place there. The measurements behind it were real; the inference from them
was not.

Fixed, from [#61 (closed)](https://github.com/stop-cran/namespace2xml/issues/61).
`ViewTransformer.ToRecords` seeds the record sequence from the node's existing sequence instead of
an empty one; the ordering allocator already started at the node's §5.4 high-water mark, so every
generated record with a fresh implicit value was already landing above the items that were being
discarded. `conformance/key-records-merge-with-an-independent-sequence` pins the result, including
the consequence that makes folding distinguishable from replacing: a native item the transformation
never touched sits *between* two generated records, because §5.4 sorts the combined set by ordering
value rather than by origin.

A second revision of this entry said the clause was right and needed no amendment. That was wrong
in one respect. §16.5 used to route the fold through the effective `merge` strategy, while §16.10
says `merge` "applies only to common-model input and wildcard-generated contributions at pipeline
steps 8 through 11" — and `key` is step 16, so the two clauses could not both hold. §16.5 now names
§17.1 directly and §16.10 says so explicitly, which changes no behaviour under the default strategy
and removes a contradiction that would otherwise have surfaced the first time anyone wrote
`merge=replace` beside a `key`.

### 1.13 *(resolved)* A destination high-water mark is lost when `replace` removes the path entirely

§17.5 says "Every output contribution carries its complete per-path high-water map, including marks
raised by items hidden by output projection." This build carries marks on the overlay tree rather
than in a separate map, so a mark lives on the node whose sequence raised it. A destination
`filemerge=replace` that discarded a path without naming it left the mark with nowhere to live, and
it was lost: a later contribution recreating that path allocated from zero, and a further one
addressing an explicit ordering value then destroyed an item instead of landing beside it.

Fixed, from [#62 (closed)](https://github.com/stop-cran/namespace2xml/issues/62).
`OverlayMerger.CarryMarks` retains a marks-only node for a path the replacement removes, and
`OverlayMerger.StripHighWaterCarriers` removes whatever no contribution recreated once the fold that
needed it has finished. Both halves are pinned:
`conformance/a-replacement-keeps-the-mark-of-a-path-it-does-not-name` and
`conformance/a-replacement-leaves-no-trace-of-a-path-it-removed`, alongside the already-covered
reachable case in `conformance/a-replaced-destination-keeps-the-high-water-mark`.

The rejected-fix note this entry used to carry — that materialising an empty node "puts a path §16.10
has just removed back into the overlay tree, where wildcards, references and selectors find it
again" — was true of the wrong merge. `MergeContext` distinguishes the step-8 input merge from the
step-18 destination fold, and §17.5 states that file-level merge "operates on fully transformed
contribution models after `type`, `key`, and `root` have been applied". Every stage that addresses a
path by name has already run by then, so the objection applies to `merge=replace` and not to the
`filemerge=replace` this entry was about. The retention is enabled only for the destination fold.

Two consequences worth recording. The carrier holds `StableOrderingKey.Last`, a position that loses
every §5.2 comparison, because §17.5 enumerates the high-water mark as the one thing surviving a
replacement and says nothing about the position; a carrier that kept the original position would
sort a recreated path where its discarded contribution used to be. And the carrier must not reach a
renderer: an exclusive destination emits it as an empty mapping, which is the removed path
reappearing. Stripping it at the end of step 18 is safe because step 18 is the last stage that
allocates a §5.4 ordering value.

### 1.15 A value ending in a blank line is spelled double-quoted, not as a block scalar *(resolved)*

Resolved by [#52 (closed)](https://github.com/stop-cran/namespace2xml/issues/52). §19.4 no longer
promises a block scalar unconditionally: it now reads "uses literal block scalars for multiline
values that a block scalar can carry exactly and that can legally terminate the document, and the
double-quoted form otherwise", and names all five conditions that force the quoted form — trailing
whitespace, a CR line break, a control character outside `c-printable`, an indented first non-empty
line, and a value ending in a blank line, which needs `|+` and so cannot satisfy §24's single
trailing LF.

§19.4 also now states the position-independence rule and why it was chosen over the narrower one:
declining the block only where it would fall last also never produces an illegal document, but it
makes a value's spelling depend on where it sorts among its siblings, so adding an unrelated key
would silently rewrite an untouched value.

Behaviour is unchanged. Four of the five conditions were already being quoted on §3.3 grounds with
no clause permitting it, and three had no fixture at all;
`a-block-scalar-is-declined-when-it-would-not-be-exact` now pins all five against a control value
that does get a block scalar.

### 1.16 A top-of-file comment binds differently in namespace and YAML input *(resolved)*

§8.5 stated of namespace input that "consecutive comments are associated with the next entry",
without qualification, so a comment at the top of a namespace profile became a leading comment of
the first entry. §20 classifies comments across every format and scopes its leading rule explicitly
to "a comment between two payloads or items", which carves the first position out of it: "a comment
before the first payload or item is document-leading". §10.1 lists the YAML comment positions that
are supported but states no association rule of its own, so §20 governs YAML.

The same top-of-file comment was therefore classified two ways depending on the format it was read
from. A plain round trip did not show it, because a document-leading comment and a leading comment
of the first entry emit in the same place. It became observable once the owning entry stopped being
emitted: an ignore mask over the first entry took that entry's leading comment with it, as §8.6
requires of "comments bound to suppressed paths", while a document-leading comment survived.

§8.5 now carries the exception, so the classification is format-independent: "comments preceding the
first entry of a source are document-leading, as Section 20 classifies the first position for every
format". A namespace header outlives a mask over the entry below it, and a profile converted between
formats keeps that header.

The exception has a cost, and §8.5 states it rather than leaving it to be met: an opening comment is
bound to no path, so §5.2 does not move it when the first entry is overridden and §16.5 does not
carry it into a generated record. A source whose first entry needs a comment of its own must be
written with that entry second. `a-namespace-header-comment-outlives-its-first-entry` and
`an-opening-comment-does-not-move-with-its-entry` pin both halves.
Tracked as [#63 (closed)](https://github.com/stop-cran/namespace2xml/issues/63).

### 1.17 An unpaired surrogate cannot reach an output, and `-v` loses one silently

§16.9 now states the qualifier it was missing, resolving
[#64 (closed)](https://github.com/stop-cran/namespace2xml/issues/64): non-ASCII text is emitted as
literal UTF-8 "wherever UTF-8 admits it", and a UTF-16 code unit UTF-8 cannot encode — an unpaired
surrogate — is emitted as a `\uXXXX` escape regardless of the flag, because the alternative is a
silent U+FFFD that discards the code unit while reporting success.

The entry remains open because the second half of its title is unresolved: the branch is
unreachable, so nothing pins it, and `-v` still loses a surrogate before the tool sees it. Every
route into the model was tried. **verified**

| Route | Result |
|---|---|
| Any file input | UTF-8 has no encoding for a surrogate, so the bytes cannot occur |
| JSON `"x\uD800y"` | `PARSE001 §9.1` |
| YAML `"x\uD800y"` | `PARSE001 §10.1` |
| Namespace `\u{D800}` in a name | `PARSE001 §8.2` |
| Namespace `\u{D800}` in a value | Not an escape there; the text stays literal |
| `-v cfg.a=x<U+D800>y` | Arrives as U+FFFD, exit `0`, no diagnostic |

Only the last row loses anything, and the substitution happens before the tool runs: the .NET
apphost passes its arguments through UTF-8 on the way to managed code, so `Main` is handed a U+FFFD
that nothing downstream can tell from one the user typed. Reproduced both through `dotnet run` and
by starting the apphost directly with a UTF-16 argument list, which rules out the shell.

It is recorded rather than acted on, because refusing U+FFFD in a variable would reject legitimate
text and there is no other signal to test. A revisit is warranted only if someone reaches it in
practice. No conformance fixture is possible while the branch is unreachable, so §16.9's sentence is
the only thing holding the behaviour; it says so, adding that an implementation "is not required to
make such text reachable".

### 1.18 *(resolved)* `NewLineOnAttributes` and the first attribute

§16.9 said `NewLineOnAttributes` "places each attribute after the first on its own line", and this
build placed *every* attribute on its own line, including the first.
[#53 (closed)](https://github.com/stop-cran/namespace2xml/issues/53) put the choice to review, and the clause
moved: §16.9 now says every attribute, including the first. The corpus gap that let the divergence
survive is closed by `conformance/xml-newline-on-attributes`.

This entry is kept rather than deleted because the reasoning is the point. The decision went to the
clause and not to the code, which is the direction rule 2.1 exists to make expensive, and it was
taken for a reason narrower than "the code was already like that": the flag is cosmetic and opt-in,
and obeying the old wording meant hand-writing start tags and taking attribute escaping, namespace
declaration placement and mixed content back from `XmlWriter` — a much larger surface, at risk of
being wrong in ways nothing else in the contract would catch. Where a clause has teeth, the code
moves instead.

### 1.19 *(resolved)* `root` on a bare-scalar flat output root had two readings

Resolved by [#54 (closed)](https://github.com/stop-cran/namespace2xml/issues/54). §16.3 now states
that "`root` wraps; it never renames", and each of §19.1, §19.2 and §19.6 says that `root` prefixes
the key a flat format retains for a bare scalar rather than replacing it. For `k=1` with
`k.output=ini` and `k.root=s` the file is `[s]` then `k=1`.

The question was framed as INI-only and was not. All three flat formats had it, and the two
namespace clauses were the reason: they offered a rename and a wrap as alternatives, which is
two answers offered as one. Every format bullet in §16.3 already describes `root` with a wrapping
verb — prefixes, wraps, emits `{"x":{"y":...}}` — so "rename" was the single word out of step, and
removing it settles all three formats at once instead of making INI an exception.

The reading is also the one that survives contact with use. A shell consumer selecting a bare scalar
at `db.password` with `root=APP` now gets `APP_password='secret'`; under the replacing reading it got
`APP='secret'`, discarding the only name the value had.

`conformance/root-wraps-a-bare-scalars-retained-key` pins it across namespace, quoted namespace, INI,
JSON and YAML, beside `root-wraps-uniformly-across-formats` for the container case.

### 1.20 `WARN009` binds by existence, where §22 said effectiveness

Resolved by [#55 (closed)](https://github.com/stop-cran/namespace2xml/issues/55). §22 and Appendix B
now agree with §15.2 that the condition is binding to "no concrete output instance or path", and
§15.2 states the test as existence rather than effect. A directive naming an `output=ignore`
instance is silent, because §16.1 keeps that instance in existence so a later declaration can
restore it; a directive stranded beneath a `type=ignore`, whose path is destroyed, still warns.
Both sides are pinned — `a-directive-on-an-ignored-output-instance-does-not-warn` and
`type-ignore-removes-a-subtree-and-strands-its-directives`.

### 1.21 An XML comment moves to the far side of the value it sits beside

`<a><!--c-->1</a>` emits as `<a>1<!--c--></a>`. The comment survives with its text intact, in the
same element, and so does the value — but a comment written **before** an element's text is written
**after** it, so this one aspect of an XML → XML round trip is not byte-identical.

The cause is structural rather than an oversight in the writer. §11.4 exposes an element's lone text
run as the scalar at the element path — that is what makes `<a>1</a>` the value `1` rather than the
content node `a.#0` — and a scalar exposed that way carries no content-token ordering value. The
comment beside it does carry one, so the writer has nothing to compare it against and emits the
value first. §19.5 states this outcome rather than leaving it to the implementation.

Mixed content is **not** affected: `<a>x<!--c-->y</a>` gives every run its own ordering value, and
the comment keeps its place. Nor is a comment among element-only children. The limit is confined to
an element holding exactly one text run and at least one comment.

The exposed run still consumes the index it would have occupied, so the comments beside it keep the
values they would otherwise have had and a directive written against the gap matches nothing:
`<a><!--c-->1<!--d--></a>` addresses its comments as `a.#0` and `a.#2`, and `a.#1` emits `WARN009`.

Lifting it means carrying an ordering value for the exposed scalar through the overlay beside its
payload, so that a later contribution replacing the value does not inherit the position of the value
it replaced. That is a change to the shared node marks rather than to the XML writer, which is why
it is not in 3.0. Pinned by `an-xml-comment-is-written-after-the-value-it-sits-beside` and
`xml-an-exposed-scalar-consumes-its-content-token-index`, so the behaviour cannot drift while the
limit stands.

### 1.22 *(resolved)* `WARN010` was raised for a mapping nothing inferred

Resolved by [#90 (closed)](https://github.com/stop-cran/namespace2xml/issues/90), and present in
`3.0.0-preview.3`. Where a JSON or YAML document wrote a mapping at a path and a **later** document
wrote a sequence there, §17.1 kept the later container and the run then also raised `WARN010`
against the first document — naming it as having written a mapping "whose keys are all canonically
numeric" whatever its keys actually were, and offering `type=mapping` to undo a §8.7 inference that
never ran. Every factual clause of the message was false for that input.

The warning is advisory, so nothing was mis-rendered; what it cost was the diagnostic's credibility
on the one case where a reader most needs it, two documents disagreeing about a node's shape. The
implementation had derived "was inferred" from "renders as a sequence", which §17.1's shape contest
also produces. Step 11 now discards the provenance at a node it declined to infer, so the test means
what §3.2 says. Pinned by `warn010-is-not-owed-when-a-sequence-wins-the-shape-contest`, which
asserts the whole diagnostic stream rather than one record — the defect was an extra warning, and
only a stream assertion can fail on an extra.

## 2. Acceptance coverage

`conformance/assertions.json` records **every** acceptance requirement from specification §26, each
with a status; that file is generated from §26 and is the count of record. Items marked `pending`
have **no fixture coverage yet** and no claim is made about them. Items marked `required` are
covered and can never lose coverage.

Do not read a passing test run as evidence about a `pending` item.

Two specified conditions had no fixture at all until
[#47 (closed)](https://github.com/stop-cran/namespace2xml/issues/47) landed: Section 22 listed
diagnostic members per *code* while the mapping appendix enumerated *conditions*, and Appendix C.4
compares members exactly, so an omitted member was an assertion of absence that the specification
did not determine. Appendix B now states the member set each *condition* supplies, and both fixtures
have since been authored — `merge-error-rejects-a-second-source-contribution` for §16.10
`merge=error`, and `WILDCARD002` across the four wildcard-bound cases.

### 2.1 The INI dialect names no third-party parser, and is verified against none

Acceptance item 28 asks for "the documented INI dialect against representative parsers", and
Section 19.6 names the dialect `PortableIni1`. It also now says what an implementation owes the
question: the compatibility documentation "names the parsers it holds itself interoperable with,
and conformance tests must cover every parser it names", and "naming none is a permitted and
complete answer".

**3.0 names none, deliberately, and this entry is that statement.** `PortableIni1` is verified
against the specification and against no external reader. `IniSerializerTests` and the
`.ini`-producing fixtures compare the serializer's own bytes against expected bytes authored from
Section 19.6, which establishes that the output is stable and matches the specification's
description of the dialect — not that any real parser reads it back as the same key-value model.
Those are different claims, and only the second is what item 28 asks for.

Naming a parser is not paperwork. Feeding the corpus's `.ini` files to Python's `configparser`
rejects **10 of 17** with `MissingSectionHeaderError`, because Section 19.6 emits a scalar path of one
part as a global key in a preamble before the first section header and `configparser` has no default
section. The first parser anyone would name is therefore one this dialect could not be made to
satisfy at all, in its most ordinary case, and the incompatibility was in the projection rule rather
than in either dialect switch — so neither `QuoteValues` nor `EscapeMultiline` avoided it.

That much is now fixed. `inioutputoptions=GlobalSection` writes the global keys into a leading
section named `global` instead of a preamble, and a file produced under it is accepted by
`configparser`; the option, its `WARN012` counterpart on an unguarded preamble, and the blocking
`FLAT001` when a path already projects to that section are pinned by acceptance item 88. Tracked as
[#88 (closed)](https://github.com/stop-cran/namespace2xml/issues/88). The count above is what the
corpus looks like with the option *not* selected, which is how most of its cases are written: they
are about something else and set no INI options at all.

What remains is the original item-28 claim, which the option does not settle. That a `GlobalSection`
file is accepted by one parser on one machine is a spot check, not a verification lane: there is
still no named parser list, no coverage of the dialect switches against real readers, and no harness
that would notice a regression. Building the lane also needs a decision about which parsers are
normative — plausibly
`configparser`, `ini4j`, and whatever the Windows profile API accepts, each under both
`QuoteValues` settings and both `EscapeMultiline` settings, since those options are where dialects
disagree elsewhere. That list is a compatibility policy this release does not state, and
Section 19.6 makes it normative by referring to it, so it has to be written down before any harness
is built. Tracked as [#67](https://github.com/stop-cran/namespace2xml/issues/67).

Until then, treat `PortableIni1` output as specified-and-self-consistent rather than as verified
interoperable, and read `docs/format-ini.md` before pointing it at a specific consumer. Item 28
stays `pending` and is the largest single gap in the corpus.

## 3. Platform and environment

- **Supported:** Linux, Windows and macOS on x64 and arm64, via the .NET 10 runtime.
- **Not yet validated:** nothing. The Windows publication path walks retained directory handles and
  is therefore TOCTOU-safe by construction rather than by a check; `spikes/windows-publication`
  carries the prototype and the reasoning. Two of that spike's cases could not be exercised where it
  ran, because creating a symbolic link needed privileges that were unavailable; they are recorded
  as untested rather than as passing, and the refusal they would have exercised is reached anyway by
  the privilege-free junction cases, because the check tests for a reparse point rather than for a
  particular tag.
- **The shipped publication sink walks retained handles.** `FileSystemPublicationSink` opens the
  output root once as a trust anchor and then opens every path component relative to the handle of
  its parent, refusing any component that carries a reparse point, so no full path string is ever
  re-parsed and the check-then-open race is closed by construction rather than by a check. The
  primitive is `NtCreateFile` with `OBJECT_ATTRIBUTES.RootDirectory` on Windows and `openat` with
  `O_NOFOLLOW` elsewhere. A host that is neither reports `PATH001` and creates nothing, which is
  what §21.1 means by failing "before creating directories or opening destinations". The output
  root itself is opened by path, so a link *in the root you configured* is followed — that root is
  the anchor you chose, not something an attacker introduced beneath it.
- **The `openat` sink has not been executed on a POSIX host during development.** It was written
  against the same design as the Windows implementation and is exercised by the Linux and macOS
  legs of CI, which is where its evidence comes from; no local run stands behind it. If it is
  wrong, the whole Linux conformance corpus fails loudly rather than silently degrading, which is
  why this is recorded as a provenance note rather than as a risk.
- **The portable conformance corpus cannot plant a symbolic link or junction.** Appendix C gives a
  fixture `inputs/`, `schemes/` and `expected/`, all of which are ordinary files that git stores and
  the harness copies; there is no reserved name that means "create this link before the run". A
  corpus that needed one would also stop being portable, because creating a symbolic link on
  Windows requires a privilege that ordinary accounts do not hold, so the fixture would be
  unrunnable rather than merely unsupported for a third-party implementer. Escape refusal is
  therefore asserted in the test suite — `PublisherTests` and `SecureDirectoryTests`, which create
  real links and junctions and skip explicitly when the host forbids it — and the corpus asserts
  only what it can express portably, which is the non-directory output root. The CI job
  `publication-invariants` runs both on Linux and Windows so that neither platform's primitive is
  evidenced solely by the other's green run.
- **Hard-link escape is out of scope.** A destination reached through a hard link to a file outside
  the output root cannot be detected by any no-follow walk, on any platform, because a hard link is
  not distinguishable from the original name. An optional refusal based on link count is
  demonstrated in the spike but is not enabled.
- **Native AOT** is a non-blocking investigation, not a shipped configuration. Just-in-time startup
  measures ~66 ms median on `win-arm64`, and the code currently draws no AOT, trim or single-file
  analyzer complaints — but half the pipeline is still unimplemented, so that zero is a baseline and
  not a verdict. Linking and cross-platform startup are measured by the dispatchable
  `native-aot-spike` workflow. The bar it has to clear, and the trap that already cost time, are in
  [`spikes/native-aot/FINDINGS.md`](spikes/native-aot/FINDINGS.md). Revisit at M6.
- **YAML comment preservation** relies on a two-pass read: parser events for structure, plus a
  second scanner pass for the comment token inventory, because the parser event stream truncates
  comment-only documents and misreports inline-ness on root values. This is proven in
  `spikes/yaml-comments` but not yet implemented.

- **Globalisation is invariant by construction.** The tool sets `InvariantGlobalization`, so
  behaviour cannot vary with the host locale. This is deliberate and will not become configurable:
  it is what makes byte-identical output achievable.

## 4. Documented but not yet enforced

Some rules in `CONTRIBUTING.md` are stated as binding and have a CI gate; a few do not yet.

| Rule | Gate |
|---|---|
| C1 requirement-and-fixture-first | Partial — the manifest and traceability tests exist; nothing reads acceptance items out of the pull-request body, so "fails before" is reviewer-verified |
| C2 cite the specification | Partial — anchors are constrained by the registry and schema tests, but the citation itself is a template field, not a gate |
| C3 specification decision precedes acceptance | Contract-revision job, active |
| C4 bidirectional traceability | Active |
| C5 determinism | Active — `tools/hash-corpus-outputs.ps1` measures exit status, standard output, standard error and the produced file tree, for every argument vector each case declares, repeats each measurement under the three environments Appendix C.7 requires (differing in parser worker count, locale decimal convention and time zone) into a fresh output root each time and requires them to agree, and `cross-os-hash` requires all three platforms to agree |
| C6 side-effect invariants first | Not yet — publication is implemented, but no §21 fixture exercises the symlink, escape or partial-write invariants, and no workflow contains a job by that name |
| C7 evidence must be able to fail | Not machine-checkable, and probably cannot be. `HarnessSelfTests` proves the comparer's own rules reject what they claim to; everything else — mutation proof for a new test, and the observability of an assertion — is a reviewer obligation |

Stating a rule before its enforcer exists is a deliberate choice, but it is a debt. It is recorded
here rather than left implicit. `CONTRIBUTING.md` §3 repeats these qualifications inline; if the two
ever disagree, this table is the one that is maintained and the discrepancy is itself a bug report.

### 4.1 What the legacy differential lane does not prove

The Appendix C.6 lane runs namespace2xml 2.4.0 over the whole corpus and fails on any divergence no
case explains. Three things it does not establish, listed so nobody reads more into a green run than
is there.

- **Sampling refutes; it does not confirm.** A case is run ten times, so a baseline that falls both
  ways at an even rate is caught reliably. A rare branch is not: `json-strict-parsing-refusals` was
  measured reproducing its expected result on one run in forty, and `mask-after-sequence-rebasing`
  was recorded as `agrees` for weeks on the strength of a single lucky run. Other unstable cases may
  be sitting in the corpus under an `agrees`, `differs` or `crashes` verdict that ten runs happen not
  to contradict. When one surfaces it is a fixture correction, not a contract change.
- **`nondeterministic` is asserted, not checked.** Because no bounded sample can refute it, C.6 makes
  it the one verdict the harness accepts on the contributor's word. It is a real escape hatch. What
  keeps it honest is publication rather than enforcement: `docs/migration-2.x-to-3.0.md` lists every
  case that claims it, with the prose that has to carry the measurement.
- **An `agrees` verdict can be weak evidence.** The verdict compares the observable result — tree and
  exit code — and nothing else. A case that expects exit `1` and an empty tree `agrees` with a
  baseline that exits `1` because the option did not exist in 2.4.0 at all. That is the intended
  reading, and C.6 requires the prose to say which it is, but the *verdict alone* on such a case
  should not be read as evidence that 2.4.0 implemented the behaviour.

The lane runs on Linux only, on the .NET 9 runtime the baseline package targets. Divergence between
2.4.0's Windows and Linux behaviour beyond the exit-code convention for an unhandled exception is
unmeasured.

## 5. Documentation gaps

- The specification does not fix everything the tool decides. The `r69` review found 39 places where
  `docs/specification.md` is silent, ambiguous or under-determined — the argv character model, the
  format of the derived artifacts it names, host termination exit codes, warning ordering, several
  output-format literal spellings — and those are deferred to 3.1 and listed in
  [#92](https://github.com/stop-cran/namespace2xml/issues/92). **None is a divergence between the
  document and this build**: the behaviour is defined, deterministic and fixture-pinned in every
  case. What it means for you is narrower and worth stating plainly: on those points the
  specification will not let you predict the tool, so read the fixture or ask. A second
  implementation written from the document alone could legitimately differ.
- `docs/usage-methodology.md` now carries the layering guidance, a worked cross-format
  specialization pipeline, and the fixture discipline. What is still thin is breadth: one worked
  pipeline is not a cookbook, and the multi-output cases are unwritten.
- `docs/migration-2.x-to-3.0.md` is assembled from each fixture's `legacy.md` as fixtures land, so it
  is incomplete until the corpus is.
- There is no cookbook, and there probably should be. If you built something with this tool and had
  to work it out yourself, that is a **usage gap** report and it is the most valuable kind.

The five per-format guides — `docs/format-namespace.md`, `format-json.md`, `format-yaml.md`,
`format-xml.md` and `format-ini.md` — are written and cite the clause behind each rule. Where a
guide and this file disagree about what the build does, this file is the one kept current against
the corpus; report the difference.

## 6. Things that are deliberately not going to change

Listed so nobody spends effort proposing them.

- **No snapshot-update mode.** No `--update-snapshots`, no regeneration of expected fixture output
  from the tool's own output. A test that records the implementation's opinion validates nothing, and
  the ability to distinguish correct from customary is the project's main asset.
- **No locale-sensitive behaviour.** See §3.
- **No publication from a branch.** Releases come from tags only.
- **No relaxation of the validation gate.** If any output fails validation, nothing is written. There
  will not be a `--force`.

## 7. How to report against this file

| Your situation | Route |
|---|---|
| An entry here is wrong or out of date | [Bug report](https://github.com/stop-cran/namespace2xml/issues/new?template=bug_report.yml) |
| You need something in §1 or §5 sooner | Comment on the tracking issue for that milestone |
| Something surprised you and is *not* listed here | [Pick a form](https://github.com/stop-cran/namespace2xml/issues/new/choose) — an unlisted gap is exactly what the preview is for |

Always include the `contract-bundle` revision from `--version`.
