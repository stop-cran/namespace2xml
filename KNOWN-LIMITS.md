# Known limits

**As of `3.0.0-preview.1`, contract bundle `r33+925af40f8fec`. Dated 2026-08.**

This file exists because a project that claims completeness cannot receive feedback: every gap reads
as user error, and the reporter concludes they are holding it wrong. During the preview this list is
long, and that is correct. It shrinks as milestones land.

If something you need is here, **say so** — an entry on this list is a statement of current state,
not a refusal. Adding your case to the relevant thread is what moves it.

---

## 1. Implementation completeness

The 3.0 rewrite lands in milestones that follow the specification's own pipeline order. A stage that
has not landed is **not implemented**, and the tool exits with a non-normative status rather than
pretending to succeed.

The tool currently transforms the **flat family end to end**: namespace-profile, JSON, YAML and XML
input, overlaying, output planning, and publication of namespace, quoted-namespace and INI
destinations. Everything below that line is refused, not approximated.

| Area | State | Specification |
|---|---|---|
| Command line, informational modes, diagnostics encoding | Implemented | §6, §6.4 |
| Contract bundle reporting | Implemented | §22 |
| Namespace-profile input parsing, encoding detection, budgets | Implemented | §7–§9 |
| JSON **input** | Implemented, with the reductions in §1.1 | §7.1, §9, §15.1 |
| YAML **input** | Implemented, with the reductions in §1.2 | §7.1, §10, §15.1 |
| XML **input** | Implemented, with the reductions in §1.3 | §7.1, §11, §15.1 |
| Scheme parsing, `output`, `filename`, `root`, `delimiter` | Implemented, namespace-profile scheme files only | §16 |
| Overlaying, precedence, mapping order after override | Implemented | §5, §10 |
| Scalar inference and canonical numeric text | Implemented | §18 |
| Output planning, destination paths, collision folding | Implemented | §17 |
| Rendering: namespace, quoted namespace, INI | Implemented | §19.1–§19.2, §19.6 |
| Publication and the validation gate | Implemented | §21 |
| References and value wildcards | Implemented, except the `REFERENCE005` case in §1.9 | §13 |
| Templates and masks | Implemented for namespace input | §8.6, §12 |
| Wildcard output selectors | Implemented | §14 |
| Path-scoped view transformations: `type`, `key`, `multiline` | Implemented, with the gaps in §1.10–§1.12 | §16.5, §16.6 |
| `substitute` | Not yet | §16.7 |
| Ordered sequences from numeric paths | Implemented, except the §3.2 warning in §1.7 | §8.7, §5.4 |
| Rendering: JSON, YAML, XML | Not yet | §19.3–§19.5 |
| **Scheme files** written as JSON, YAML or XML | Not yet | §15 |

A preview binary returns exit status `70` when an invocation needs something in the second half of
that table. That status is deliberately outside the contract: `0` and `1` are normative, and a
preview must never return either for work it did not do. It is a **refusal**, not a diagnostic — the
run decides no outcome at all, publishes nothing, and says on standard error which capability it
lacked. A step that could not do its job never passes its input through, because a plausible wrong
file is worse than no file.

### 1.1 Reductions inside JSON input

JSON input is complete for §9 syntax, the §18 typed-scalar rules and the §15.1 projection, and these
three cases are declined or unfinished within it.

- **A wildcard in a native key is declined**, with exit `70` and no output. `{"*": 1}` has no
  representation in the overlay this preview builds, so it is refused rather than guessed at. `\*`
  for a literal asterisk works and is tested. §9.1 keeps the syntax reserved. §12 evaluation is
  implemented for namespace-profile rules; what is missing is §12.3's requirement that
  "template-bearing JSON or YAML branches are extracted entry-by-entry", because the entry a
  structured reader emits carries an interpreted value and cannot express a typed payload, an empty
  container, or a sequence ordering value beneath a wildcard key. Acceptance item 12 covers it.
- **`substitute=Key` and `substitute=None` are parsed and not applied.** This is not specific to
  JSON — no format applies them yet, because the machinery §13.4 describes does not exist. A scheme
  that sets `substitute` is accepted and has no effect on any input.
- **§4.4's *scalar-versus-container* contest has no end-to-end coverage.** An empty container is
  recorded as a shape contribution and its precedence is proved by unit test, but neither
  implemented output format has to choose between a scalar and a container — §19.1 and §19.6 both
  emit a scalar and its descendants together — so no conformance fixture can yet observe *that*
  contest. This closes when JSON or YAML rendering lands. The neighbouring *mapping-versus-sequence*
  contest is covered: a flat output does spell exactly one container shape, and
  `conformance/namespace-shape-conflict-precedence` pins both directions of the §17.1 precedence
  rule and the `TYPE002` warning that reports the omitted shape.

A reference nested inside a native sequence is a fourth case, but it cannot be reached: any
unresolved value declines the whole invocation under §13, so no path exists today by which the
overlay is consulted. It is recorded in the source at `StructuredProfileReader.BuildSequence` as a
prerequisite for §15.1 step 15 rather than as a limit you can encounter.

### 1.2 Reductions inside YAML input

YAML input implements the whole of §10.1's `RestrictedYaml1` schema and every §10.2 and §10.3
refusal, and shares the §15.1 projection with JSON. These cases are declined or unfinished within
it.

- **Comments are not retained.** §10.1 lists comment support among the subset's features, and this
  preview parses a comment and discards it. Nothing is misreported — a document with comments reads
  correctly and its values are right — but a scheme that would carry comments through to an output
  gets none. The design for the two-pass association layer this needs is worked out and recorded in
  `spikes/yaml-comments/FINDINGS.md`, against a 28-document corpus; only the implementation is
  outstanding. Comment **output** is unimplemented for every format, so nothing observable is lost
  today.
- **A wildcard in a key is declined**, with exit `70` and no output, exactly as for JSON, and for the
  same §12.3 reason. `\*` for a literal asterisk works.
- **`substitute` is parsed and not applied**, again as for JSON and every other format.

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

- **`NormalizeFormattingWhitespace` is declined**, with exit `70` and no output. Only §11.7's
  default `PreserveWhitespace` is implemented, and it "retains every text node". **The consequence
  is worth stating plainly, because it will surprise you:** an indented document is therefore mixed
  content. `<a>\n  <b>1</b>\n</a>` addresses as `a.#0`, `a.#1.b` and `a.#2` — not as `a.b`. Element
  name addressing today requires XML written with no formatting whitespace between element
  children, so there is at present **no way to read a pretty-printed document by element name**.
  Nothing is misreported and the fixture `xml-canonical-addresses` pins both spellings, but if you
  are pointing this tool at XML a human wrote, say so: the opt-out §11.7 defines is exactly what
  you need and it is the next thing to land here.
- **Mixedness and repeated-child classification are per document.** §11.4 makes them "properties of
  the merged common-model element", "evaluated at concrete merge time across all input
  contributions to that element". This preview classifies each element from the one document that
  contains it. A single document is therefore always right; what is not yet right is two documents
  contributing to the same element — two sources that each supply one `<b/>` produce an override
  rather than the two-item sequence §11.4 requires, and the §11.4 singleton-to-sequence promotion
  and its `WARN009` cannot occur. Overlaying XML onto XML at the same path is the case to avoid.
- **Comments are not retained.** §11.5 retains them "as ordered comment nodes" and §11.4 allows
  selecting one "for ignore and conversion through `#n`". This preview parses a comment and
  discards it — the same common-model gap as §1.2's YAML entry, since structured input has no
  comment facet. Its §11.4 ordering value **is** spent, so siblings keep their positions:
  `<a>t<!--c-->u</a>` addresses its two text runs as `#0` and `#2`, never renumbered to `#0` and
  `#1`. Comment output is unimplemented for every format, so nothing observable is lost today.
- **CDATA is not retained as a distinct node kind.** §11.6 keeps it distinct so that XML output can
  re-emit it as CDATA. The coalescing rule itself is implemented exactly as written — adjacent
  CDATA and adjacent ordinary text coalesce separately and never with each other — but the run's
  CDATA-ness does not survive projection into the common model, so from that point it is text.
  XML rendering is unimplemented, so nothing observable is lost today; this closes with §19.5.
- **Element-only children carry no separately addressable content token, and interleaved repeats
  lose their document order.** §11.4 says every parent assigns ordering values "including
  element-only parents", and that element-only children retain element-name addressing "while also
  carrying their content-token ordering value for deterministic placement". This preview
  materializes the element-name address only, so `a.#1` does not select an element-only child and a
  comment among element-only children has no address at all.

  The consequence is a real loss, not only a missing address. §11.4 also says repeated same-name
  children "form a sequence at `parent.child`", so a repeat is recorded as one property at its
  first appearance; without the content-token value there is nothing left that says where its later
  occurrences stood among differently-named siblings. `<a><b>1</b><c>2</c><b>3</b></a>` and
  `<a><b>1</b><b>3</b><c>2</c></a>` therefore produce the same model, and the same output, in this
  preview — **verified**, not merely expected. §11.4 makes content-token values "determine
  placement in the parent's serialized stream", so this becomes observable when §19.5 lands and the
  first document must serialize back with its `<c>` between the two `<b>` elements. Until then no
  implemented output format orders element-only children by anything but first appearance, so
  nothing observable is lost today.

  An earlier revision of this file claimed here that "document order is preserved — it is what the
  §5.2 position marks record". That was wrong for exactly the interleaved case above, and it is
  recorded rather than quietly deleted because a reassurance is worth less than nothing when it is
  the thing that stops you checking.
- **The content-token alias for a sole text node is not materialized.** §11.4 calls the element path
  and its sole text or CDATA content-token path "two canonical addresses for one scalar identity".
  This preview exposes the element path only, so `<b>two</b>` is addressable as `b` and not as
  `b.#0`.
- **Processing instructions are discarded**, with one `WARN006` per document that carried any.
  §11.8 places them outside the preservation contract.
- **`substitute` is parsed and not applied**, as for every other format.

### 1.4 Scheme files must be namespace profiles

§15 says "Scheme files may use the same case-insensitive format extensions as input files for
compatibility", and that JSON, YAML and XML scheme files "use secure default parsing". This preview
reads only the namespace-profile form, which the same section calls "the canonical and recommended
representation".

A `-s` file whose name ends in `.json`, `.yaml`, `.yml` or `.xml` is therefore **declined**, with
exit `70` and no output, naming §15. It is refused before it is read rather than handed to the
namespace parser: parsing a JSON scheme as a namespace profile reports `PARSE001` against §8.1,
which names a contract the file was never written to and asks its author to repair syntax that is
already correct for the contract §15 pointed them at. A wrong diagnostic costs more than a refusal.

The extension decides, not the content — a scheme written in namespace-profile syntax but saved as
`scheme.json` is still declined, and one saved as `scheme.ini`, `scheme.sh` or with no extension at
all is still read, exactly as §7.1 treats input files.

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

### 1.6 `Q{}local-name` is not separately addressable, by decision

§11.4 now says a `Q{}local` component and an unmarked `local` component **are the same component**
and address the same overlay node, and that the marker "does not narrow the component — it narrows
the addressing". This build implements that: the lexer yields an ordinary component for an empty
URI, and `QualifiedElementPart` refuses to represent one, so the redundant form does not exist.
**verified**

What the marker still does is opt a path out of the §13.1 and §15.2 simple alias index, so that an
attribute `@x` or a content token `#n` cannot compete with an element `x` for the same unmarked
spelling. **That behaviour is unimplemented**, and it is now reachable: §13.1 value references
resolve in this build, and `${a.b}` against a model carrying both `a.@b` and `a.b` reports
`REFERENCE004` naming two candidates. §11.4 offers `a.Q{}b` as the way to say "the element", and
that spelling does not survive the lexer, so the ambiguity has no in-band escape. Address the
attribute canonically as `${a.@b}` and rename the element, or vice versa. **verified**

`Q{}` cannot simply be admitted as a distinct component without contradicting the rest of §11.4,
which says the two spellings *are* the same node. The narrowing has to live in the reference
grammar rather than in the name, and where §15.2 directive binding needs the same distinction is
not yet settled. See issue #43 for the reasoning that settled the reading.

### 1.9 A canonical reference to an XML comment position reports `REFERENCE002`, not `REFERENCE005`

§13.1 says "a canonical reference directly addressing an XML comment path fails as a non-scalar
reference", which §22 codes as `REFERENCE005`. This build reports `REFERENCE002`, the missing
reference, for that case. **verified**

The cause is upstream of §13. `XmlInputReader` gives a comment an ordering value and nothing else —
`BoundComment(Text, Placement, Order)` carries no content ordinal — so at resolution time the model
cannot tell `${a.#0}` naming a comment from `${a.#0}` naming nothing at all. Both are a `#n` path
with no payload, and the more conservative of the two codes is the one that is always true.

Both are blocking errors at the same severity with the same exit code, so a correct run is
unaffected and an incorrect one still fails. What is lost is the message quality: a user who wrote
`${a.#0}` meaning the comment is told the path does not exist rather than that comments are not
values. Closing it means giving a comment a content ordinal in the overlay, which is the same
provenance change §1.7 and §1.8 need.

### 1.7 `WARN010` is not emitted

Section 3.2 requires "exactly one compatibility warning for each source contribution, canonical
mapping path, and output instance where a JSON or YAML mapping inferred at step 11 remains projected
as a sequence". The inference itself is implemented; `WARN010` is not emitted for it.
`{"a":{"2":"x","7":"y"}}` renders as the dense sequence §8.7 specifies, with an empty diagnostic
stream where one warning per contributing source is owed.

This is not silent success in disguise. The exception §3.2 grants is `type=mapping`, which this
build now implements — see the `type-mapping-keeps-numeric-keys-and-array-discards-names` fixture —
so a run that would earn the warning does have a way to act on it. What is missing is the prompt to
do so. Until then, read the absence of a diagnostic on a numeric JSON or YAML mapping as "not
checked" rather than "no compatibility risk", and reach for `type=mapping` on any numeric mapping
whose keys are data.

Emitting it needs per-source provenance the overlay does not retain: a node records the latest
contribution to each of its marks, not the set of sources that contributed, and "one per source
contribution" is a count over that set.

### 1.8 A wildcard contribution merges as one earlier-or-later value, not interleaved

§12.4 makes every generated `(rule,match)` result "a separate contribution for every merge strategy"
and merges it "at its deterministic rule/match position". Where a path already carries contributions
from several sources, this preview folds the generated value in as a single earlier-or-later
neighbour of what is already there, by comparing the rule mark against the latest mark at the node.

That is correct whenever the existing contributions all precede or all follow the rule. Where they
straddle it — an earlier source and a later source both wrote the path, and the template sits
between them — one binary split cannot express the true interleaving, and the generated value is
ordered against the whole rather than against each part.

Full fidelity means retaining each source's contribution at a path instead of the folded result,
which is the same change §1.7 needs. No case in the corpus distinguishes the two orderings today;
this entry exists so that one that does is read as a known gap rather than as a surprise.

### 1.10 A scheme path addresses an XML component canonically, and `SCHEME002` is never raised

§15.2 says "an unmarked component uses the simple alias index for compatibility and convenience;
if ordinary and XML components make that alias ambiguous at a matched location, selector expansion
at pipeline step 13 emits blocking `SCHEME002`". This build matches a scheme path against the same
typed component model the data uses and nothing else, so `a.x` in a scheme binds to the element `x`
and never to the attribute written `a.@x`. `SCHEME002` consequently has no call site. **verified**

This is the §15.2 half of the same unimplemented alias index as §1.6, and it fails in the safer
direction: the canonical spelling is always unambiguous, so a directive either binds to exactly what
it names or binds to nothing and earns the `WARN009` from §15.2. What is lost is the compatibility
affordance — a 2.x scheme that wrote `a.x` to mean an attribute silently stops binding, with a
warning rather than a wrong target. Write `a.@x`.

### 1.11 A directive bound beneath a node that a later step-16 pass reshapes becomes inert

§15.1 says "a transformation does not cause scheme matching to restart against newly created paths",
and this build honours that by binding every rule once, against the pre-transformation paths, before
any pass runs. The passes then walk in the §15.1 order, each over the previous pass's output.

The consequence is that a rule bound at, say, `a.b.c` is unreachable to a *later* pass if an
earlier pass renamed `b` — `type=array` at `a` turns `b` into the ordering value `0`, and `key` at
`a` turns it into a record. The rule matched a path that no longer exists, and the later pass walks
the new names, so nothing applies it and nothing reports it: the `WARN009` in §15.2 counts the rule
as bound, because it did bind.

§16.6 specifies this outcome for the one case it considers — a directive under an ignored path is
"inert and emits the unbound-directive warning" — and says nothing about the conversion cases. The
behaviour here is inertness without the warning. Order the directives so that a reshaping `type` or
`key` at an ancestor is the last thing that happens to a subtree, and treat a directive under one as
having no effect.

### 1.12 `key` does not merge with an independent sequence already at the node

§16.5 says of the sequence a `key` transformation produces: "If an independent sequence projection
already exists at the same node, the transformed contribution merges with it under the effective
`merge` strategy." This build replaces rather than merges: the converted node is built from an empty
sequence, and any sequence projection that was already there is discarded. **verified**

The case needs a node carrying both an ordered mapping and an explicit independent sequence, which
§4.4 already resolves in favour of one of them for every format this build writes, so no corpus
fixture reaches it. It is recorded because the specification names a merge strategy there, and a
future format that renders both projections would make the difference visible.

## 2. Acceptance coverage

`conformance/assertions.json` records all 86 acceptance requirements from specification §26, each
with a status. Items marked `pending` have **no fixture coverage yet** and no claim is made about
them. Items marked `required` are covered and can never lose coverage.

Do not read a passing test run as evidence about a `pending` item.

Two specified conditions had no fixture at all until
[#47](https://github.com/stop-cran/namespace2xml/issues/47) landed: Section 22 listed diagnostic
members per *code* while the mapping appendix enumerated *conditions*, and Appendix C.4 compares
members exactly, so an omitted member was an assertion of absence that the specification did not
determine. Appendix B now states the member set each *condition* supplies, and both fixtures have
since been authored — `merge-error-rejects-a-second-source-contribution` for §16.10 `merge=error`,
and `WILDCARD002` across the four wildcard-bound cases. Nothing under this heading is outstanding.

## 3. Platform and environment

- **Supported:** Linux, Windows and macOS on x64 and arm64, via the .NET 10 runtime.
- **Not yet validated:** nothing. The Windows publication path is proven by the
  `spikes/windows-publication` prototype, which walks destinations component-by-component with
  `NtCreateFile` relative to retained parent handles, and is therefore TOCTOU-safe by construction
  rather than by a check. Two cases could not be exercised where the spike ran because creating a
  symbolic link needed privileges that were unavailable; they are recorded as untested rather than
  as passing.
- **The shipped publication sink is not the spike.** `FileSystemPublicationSink` resolves a path,
  checks it, and then opens it, because .NET exposes no no-follow open. The retained-handle walk
  the spike demonstrates has not been adopted, so the shipped sink closes every escape that is
  present when it looks, and none introduced between the check and the open. Concretely: a link
  standing at a destination or at any ancestor is refused with `PATH001` before anything is
  created, and a resolved path outside the output root is refused likewise — but an attacker able
  to replace a component during publication is not defeated by this sink. Adopting the spike's walk
  is tracked for M4. Do not read Section 21.1 conformance here as a race-free guarantee.
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

## 5. Documentation gaps

- `docs/usage-methodology.md` is an outline. The layering guidance is sound; the worked pipelines are
  not written yet.
- `docs/migration-2.x-to-3.0.md` is assembled from each fixture's `legacy.md` as fixtures land, so it
  is incomplete until the corpus is.
- There is no cookbook, and there probably should be. If you built something with this tool and had
  to work it out yourself, that is a **usage gap** report and it is the most valuable kind.

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
