# Known limits

**As of `3.0.0-preview.2`, contract bundle `r37+2d644be6926e`. Dated 2026-08.**

This file exists because a project that claims completeness cannot receive feedback: every gap reads
as user error, and the reporter concludes they are holding it wrong. During the preview this list is
long, and that is correct. It shrinks as milestones land.

If something you need is here, **say so** — an entry on this list is a statement of current state,
not a refusal. Adding your case to the relevant thread is what moves it.

Every entry that owes a resolution **names the issue that owns it**, so that this file is a map of
current behaviour and the tracker is where the work is argued. Two kinds of entry name no issue and
that is not an oversight: one documenting a choice the specification explicitly permits (§1.5), and
one stating the boundary of what a piece of evidence proves (§4.1). Anything with no user-visible
symptom — a corpus gap, an internal to-do — is not written here at all; it lives only as an issue.

---

## 1. Implementation completeness

The 3.0 rewrite lands in milestones that follow the specification's own pipeline order. A stage that
has not landed is **not implemented**, and the tool exits with a non-normative status rather than
pretending to succeed.

The tool currently transforms **every input format and every output format the specification
defines**, end to end: namespace-profile, JSON, YAML and XML input, overlaying, references,
templates, output planning, and publication of namespace, quoted-namespace, INI, JSON, YAML and XML
destinations. Two capabilities remain unbuilt — the `substitute` directive and scheme files written
in a structured format — and both are refused rather than approximated.

| Area | State | Specification |
|---|---|---|
| Command line, informational modes, diagnostics encoding | Implemented | §6, §6.4 |
| Contract bundle reporting | Implemented | §22 |
| Namespace-profile input parsing, encoding detection, budgets | Implemented | §7–§9 |
| JSON **input** | Implemented, with the reductions in §1.1 | §7.1, §9, §15.1 |
| YAML **input** | Implemented, with the reductions in §1.2 | §7.1, §10, §15.1 |
| XML **input** | Implemented, with the reductions in §1.3 | §7.1, §11, §15.1 |
| Scheme parsing, `output`, `filename`, `root`, `delimiter`, `merge`, `filemerge` | Implemented, namespace-profile scheme files only | §16 |
| Overlaying, precedence, mapping order after override | Implemented | §5, §10 |
| Scalar inference and canonical numeric text | Implemented | §18 |
| Output planning, destination paths, collision folding | Implemented | §17 |
| Rendering: namespace, quoted namespace, INI | Implemented | §19.1–§19.2, §19.6 |
| Rendering: JSON, YAML | Implemented, except the §3.2 warning in §1.7 | §19.3–§19.4 |
| Publication and the validation gate | Implemented | §21 |
| References and value wildcards | Implemented, except the `REFERENCE005` case in §1.9 | §13 |
| Templates and masks | Implemented for namespace input | §8.6, §12 |
| Wildcard output selectors | Implemented | §14 |
| Path-scoped view transformations: `type`, `key` | Implemented, with the gaps in §1.10–§1.12 | §16.5, §16.6 |
| Ordered sequences from numeric paths | Implemented, except the §3.2 warning in §1.7 | §8.7, §5.4 |
| Rendering: XML | Implemented | §19.5 |
| `substitute` | Not yet — [#65](https://github.com/stop-cran/namespace2xml/issues/65) | §16.7 |
| **Scheme files** written as JSON, YAML or XML | Not yet — [#66](https://github.com/stop-cran/namespace2xml/issues/66) | §15 |

A preview binary returns exit status `70` when an invocation needs one of the two `Not yet` rows.
That status is deliberately outside the contract: `0` and `1` are normative, and a
preview must never return either for work it did not do. It is a **refusal**, not a diagnostic — the
run decides no outcome at all, publishes nothing, and says on standard error which capability it
lacked. A step that could not do its job never passes its input through, because a plausible wrong
file is worse than no file.

### 1.1 Reductions inside JSON input

JSON input is complete for §9 syntax, the §18 typed-scalar rules and the §15.1 projection, and these
two cases are declined or unfinished within it.

- **A wildcard in a native key is declined**, with exit `70` and no output. `{"*": 1}` has no
  representation in the overlay this preview builds, so it is refused rather than guessed at. `\*`
  for a literal asterisk works and is tested. §9.1 keeps the syntax reserved. §12 evaluation is
  implemented for namespace-profile rules; what is missing is §12.3's requirement that
  "template-bearing JSON or YAML branches are extracted entry-by-entry", because the entry a
  structured reader emits carries an interpreted value and cannot express a typed payload, an empty
  container, or a sequence ordering value beneath a wildcard key. Acceptance item 12 covers it. This
  is the same defect as the YAML half in §1.2 and shares its issue,
  [#57](https://github.com/stop-cran/namespace2xml/issues/57) — but note that only the YAML half is
  a regression against 2.4.0, because 2.4.0 had no JSON input at all.
- **`substitute` is refused, not ignored.** This is not specific to JSON — no format applies it,
  because the machinery §13.4 describes does not exist. A scheme that sets `substitute` in any of
  its forms ends the run with exit `70` and a message naming the directive and the file it came
  from, rather than accepting it and quietly doing nothing. Tracked as
  [#65](https://github.com/stop-cran/namespace2xml/issues/65).

A reference nested inside a native sequence is a third case, but it cannot be reached: any
unresolved value declines the whole invocation under §13, so no path exists today by which the
overlay is consulted. It is recorded in the source at `StructuredProfileReader.BuildSequence` as a
prerequisite for §15.1 step 15 rather than as a limit you can encounter.

### 1.2 Reductions inside YAML input

YAML input implements the whole of §10.1's `RestrictedYaml1` schema and every §10.2 and §10.3
refusal, and shares the §15.1 projection with JSON. These cases are declined or unfinished within
it.

- **Anchors, aliases, tags and merge keys are refused rather than retained.** A comment attached to
  a construct that §10.2 declines never reaches the model, because the document does not.
- **A wildcard in a key is declined**, with exit `70` and no output, exactly as for JSON, and for the
  same §12.3 reason. `\*` for a literal asterisk works. **This one is a regression against 2.4.0.**
  The differential lane measured 2.4.0 producing §10.4's worked example correctly for
  `conformance/yaml-wildcard-template-in-a-native-key-is-declined`, so for that input a 2.x user
  gets the right file today and gets nothing from this preview. It is the only case in the corpus
  where the baseline satisfies the specification and this implementation does not. Closing it is a
  blocker for 3.0 final rather than a deferred nicety, and is tracked as
  [#57](https://github.com/stop-cran/namespace2xml/issues/57).
- **`substitute` is refused with exit `70`**, again as for JSON and every other format.

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
  `a.b` lands beside the real element rather than on it, silently, which is
  [#40](https://github.com/stop-cran/namespace2xml/issues/40) and is worked through in
  [docs/usage-methodology.md](docs/usage-methodology.md) §3. The fixture `xml-canonical-addresses`
  pins both spellings.
- **A promoted sequence item's place in the serialized XML stream is not pinned.** §11.4 evaluates
  mixedness and repeated-child classification "across all input contributions to that element", and
  this tool does: a mixed contribution re-addresses an element-only contribution's children as
  content nodes, and a contribution that repeats a name makes another contribution's singleton an
  item of that one sequence. The *addresses* are settled. What is not settled is where a
  concatenated item is *drawn*: §11.4 gives that job to the content token — "content-token values
  determine placement in the parent's serialized stream only" — but never says how a second
  document's tokens relate to the first's, and an item promoted out of another document carries a
  token from that document's counter. Converted mixed content is allocated above the merged
  element's high-water mark, so it is ordered; a promoted sequence item keeps its own token and can
  therefore be drawn among the items it follows in the address space. The fixtures pin the addresses
  in both cases and the serialized stream only for the mixed case. §11.4 carries a non-normative
  open question recording this, so the two cannot drift apart; filing it as a specification
  ambiguity is the fast-follow.
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

  One reduction remains inside XML itself: a comment is addressed at `#n`, and `#n` does not yet
  select an element-only child, so `type=ignore` and the §11.4 conversions reach a comment in mixed
  content but not one sitting among element-only children.
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
  elements, and a namespace-profile override at `a.b.1` changes the third element's text without
  moving it. What is still missing is the *address*: `a.#1` does not select an element-only child,
  so a comment among element-only children has no address at all, and neither does an element-only
  child selected positionally rather than by name.

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
- **`substitute` is refused with exit `70`**, as for every other format.

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
all is still read, exactly as §7.1 treats input files. Tracked as
[#66](https://github.com/stop-cran/namespace2xml/issues/66), which also carries the question §15
leaves open: whether a native sequence is a legal spelling for a directive that a profile writes as
a comma-joined string.

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

### 1.6 `Q{}local-name` narrows addressing in references, and not yet in scheme paths

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

The §15.2 half is still open, but vacuously: scheme paths do not consult the alias index at all, so
a marker there has nothing to escape. See §1.10, which is the entry that has to close first, and
[#56](https://github.com/stop-cran/namespace2xml/issues/56), which owns it. Issue #43 carries the
reasoning that settled the §11.4 reading.

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
contribution" is a count over that set. Tracked as
[#58](https://github.com/stop-cran/namespace2xml/issues/58).

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
this entry exists so that one that does is read as a known gap rather than as a surprise. Tracked as
[#59](https://github.com/stop-cran/namespace2xml/issues/59), which asks for that case to be
constructed before either fix, since it may show the shape is unreachable.

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
provenance change §1.7 and §1.8 need. Tracked as
[#60](https://github.com/stop-cran/namespace2xml/issues/60).

### 1.10 A scheme path addresses an XML component canonically, and `SCHEME002` is never raised

§15.2 says "an unmarked component uses the simple alias index for compatibility and convenience;
if ordinary and XML components make that alias ambiguous at a matched location, selector expansion
at pipeline step 13 emits blocking `SCHEME002`". This build matches a scheme path against the same
typed component model the data uses and nothing else, so `a.x` in a scheme binds to the element `x`
and never to the attribute written `a.@x`. `SCHEME002` consequently has no call site. **verified**

This is the §15.2 half of the same unimplemented alias index as §1.6, and it fails in the safer
direction *on this surface*: the canonical spelling is always unambiguous, so a directive either
binds to exactly what it names or binds to nothing and earns the `WARN009` from §15.2. What is lost
is the compatibility affordance — a 2.x scheme that wrote `a.x` to mean an attribute silently stops
binding, with a warning rather than a wrong target. Write `a.@x`.

The *data* surface beside it is not so kind. A contribution written `a.x=v` against an attribute
`@x` creates an ordinary sibling and warns about nothing, which is specified behaviour — §8.2 scopes
the alias index to references and scheme paths — but is a silent wrong result for a migrating 2.x
profile. Both halves are tracked as
[#56](https://github.com/stop-cran/namespace2xml/issues/56).

The data half is now **pinned**, in both spellings, by
`conformance/xml-a-2-x-style-attribute-override-adds-a-sibling-element` and
`conformance/xml-an-attribute-from-xml-input-is-overridden-through-its-canonical-address`, so it
reaches the generated migration guide rather than living only here. The first of those measured
2.4.0 accepting `r.a.x=dev` against `<a x="base">` and overriding the attribute, which is what makes
this a migration hazard and not merely a specification detail. Write `a.@x`.

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
having no effect. Tracked as
[#49](https://github.com/stop-cran/namespace2xml/issues/49), which asks §15.1 to say what the
outcome should be.

### 1.12 `key` does not merge with an independent sequence already at the node

§16.5 says of the sequence a `key` transformation produces: "If an independent sequence projection
already exists at the same node, the transformed contribution merges with it under the effective
`merge` strategy." This build replaces rather than merges: the converted node is built from an empty
sequence, and any sequence projection that was already there is discarded. **verified**

The case needs a node carrying both an ordered mapping and an explicit independent sequence, which
§4.4 already resolves in favour of one of them for every format this build writes, so no corpus
fixture reaches it. It is recorded because the specification names a merge strategy there, and a
future format that renders both projections would make the difference visible. Tracked as
[#61](https://github.com/stop-cran/namespace2xml/issues/61), which asks first whether the shape is
reachable at all — if it is not, amending §16.5 may be the honest resolution.

### 1.13 A destination high-water mark is lost when `replace` removes the path entirely

§17.5 says "Every output contribution carries its complete per-path high-water map, including marks
raised by items hidden by output projection." This build carries marks on the overlay tree rather
than in a separate map, so a mark lives on the node whose sequence raised it. A destination
`filemerge=replace` that discards a path and does not itself name that path leaves the mark with
nowhere to live, and it is lost. **verified**

Marks below a path the replacement *does* name survive, which is the reachable case and is pinned by
`conformance/a-replaced-destination-keeps-the-high-water-mark`. The lost case needs four
contributions to one destination — one to raise the mark, one to replace without naming the path, one
to recreate it, and one to address an explicit ordering value the first had used — because §5.4
renumbers survivors densely from zero and hides every smaller difference.

Materialising an empty node to hold the orphaned mark was tried and rejected: it puts a path §16.10
has just removed back into the overlay tree, where wildcards, references and selectors find it again,
which trades a remote divergence for a common one. Closing this properly means carrying the map
beside the tree and re-addressing it through selector-prefix removal, `root`, `key` and `type`, as
§17.5 describes. Tracked as [#62](https://github.com/stop-cran/namespace2xml/issues/62).

### 1.14 A JSON or YAML key collision has no diagnostic

§3.3 requires a same-format round trip to preserve "data structure", and §8.2 gives JSON and YAML
mapping keys as "one ordinary literal component" that never acquires an XML node kind. Two distinct
overlay components can nevertheless spell the same structured key: an ordinary component whose
literal text is `@x`, read from JSON, and a typed attribute `x`, read from XML, both project to the
key `@x`.

The tool emits both members, producing a document with a duplicate key, exit status `0` and no
diagnostic. Its own reader then rejects that document with `PARSE001`. **verified**

There is no code to raise. §17.4 requires collision detection only of "every flat output", and §22
states that "`FLAT001` covers namespace, quoted-namespace, and INI post-projection key collisions" —
the closed diagnostic list assigns nothing to the structured formats. Inventing a code here would
put an unspecified blocking error on a default path, so the gap is recorded rather than filled. It
is filed as a specification gap in
[#51](https://github.com/stop-cran/namespace2xml/issues/51); the natural resolutions are to widen
`FLAT001` to every format whose projection can collide, or to add a structured-format counterpart.

Reaching it needs two input formats disagreeing about the same path, which is why no fixture in the
corpus produced it.

### 1.15 A value ending in a blank line is spelled double-quoted, not as a block scalar

§19.4 "uses literal block scalars for multiline values", and a value whose content ends in a blank
line needs the keep indicator `|+` to carry that line. The resulting block ends with two LFs, so it
cannot be the last thing in the file: §24 requires a text output to "end with exactly one LF". Such
a value is therefore written in the double-quoted form instead, in every position. **verified**

The obvious narrower rule — decline the block only where it would fall last — was rejected. It makes
a value's spelling depend on where it sorts among its siblings, so adding an unrelated key silently
rewrites an untouched value. The uniform rule keeps the spelling a property of the value alone.

Nothing is lost: the double-quoted form is exact, and a round trip through an independent parser
returns the value unchanged. What §19.4 promises literally — a block scalar — is not what is
emitted, which is the reason this is recorded here rather than treated as settled.

The underlying issue is that §19.4's blanket "uses literal block scalars for multiline values" has
never been the whole rule. A block scalar cannot carry a value containing CR, containing a control
character, whose lines have trailing whitespace, or whose first non-empty line is indented, and the
writer has always quoted those instead, because §3.3 requires the round trip to preserve the data.
The blank-line case is the same shape of exception with §24 in place of §3.3.
[#52](https://github.com/stop-cran/namespace2xml/issues/52) asks for the qualifier to be stated
explicitly, covering all of them rather than this one.

### 1.16 A top-of-file comment binds differently in namespace and YAML input

§8.5 states of namespace input that "consecutive comments are associated with the next entry",
without qualification, so a comment at the top of a namespace profile becomes a leading comment of
the first entry. §20 classifies comments across every format and scopes its leading rule explicitly
to "a comment between two payloads or items", which carves the first position out of it: "a comment
before the first payload or item is document-leading". §10.1 lists the YAML comment positions that
are supported but states no association rule of its own, so §20 governs YAML.

The same top-of-file comment is therefore classified two ways depending on the format it was read
from, and the implementation follows each clause as it is written. **verified**

A plain round trip does not show it, because a document-leading comment and a leading comment of the
first entry emit in the same place. It becomes observable once the owning entry stops being emitted:
an ignore mask over the first entry takes that entry's leading comment with it, while a
document-leading comment survives to the output.

Both readings are the plain sense of their own clause, so there is nothing the implementation can
settle by itself. The candidate resolutions are to give §10.1 an association rule matching §8.5, or
to restate §20's trichotomy as format-independent and amend §8.5 to match. Choosing between them
with no use to point at would be guessing, so this is recorded and left for the preview to settle.
Tracked as [#63](https://github.com/stop-cran/namespace2xml/issues/63).

### 1.17 An unpaired surrogate cannot reach an output, and `-v` loses one silently

§16.9 says that without `EscapeNonAscii` "non-ASCII text is emitted as literal UTF-8". UTF-8 has no
encoding for an unpaired surrogate, so that sentence cannot be obeyed for one, and the JSON writer
escapes such a code unit as `\uXXXX` whatever the flag says rather than emit a silent U+FFFD.

That branch is unreachable. Every route into the model was tried. **verified**

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
practice. Tracked as [#64](https://github.com/stop-cran/namespace2xml/issues/64), which asks §16.9
for the qualifier it is missing rather than asking the writer to change — the escape this build
emits is the right behaviour, it is simply an implementation decision on a default path that the
contract does not state.

### 1.18 *(resolved)* `NewLineOnAttributes` and the first attribute

§16.9 said `NewLineOnAttributes` "places each attribute after the first on its own line", and this
build placed *every* attribute on its own line, including the first.
[#53](https://github.com/stop-cran/namespace2xml/issues/53) put the choice to review, and the clause
moved: §16.9 now says every attribute, including the first. The corpus gap that let the divergence
survive is closed by `conformance/xml-newline-on-attributes`.

This entry is kept rather than deleted because the reasoning is the point. The decision went to the
clause and not to the code, which is the direction rule 2.1 exists to make expensive, and it was
taken for a reason narrower than "the code was already like that": the flag is cosmetic and opt-in,
and obeying the old wording meant hand-writing start tags and taking attribute escaping, namespace
declaration placement and mixed content back from `XmlWriter` — a much larger surface, at risk of
being wrong in ways nothing else in the contract would catch. Where a clause has teeth, the code
moves instead.

### 1.19 `root` on a bare-scalar INI output root has two readings

§16.3 says the output selector prefix is removed and "the original selector name is not retained
unless it is also present in the `root` value". §19.6 says that when the selected output root is a
bare scalar, "INI retains the final concrete selector part as a global key" and that "`root` may
place it in a section", with root parts being "section-path parts rather than part of the key text".

For `k=1` with `k.output=ini` and `k.root=s`, the first clause gives `s=1` and the second gives
`[s]` with `k=1`. This build emits `s=1`. **verified**

It honours §19.6's retention only while `root` is absent — `k=1` alone emits `k=1` — and switches to
§16.3's replacement as soon as `root` appears, which also makes the bare-scalar case behave
differently from the ordinary one directly beside it: `a.k=1` with `a.root=x.y` emits `[x:y]` and
`k=1`, keeping the key and treating root as a section path.

The one fixture that combines INI with `root`, `ini-projection-and-section-order`, pins only the
ordinary case, so nothing currently pins this one. Tracked as
[#54](https://github.com/stop-cran/namespace2xml/issues/54); `docs/format-ini.md` documents the
question rather than either answer.

### 1.20 `WARN009` binds by existence, where §22 says effectiveness

§22 gives the `WARN009` condition as a directive that "binds to no effective output or path"; §15.2
gives it as one that "binds to no concrete output instance". §16.1 keeps an `output=ignore` instance
in existence so a later declaration can restore it, so a directive landing on an ignored instance
binds under §15.2 and does not bind under §22.

This build emits no warning: the directive binds to the instance that exists. **verified**

The behaviour predates the entry and was preserved rather than chosen. Both existing `WARN009`
fixtures concern selectors that bind to nothing at all, which the two readings agree about, so
nothing pins this case either way. Tracked as
[#55](https://github.com/stop-cran/namespace2xml/issues/55).

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
and `WILDCARD002` across the four wildcard-bound cases.

### 2.1 The INI dialect is not tested against any third-party parser

Acceptance item 28 asks for "the documented INI dialect against representative parsers", and
Section 19.6 names the dialect `PortableIni1` and requires that "conformance tests must cover the
representative parsers named by the implementation's compatibility documentation."

Neither half exists. No document in this repository names a representative parser, and nothing in
the corpus or the test suite feeds an emitted `.ini` file to one. `IniSerializerTests` and the
`.ini`-producing fixtures compare the serializer's own bytes against expected bytes, which
establishes that the output is stable and matches the specification's description of the dialect —
but not that any real parser reads it back as the same key-value model. Those are different
claims, and only the second is what item 28 asks for.

This cannot be closed by a fixture. The corpus compares files; establishing round-trip fidelity
needs harness machinery that invokes an external parser, and a decision about which parsers are
normative — plausibly Python's `configparser`, `ini4j`, and whatever the Windows profile API
accepts, each under both `QuoteValues` settings and both `EscapeMultiline` settings, since those
options are exactly where dialects disagree. Choosing that list is a specification question, not
an implementation one, and §19.6 makes it normative by referring to it, so it has to be written
down before any harness is built. Tracked as
[#67](https://github.com/stop-cran/namespace2xml/issues/67).

Until then, treat `PortableIni1` output as specified-and-self-consistent rather than as verified
interoperable. Item 28 stays `pending` and is the largest single gap in the corpus.

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
