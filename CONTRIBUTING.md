# Contributing to namespace2xml

This file is not a politeness document about commit style. It is the part of the project that
**owns** the implementation: it states how the contract changes, how the code follows, and how what
you learn using the tool gets back into the contract instead of dying in a chat log.

Read it before changing anything, and before reporting anything.

---

## 0. What in this file is binding

| Binding | Reference |
|---|---|
| §2 The ownership chain | §1 Why the project is shaped this way |
| §3 The change protocol, rules C1–C7 | §7 Usage methodology |
| §4 The report forms and routing | §6 Worked examples |
| §5 Channel selection and flood control | §9 Release log rationale |

Binding sections state rules. A pull request or an issue that violates one will be sent back, and in
most cases a CI gate will catch it before a human does. Reference sections explain why, and you are
free to disagree with them — in writing, via the routes in §4.

---

## 1. Why the project is shaped this way

Most tools are written and then described. This one was specified and then written. That inversion
is the whole design, and every rule below follows from it.

The reason is structural rather than stylistic. A mechanism — a program — has no purpose inside
itself. It preserves the invariants it was given only for as long as something outside it keeps
checking. Left alone, it drifts, and the invariants it drops first are the ones on the **failure
path**, because a successful run never exercises them. The code can pass every test anyone happens
to run while having already lost them, and nothing inside it will object.

So this repository keeps the purpose outside the code, in four layers:

| Layer | Here | Role |
|---|---|---|
| Methodology | `docs/usage-methodology.md` | The portable practice. Not yet generalised — see §10. |
| **Contract** | `docs/specification.md` + `spec/diagnostics.registry.json` | States the invariants without describing internals. |
| **Oracle** | `conformance/` | Judges a break *independently of the code's own logic*. |
| Mechanism | `src/` | Does the work. Authoritative about nothing. |

The load-bearing claim is that the oracle is genuinely independent. A test that runs the tool and
compares the result to what the tool produced last time is not an oracle; it is the mechanism
agreeing with itself, wearing the costume of validation. Every fixture's expected output in
`conformance/` is authored from the specification or from the pinned 2.4.0 baseline, by hand. There
is no `--update-snapshots`, there will not be one, and adding one would end the project's ability to
tell correct from customary.

---

## 2. The ownership chain (binding)

```
        methodology            docs/usage-methodology.md
             owns ↓                    ↑ promoted only on a second grounded instance
        SPECIFICATION          docs/specification.md + spec/diagnostics.registry.json
             owns ↓                    ↑ revised by specification-ambiguity reports
        CONFORMANCE CORPUS     conformance/**
             owns ↓                    ↑ extended before any behaviour change
        IMPLEMENTATION         src/**
```

Four consequences, stated as rules rather than sentiments:

**2.1 Code never wins an argument with the specification.** If they disagree, exactly one of two
things happens: the code is fixed, or the specification is amended by an explicit, reviewed change.
There is no third option in which the code stays and the specification is quietly reinterpreted.

**2.2 The specification never wins an argument with reality silently.** If real usage shows the
contract is wrong, that is a *specification-ambiguity report* with its own lifecycle (§4), not a code
fix that diverges. A contract that cannot be told it is wrong stops being a contract and becomes a
description.

**2.3 The diagnostic registry has a bounded authority, not a competing one.**
`docs/specification.md` incorporates exactly one `spec/diagnostics.registry.json` by hash. Within its
domain — the set of codes, their severity, their cardinality rules, and their permitted fields — the
registry is canonical. Everything else, including the conditions under which each code is raised, is
owned by the prose. A mismatch between the two invalidates the build; it is never resolved by
preference.

**2.4 The binary declares which contract it implements.** `--version` prints a `contract-bundle`
revision covering both the specification and the incorporated registry. CI fails if either changed
without that revision moving. This is the mechanical form of the leash: drift becomes impossible to
commit accidentally, rather than merely discouraged.

---

## 3. The change protocol (binding)

Seven rules. A rule with no enforcer is decoration, so each one names its enforcer — and, where the
enforcer does not exist yet, says so in the same breath. [KNOWN-LIMITS.md §4](KNOWN-LIMITS.md#4-documented-but-not-yet-enforced)
is the single list of which gates are live; this section must never contradict it.

### C1 — Requirement and fixture first

No behaviour change without named acceptance items and fixture evidence that **fails before and
passes after**. The fixture may already exist or may land in the same pull request. It is authored
from the specification, never captured from the tool's output.

*Enforced by:* `TraceabilityTests` and the assertion-manifest gate, both running today. **Partly
manual:** CI verifies that every named acceptance item exists and that every `required` item has
fixture coverage, but nothing yet reads the acceptance items out of the pull request body, so the
"fails before" half is reviewer-verified. Tracked in KNOWN-LIMITS §4.

A pull request labelled `refactor-only` is exempt from adding a fixture, and in exchange must leave
the entire observable corpus byte-identical and carry a maintainer approval. If a refactor changes
one byte of output, it was not a refactor.

### C2 — Cite the specification

Every pull request and every issue names the sections it relies on (`§16.6`, `Appendix B`). Every
diagnostic occurrence carries the anchor it actually enforces, not a nearby one.

*Enforced by:* registry and schema tests constrain the permitted anchors, and fixture comparison
checks the occurrence-level anchor whenever a fixture pins one. **Not machine-checked:** the
citation itself is a required field in the pull request template, and a template field is a prompt,
not a gate. Tracked in KNOWN-LIMITS §4.

### C3 — Specification decision precedes implementation acceptance

A normative amendment is reviewed and approved before the changed behaviour is accepted. The
specification, registry, assertions, fixtures and implementation **may merge atomically**, so the
default branch never carries a knowingly failing contract. If the work is split, use stacked pull
requests with an explicit expected-failure link; never merge the red layer alone.

*Enforced by:* CI requires the contract-bundle revision to move for every normative diff, and
requires the assertion manifest to be regenerated. A pull request touching both `docs/specification.md`
and `src/` needs maintainer approval — it is not rejected merely for touching both.

### C4 — Traceability stays bidirectional

Every acceptance item has at least one fixture once it is marked `required`, and every fixture cites
at least one acceptance item. Coverage grows by ratchet: an item is promoted to `required` when the
milestone that owns it merges, and from then on it can never lose its fixture.

*Enforced by:* `TraceabilityTests`, running from the first milestone.

Marking an item `required` before it is genuinely covered is worse than leaving it `pending`, because
it converts an honest gap into a false claim. If you are tempted, don't.

### C5 — Determinism is a precondition, not a feature

Any change ships green on the determinism matrix: three operating systems, repeated runs,
byte-identical corpus output. Within a single run the corpus is measured three times over —
under one parser worker and under many, under locales that disagree about the decimal separator,
and under different time zones — into a fresh output root each time. A case whose bytes depend on
any of those is not merely untidy; it has failed.

*Enforced by:* the `determinism` and `cross-os-hash` jobs, both required.

### C6 — Side-effect invariants are re-verified first

After **any** change to publication, path handling, or the validation gate — including a refactor
that merely passes through them — re-run the specification §21 fixtures before anything else.

*Enforced by:* **nothing yet.** The §21 publication fixtures do not exist until publication is
implemented, so there is no job to run them first and no CI job by that name in any workflow. Until
that milestone lands, C6 is a reviewer obligation. Tracked in KNOWN-LIMITS §4. Stating it now is
deliberate: the rule has to precede the code it governs, or the first publication change will be
written without it.

**C6 is the least obvious rule, so here is why it exists.** These three invariants never fire on a
successful run:

- **§21.1**, output-root confinement. Only observable when someone points the tool at a hostile tree.
- **§21.2**, the validation gate: *nothing* is written if *any* output fails validation. Only
  observable when validation fails.
- **§21.3**, publication ordering: deterministic partial-write order. Only observable after an I/O
  error part-way through publication.

All three are security- or data-integrity-relevant, and a refactor that breaks any of them will look
completely healthy. That is the prediction of §1 made concrete, and it is why C6 is a rule rather
than advice.

### C7 — Evidence must be able to fail

A test, a gate or an assertion earns trust only by being shown to reject something. Two limbs:

- **A new test or gate is run against a deliberate defect and observed to go red**, then restored.
  A green suite proves nothing about a rule nobody has watched fail. `HarnessSelfTests` is this
  rule applied to the conformance comparer, and it is why the comparer is trustworthy.
- **An assertion in `conformance/assertions.json` names only what its fixtures can observe.** If no
  fixture output would differ were the claim false, the claim is not covered — regardless of how
  many fixtures cite the item.

*Enforced by:* **nothing machine-checkable, and it probably cannot be.** Both limbs are reviewer
obligations, and the second is the harder one to see. Tracked in KNOWN-LIMITS §4.

**Why the second limb needed writing down.** Acceptance item 15 claimed for months that "no external
or network resource is retrieved". Every fixture input declared a DTD, which is refused before any
identifier is looked at — so the corpus proved only that a DTD is refused, and an implementation
that retrieved a resource and *then* refused would have passed unchanged. The item was cited, the
fixtures were green, the traceability gate was satisfied, and the claim was untested. Nothing in
C1–C6 catches that, because every one of them is about whether evidence *exists*, not about whether
the evidence could have come out differently.

The check is a single question, and it is worth asking out loud for each assertion you write:
**if this claim were false, which byte of which fixture would change?** If the answer is "none", you
have written documentation, not an assertion.

Corollary for oracles: an arm of a comparer that no input can reach is not defence in depth, it is
unexercised code in the one component that must not be wrong. Delete it and record which mechanism
actually closes the hole.

---

## 4. The feedback channel (binding)

This project actively wants feedback from automated agents, and is built to absorb it. That is only
survivable if reports are routed correctly and are honest about what was verified.

### 4.1 Four destinations, and why routing matters

Ask exactly one question: **what would have to change so this never surprises anyone again?**

| Answer | Destination | Form | Lifecycle |
|---|---|---|---|
| The code should have matched the specification | **Bug** | `bug_report.yml` | Fixture plus fix. Specification untouched. |
| The specification does not say, says two things, or says something surprising | **Specification ambiguity** | `spec_ambiguity.yml` | Specification decision, then assertion, fixture and code. Slower, reviewed harder. |
| Both are right; I could not find out how to do this | **Usage gap** | `usage_gap.yml` | Documentation, help text, examples. |
| The tool cannot express this at all | **Feature request** | `feature_request.yml` | Candidate for a future specification section. |

Misrouting is the main failure mode, and it is not a clerical problem. A specification ambiguity
filed as a bug gets "fixed" in code, and the contract silently rots — which is precisely the drift
this whole apparatus exists to prevent.

In a project whose contract is a single very long file, the **usage gap** is the most common finding
and the least reported, because it does not feel like a defect. It is. Report it.

### 4.2 The report form

| Field | Rule |
|---|---|
| **Version** | Exact `--version` output, including the `contract-bundle` revision. Non-negotiable: a report against an unknown revision is unactionable. |
| **Observed** | Verbatim output. Redact secrets. If reconstructed from memory, mark it `(reconstructed)` and lower the confidence. |
| **Expected** | The reporter's own words. If vague, ask **one** follow-up. **If you cannot obtain a concrete expectation, stop. Do not invent one.** |
| **Repro** | Minimal inputs, scheme and arguments. If you can express it in the Appendix C fixture layout, it is a pull request, not an issue — and it is worth far more. |
| **Specification anchor** | Section number plus a **verbatim quote** long enough to be unique. Never a bare line number: line numbers drift the moment any other change lands. Never anchor against text you have not read in this session. |
| **Classification** | One of the four routes above. |
| **Confidence** | Per claim, not per report: `verified-in-session` (you ran it) or `proposed-but-untested` (you reasoned it). |
| **Duplicate check** | `gh issue list --search "<terms>"`, or `skipped — no gh auth`. |

Two of these fields carry more weight than they look like they do.

**Per-claim confidence** is what makes agent-authored reports safe to accept in volume. An agent
required to mark each claim as observed or reasoned cannot launder a guess into a finding, and a
maintainer can triage by scanning that one column. Without it, a high-throughput feedback channel
becomes a denial-of-service attack on the maintainer, and the rational response is to stop reading —
which closes the loop this project depends on.

**Grep-reconstructible anchors** are what let reports survive the repository moving underneath them.
During the preview the specification will be revised repeatedly; a quoted phrase still finds its
clause afterwards, and `line 1843` does not.

### 4.3 Draft, then submit

Two modes, always in this order:

1. **Draft.** Fill the form. Show it to your human. Say plainly which claims are untested.
2. **Submit.** Only after the human approves.

Never file automatically. An agent that opens issues without approval is not a feedback channel; it
is a leak.

**Stop conditions.** Halt and ask rather than guessing when:

- you cannot obtain a concrete statement of expected behaviour;
- you cannot find a specification anchor, because that usually means the finding is a *specification
  ambiguity* rather than the bug you assumed;
- you cannot reproduce the behaviour — downgrade the confidence, do not fabricate a repro.

### 4.4 Channel selection

The channel is decided by **what tooling is actually available in the session**, not by how technical
the reporter is.

| Condition | Channel |
|---|---|
| `git` and `gh` present, `gh auth status` succeeds, push or fork access | **Direct** — open the issue or pull request from the session |
| Otherwise, a `mailto:` handler is available | **Mediated** — draft an email carrying the form verbatim |
| Neither | **Local handoff** — write the form to a file and ask the human to send it |

The corollary matters: the form must be **self-contained and readable offline**, because in the
mediated path it arrives as an email body with no checkout attached. Do not split it across linked
files.

### 4.5 Flood control

Search before filing. If a close match exists, add to that thread instead of opening a new one. This
applies more here than in most projects, precisely because the project invites machine-generated
feedback from a whole team during the preview. An unbounded feedback channel is not a feature.

---

## 5. Working in this repository

```
dotnet build namespace2xml.slnx
dotnet test  namespace2xml.slnx
```

`net10.0`, `.slnx` solution format, `TreatWarningsAsErrors`. A warning fails the build on purpose:
in a tool whose contract is byte-identical output, the analyser warnings that matter — culture-
sensitive comparison, unchecked arithmetic in limit accounting — are correctness bugs wearing a
warning's clothes.

After editing `docs/specification.md`, regenerate the derived artifacts. Run all five, in this order
— the codes come from the registry, the bundle hashes the registry, and the docs read the bundle:

```
pwsh -NoProfile -File tools/sync-diagnostics-registry.ps1
pwsh -NoProfile -File tools/sync-diagnostic-codes.ps1
pwsh -NoProfile -File tools/sync-contract-bundle.ps1
pwsh -NoProfile -File tools/sync-assertion-manifest.ps1
pwsh -NoProfile -File tools/sync-docs.ps1
```

Adding or changing a conformance fixture needs `sync-assertion-manifest.ps1` and `sync-docs.ps1`,
because coverage and the migration notes are both derived from the corpus.

These are generators, not formatters. `spec/diagnostics.registry.json` is derived from the §22 table,
`spec/diagnostic-stream.schema.json` is extracted verbatim from the §6.4.3 code block, and
`conformance/assertions.json` derives its item text from §26 while preserving the authored
`milestone`, `status` and `assertions` fields. Hand-editing the derived parts will be reverted by
the next run and rejected by CI in between.

Generators are idempotent, and idempotent is not correct. Read the output of one you have changed
rather than re-running it and observing that nothing moved.

### Adding a conformance fixture

A case is a directory under `conformance/`. Full layout is specification Appendix C; the essentials:

| File | Meaning |
|---|---|
| `args.txt` | One argument token per line, verbatim. No shell quoting. |
| `args-diagnostics.txt` | Optional. Token vector for the JSON-diagnostics run, when appending `--diagnostics-format json` to `args.txt` would be wrong. |
| `expected-exit-code.txt` | The expected process exit status. |
| `expected-diagnostics.json` | The expected diagnostic stream. **Absent means no stream is written at all**, which is not the same as an empty array. |
| `requirements.txt` | One acceptance item number per line. Required. |
| `legacy.md` | How 2.4.0 behaved, and whether this case agrees with it or deliberately differs. |

Everything else in the directory is either an input the case reads or a destination it produces. The
harness copies the case into a fresh working directory before every run, so a case can never
accidentally pass by reading an artifact left behind by a previous run.

`legacy.md` is required for a reason. Every intentional divergence from 2.4.0 must be written down at
the moment it is introduced, by the person who knows why. Reconstructing that list at release time
produces a worse migration guide and takes longer.

Before you finish, apply C7 to the case: for each assertion the acceptance items claim, name the
byte that would change if the claim were false. An input that is refused for reason A cannot be
evidence about reason B, however plainly it names B — that is how "no external resource is
retrieved" stayed unexercised behind a DTD refusal that fired first.

### Commits and pull requests

Small, focused commits. The pull request template asks for the acceptance items, the specification
sections, and the fixture evidence; fill it in, because a reviewer cannot verify C1 without it.

---

## 6. Worked examples

Structure is copied far more reliably than description is followed, so here are two complete
reports.

### 6.1 An implementation bug

> **Classification:** Bug — the code should have matched the specification.
>
> **Version**
> ```
> name: namespace2xml
> version: 3.0.0-preview.2
> contract-bundle: r37+2d644be6926e
> ```
>
> **Observed** *(verified-in-session)*
> Running with `--diagnostics-format json` and a scheme that raises two diagnostics, standard error
> contained the JSON array but *also* contained the line `info: Reading scheme file ...` before it.
>
> **Expected** *(verified-in-session — quoted from the contract)*
> Standard error should have contained the JSON array and nothing else.
>
> **Specification anchor**
> §6.4.3: "operational messages are suppressed entirely, so that standard error carries the
> diagnostic stream and nothing else."
>
> **Repro** *(verified-in-session)*
> Added as `conformance/json-diagnostics-suppresses-operational-messages/`, which fails on
> `contract-bundle r37+2d644be6926e`.
>
> **Duplicate check:** `gh issue list --search "diagnostics-format json stderr"` — no matches.

Note what makes this actionable: the anchor is a quote rather than a line number, the repro is
already in fixture layout, and the one claim that is quoted from the contract is marked as such
rather than presented as the reporter's opinion.

### 6.2 A specification ambiguity

> **Classification:** Specification ambiguity — the contract does not determine the outcome.
>
> **Version**
> ```
> name: namespace2xml
> version: 3.0.0-preview.2
> contract-bundle: r37+2d644be6926e
> ```
>
> **Observed** *(verified-in-session)*
> A key marked as ignored by one mechanism and explicitly output by another was emitted. I expected
> it to be suppressed, but on re-reading I cannot show the contract requires either outcome.
>
> **Expected** *(proposed-but-untested)*
> I believe suppression should win, on the principle that an explicit exclusion is a stronger
> statement than an inclusion by default — but this is my reasoning, not something the contract says.
>
> **Specification anchor**
> The two clauses each determine their own behaviour and neither addresses their interaction. I
> searched for "ignore" throughout and found no precedence rule for this combination.
>
> **What I am asking for**
> A precedence rule stated in the specification, whichever way it goes, plus a fixture pinning it. I
> am not asking for a code change, because I cannot show the current behaviour is wrong.
>
> **Duplicate check:** `gh issue list --search "ignore precedence"` — no matches.

Note what makes this a *good* report despite resolving nothing: it separates what was observed from
what was reasoned, it states plainly that the reporter could not find an anchor, and it asks for a
decision rather than for its preferred outcome. A report that had asserted "this is a bug" would have
been fixed in code, and the contract would have stayed silent — and the next person would have hit
the same gap.

---

## 7. Usage methodology

`docs/usage-methodology.md` holds the long form. The judgments that matter most:

- **Reach for this tool when you need many outputs from one overlaid model.** One file, one format,
  no overlays: use a template engine and be happier.
- **Layer by lifetime, not by topic.** Base, then environment, then instance, then secrets, in
  command-line order. The common anti-pattern is encoding the environment in the namespace instead of
  in overlay order; it works until it doesn't, and then it is a rewrite rather than a fix.
- **Scheme files are contracts, not configuration.** Version them, review them, and change them under
  C1 like anything else.
- **Specializing a document you did not write is the strongest case, and the sharpest.** Render it to
  `namespace` output first and read the model, because the paths are frequently not what the source
  looked like — an indented XML file is mixed content, and an XML attribute is `@name`. An override
  written against the obvious path lands beside the value it meant to replace, silently.
- **Pin the behaviour you depend on, and write the expected file by hand.** A handful of small
  input/expected pairs regenerated in CI tells you at upgrade time which assumption moved. Captured
  output records what the tool did and will keep agreeing with it after it starts doing the wrong
  thing.
- **Determinism is a feature to exploit.** Byte-identical output means generated artifacts can be
  committed and diffed in review, which turns configuration drift into a pull request comment. That
  is worth designing a pipeline around.

These are judgments, not rules. Moving them into the code would freeze them, and they are exactly the
part that should stay soft.

---

## 8. Current limits

`KNOWN-LIMITS.md` is the authoritative, dated list of what the tool and this document do not yet
cover, each with the route to report it.

That file exists because a document claiming completeness cannot receive feedback: every gap reads as
user error, and the reporter concludes they are holding it wrong. During the preview the list is
long, and that is correct.

---

## 9. Release log

`CHANGELOG.md` carries one entry per revision of the contract and of this document, recording what
was added, what was **removed**, and **which inbound report caused it**.

Two jobs. It proves the loop actually closed — "report #47 → clarified the §7 layering guidance" is
evidence, while "we value feedback" is not. And it forces removal: a document that only grows becomes
unreadable and then unread, so each revision must say what it dropped.

### 9.1 Publishing

A release is a **tag**, and nothing else. `git tag v<version> && git push origin v<version>` on a
commit whose `<Version>` matches the tag exactly; the workflow refuses the tag otherwise. There is
no manual dispatch and no publish on push, because 2.x published on every push to master and that
is how a half-finished thought reaches other people's build servers.

Publishing is **irreversible**. nuget.org does not allow a version to be deleted or replaced, only
unlisted, so a wrong tag is permanent and the next number is the only remedy. Read the run before
you push the tag, not after.

The moving parts, so nobody has to rediscover them:

| Part | Value | Why it is like that |
|---|---|---|
| Credential | none stored — nuget.org **trusted publishing** | The workflow proves its identity with a GitHub OIDC token and receives a key valid for one hour. There is no long-lived secret to leak, rotate, or forget. `NuGet/login@v1` runs immediately before the push because each token buys exactly one key. |
| Trust policy | owner `stop-cran`, repo `namespace2xml`, workflow `release.yml` | Registered on nuget.org, and it names the **workflow file**. Renaming `release.yml` silently revokes the ability to publish; change the policy first. |
| Environment | `nuget`, restricted to `v3.*` tags | The workflow's own trigger already says tags only; the environment says it again where a workflow edit cannot reach. Add required reviewers here if you want a human gate, and name `nuget` in the nuget.org policy to require it. |
| Order | verify → pack → check contents → install → check `--version` → follow every printed link → attest → exchange token → push | Everything cheap and reversible happens before the one step that is neither. |

The link check exists because `--version` reports a `specification-sha256` and a URL, and those are
only worth printing if the URL serves bytes that hash to that value. It fetches the specification
the tool points at and compares. A release whose contract identity cannot be resolved is worse than
no release, because a report filed against it cannot be acted on.

---

## 10. Promotion restraint

There is a general pattern visible here — a specification owning an implementation through an
independent oracle. It is tempting to name it and promote it to a principle.

Not yet. One success is a singular, and promoting a once-seen pattern to a load-bearing principle is
reaching for a universal by enumeration. It is filed as a candidate in
`docs/usage-methodology.md` and it stays there until a **second, independent** grounding in different
material earns it — a plausible one being an Ansible collection owning this binary: same shape,
entirely different soil.

If you find yourself citing "the namespace2xml methodology" as an authority, that is the failure this
section exists to prevent.

---

## Code of conduct

Participation is governed by [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Security

Do not open a public issue for a vulnerability. See [SECURITY.md](SECURITY.md).
