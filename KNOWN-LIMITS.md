# Known limits

**As of `3.0.0-preview.1`, contract bundle `r30+35e144372ca0`. Dated 2026-08.**

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
| References and value wildcards | Not yet | §13 |
| Templates and masks | Not yet | §8.6, §12 |
| Wildcard output selectors and `substitute` | Not yet | §14, §16 |
| Ordered sequences from numeric paths | Not yet | §8.7, §5.4 |
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
  for a literal asterisk works and is tested. §9.1 keeps the syntax reserved; §12 evaluation is a
  later milestone in any case.
- **`substitute=Key` and `substitute=None` are parsed and not applied.** This is not specific to
  JSON — no format applies them yet, because the machinery §13.4 describes does not exist. A scheme
  that sets `substitute` is accepted and has no effect on any input.
- **§4.4 exclusive-shape resolution has no end-to-end coverage.** An empty container is recorded as
  a shape contribution and its precedence is proved by unit test, but neither implemented output
  format requires one exclusive shape — §19.1 and §19.6 both emit a scalar and its descendants
  together — so no conformance fixture can yet observe the contest, or the shape-conflict warning it
  produces. This closes when JSON or YAML rendering lands.

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
- **A wildcard in a key is declined**, with exit `70` and no output, exactly as for JSON. `\*` for a
  literal asterisk works.
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
- **Element-only children carry no separately addressable content token.** §11.4 says every parent
  assigns ordering values "including element-only parents", and that element-only children retain
  element-name addressing "while also carrying their content-token ordering value". This preview
  materializes the element-name address only. Document order is preserved — it is what the §5.2
  position marks record — but `a.#1` does not select an element-only child, and a comment among
  element-only children has no address at all.
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

## 2. Acceptance coverage

`conformance/assertions.json` records all 86 acceptance requirements from specification §26, each
with a status. Items marked `pending` have **no fixture coverage yet** and no claim is made about
them. Items marked `required` are covered and can never lose coverage.

Do not read a passing test run as evidence about a `pending` item.

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
