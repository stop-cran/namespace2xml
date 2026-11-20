# Publication report: historical proposal for #116

**Status: historical r118 proposal, superseded by [Section 21.5](specification.md#spec-21-5)
and the distributed [version 1 schema](publication-report.schema.json).** The
D1-D7 candidate was approved and implemented in the 3.1 candidate. The
specification and schema, not this historical design, govern the interface.

This record covers [#116](https://github.com/stop-cran/namespace2xml/issues/116)
against product `3.0.0`, contract `r118+33da7b7912fc`. It incorporates the
[user-validated evidence](https://github.com/stop-cran/namespace2xml/issues/116#issuecomment-7033309727)
as it stood before approval.

**Verified-in-session (document inspection):** the full issue and comment thread,
[#26](https://github.com/stop-cran/namespace2xml/issues/26), `CONTRIBUTING.md`,
`KNOWN-LIMITS.md`, and specification Sections 6, 15, 17.5, 21, 22 and 24 were read.
At the time, the issue called for ratification before implementation. The released
binary experiments in the comment were not repeated during that review. The
proposal and acceptance cases below are retained as historical design context,
not as claims about current test results.

## What is already constrained

| Requirement | Authority and consequence |
|---|---|
| Record internal planning and staging facts | #116 requires immutable planner/staging records, never a directory rescan to rediscover outputs. Comparing one planned destination with its staged bytes is distinct from rediscovering outputs. |
| Required output facts | #116 requires destination, effective format, concrete instance identity and declaration, selected root/prefix, staged byte length and SHA-256, prior existence, byte difference, and publication status with applicable failure code or unattempted reason. |
| Required envelope facts | #116 requires schema version, product version, contract revision/hash, exit result, and whether publication occurred. The later normative amendment specifies their representation. |
| Four output statuses only | #116 fixes `published`, `unchanged`, `failed`, `unattempted`. No pruning, deletion behavior, or fifth output status is requested. |
| Canonical diagnostic channel | [Section 6.4.3](specification.md#spec-6-4-3) requires exactly one canonical JSON array on stderr, including late publication diagnostics. Neither report data nor a second JSON document may be appended there. |
| Warning refusal is explicit state | [Section 21.2](specification.md#spec-21-2) distinguishes policy refusal from successful zero-output execution. It forbids configuration publication sink calls and synthetic policy diagnostics. |
| Serialization precedes publication | [Section 15.1](specification.md#spec-15-1) and Section 21.2 require complete immutable serialized buffers and global validation before configuration publication. |
| Partial failure is not rollback | [Section 21.3](specification.md#spec-21-3) leaves completed files updated, permits the failing destination to be partial, and leaves later destinations untouched. A report cannot call a failed or incomplete action successfully published. |
| Existing writes are not silent | [Section 21.4](specification.md#spec-21-4) provides information-level replacement logging; Section 6.2 provides trace publication details. #116 adds structured facts, not replacement operational logging. |
| Equal bytes are not a metadata guarantee | The issue comment measured equal-byte replacement with a changed mtime. [Section 24](specification.md#spec-24) guarantees configuration bytes, not preservation of filesystem metadata. |
| Streaming is separate work | #26 owns stdin/stdout transformation. Its proposed stdout content channel cannot also carry a report payload. Implementing stdout transformation is not part of #116. |

## Decisions approved

These were the proposal's observable contract choices:

| ID | Approved answer |
|---|---|
| D1 | Explicit `--report-file`, relative to the working directory; existing parent required; no channel multiplexing; secure path and collision checks. |
| D2 | `unchanged` means a completed equal-byte rewrite, not a skipped write or preserved mtime. |
| D3 | Cover semantic failures and refused complete plans; explicitly distinguish policy refusal from successful zero-output execution and unknown observations from false. |
| D4 | Prepare/truncate the separate report file before configuration publication; write final outcomes afterwards; `REPORT001` invalidates an incomplete report and sets exit `1`, without rollback. |
| D5 | One entry per final destination, with ordered fold contribution history and concrete instance identities. |
| D6 | Closed schema version 1, exact canonical bytes and digest definitions; new fields require a new schema version. |
| D7 | Compare each destination immediately before its write; preserve unknown facts and distinguish completed publication from possible partial side effects. |

## Historical acceptance cases

These cases preserve the proposal's acceptance intent. The specification and
registered evidence are authoritative for the implemented contract.

| Case | Required discriminating observation | Decisions |
|---|---|---|
| Successful zero destinations | Use `a.output=ignore`. Exit `0` and no configuration publication, distinguished explicitly from policy refusal. | D1, D3, D6 |
| First single output | Input `a.x=1`, scheme `a.output=json`: configuration content is `{\n  "x": 1\n}\n`, 13 UTF-8 bytes. The comment records SHA-256 `c5e1c113d5c75240d109ea470244fa988c5d9970f26bdbbb885876ae1da9d2d7`; this length/digest was independently verified from the hand-authored bytes, not tool output. Report provenance, absent destination, staged length/hash and completed action. | D1, D5-D7 |
| Identical rerun | Reuse that destination. Under this candidate, require `unchanged` only after a completed equal-byte rewrite, not a skipped write. Assert sink operations directly; do not infer them from timestamp resolution or promise preserved mtime. | D2, D7 |
| Different existing content | Prepopulate the destination with different bytes. Require truthful prior-existence and difference facts and the status of the actual completed write. | D2, D7 |
| Multiple outputs and ordering | Choose declaration order that differs from filename order. Cover wildcard matches, same-format folding and cross-format replacement, retaining the Section 21.3 publication order and unambiguous provenance. | D5, D6 |
| Policy refusal | Add an absent input and `--fail-on-warning`. Exit `1`, no configuration sink calls, preserve pre-existing destination bytes. Distinguish from zero-output success without changing the diagnostic array. Exercise `--verbosity none` yielding `[]` plus LF. | D3, D7 |
| Planning failure | Use a blocking path or serialization error with an otherwise valid second output. No configuration destination changes; any report must not fabricate a complete staged plan or comparison facts. | D3, D6 |
| Failure before the first completed destination | Inject open, short-write, flush and close failures separately. Record the failing action truthfully, including the possibility of partial bytes, and later actions as unattempted. | D4, D7 |
| Mid-publication failure | Complete A, fail B, never attempt C. A remains completed, B is failed and may be partial, C is unattempted; no all-or-nothing result or post-failure rescan may erase that distinction. | D4-D7 |
| Report failure before configuration side effects | Fail report preparation. Under this candidate, require `REPORT001`, exit `1`, and no configuration publication; do not promise preservation of an old report already truncated during preparation. | D1, D4 |
| Report failure after configuration side effects | Fail report write/flush after a completed configuration write and after a configuration `PATH002`. Preserve actual configuration outcomes and canonical diagnostic framing; never claim an incomplete report is authoritative. | D4 |
| Destination aliases and topology | Exercise report/configuration collisions, a report path that is a parent of a configuration destination, platform-portable aliases and unsafe links according to the ratified ownership rule. | D1 |
| Schema rejection and compatibility | Reject unknown members, out-of-range numbers, missing required facts, invalid status/reason combinations and unsupported versions according to the selected schema/version policy. | D3, D6, D7 |
| Channel compatibility | With JSON diagnostics at trace verbosity, stderr remains one canonical array. No report bytes enter configuration files. Document #26's future channel conflict without implementing stdout transformation. | D1, D4 |

Where Appendix C cannot portably express prior filesystem state, timestamp or
write/flush failure observations, use exact executable gates under Appendix C.5
rather than claiming a portable fixture proves them. Apply CONTRIBUTING C7:
watch each new test reject a compiling defect, confirm the expected tests ran,
restore source and rebuild. Coverage is tracked in the assertion manifest, not this record.

## Consumer boundary

**Verified-in-session (consumer inspection):**
`ansible/plugins/module_utils/render_node.py` renders into scratch, enumerates
produced files with `discover(scratch)`, then calls `plan(scratch, dest, produced)`
to compare scratch bytes with the **real Ansible destination**. That comparison
supports check-mode and idempotence; the CLI's observations concern scratch
destinations, not the real target. A report can replace scratch enumeration and
effective-format discovery, but cannot replace those distinct real-destination
comparisons, file-attribute handling, or before/after diff generation.

## Original candidate design

The following retains the proposal's prospective wording. The
[policy-refusal example](publication-report-policy-refusal.example.json) is
historical; the [schema](publication-report.schema.json) is the implemented,
distributed version. In particular, the later D7 reconciliation below permits
the actual `PATH001` or `PATH002` configuration diagnostic.

### D1: explicit report file, unchanged standard channels

Add the optional single-valued long option `--report-file <path>`, including the
Section 6.2 inline form and later-occurrence-wins rule. Empty values are `CLI001`.
Without it, behavior is byte-for-byte unchanged. Help/version ignore it under
Section 6.1 and produce no report. Invalid CLI produces no report, even when a
valid report option was scanned earlier: unlike diagnostic format, it has no
pre-scan that authorizes filesystem writes. A literal `-` is a filename.

Resolve the explicit report path against the invocation's working directory,
not the configuration output root. Its parent must already exist; do not create
report directories. Before opening it, establish no-follow containment beneath
that parent using the same supported secure primitives as configuration
publication. Refuse a link/reparse-point report file or parent chain. Hard-link
aliases remain outside the guarantee, consistently with the current known limit.
Concurrent mutation of either publication root or report path is unsupported.

Reject report identity equal to any input/scheme file or any final configuration
destination. Also reject report/configuration file-directory prefix conflicts,
including the configured output root itself or a report file that would be its
ancestor. Compare absolute normalized path segment vectors using the existing
host path identity rules and, conservatively on every OS, ASCII-case-folded
segment vectors as well. This check is an additional collision guard, not a way
to relax Section 21.1 or to re-encode the caller's report path. Inspect existing
entries through no-follow operations; do not scan directories for outputs.

Report data never goes to stdout or stderr. #26 may later own stdout content;
no streaming option is implemented here. Canonical diagnostic stderr retains its
current one-array framing, ordering and verbosity rules.

### D2 and D7: equality is separate from completed publication

Preserve today's writes. Do not introduce equal-byte skipping or an mtime
guarantee. Every configuration destination is still created/truncated, written,
flushed and closed in Section 21.3 order, including an equal-byte destination.

After preparing its parent directories, compare a planned destination with its
immutable staged buffer through confined no-follow reads immediately before
creating/truncating the destination.
An absent file has `existedBefore: false` and `bytesDiffered: true`, including
when the staged buffer is empty. A successful comparison of an existing file
sets `existedBefore: true` and `bytesDiffered` from exact byte equality, not merely
hash equality. An unreadable file or failed comparison is `PATH002` and aborts
publication before opening that destination for writing.

There are exactly four per-destination statuses:

| Status | Meaning |
|---|---|
| `published` | Create/truncate, write, flush and close completed, and the compared bytes differed or the file was absent. |
| `unchanged` | Those same publication operations completed, but compared bytes were equal. This does **not** mean skipped write, preserved mtime, or no side effects. |
| `failed` | This destination's comparison, parent-directory preparation, open, write, flush or close failed. It may be partial. `failureCode` is the actual publication diagnostic: `PATH001` for a confinement refusal, or `PATH002` for an I/O failure. |
| `unattempted` | No publication work for this destination began. `unattemptedReason` is `warningPolicy` or `previousPublicationFailure`. |

`failureCode` and `unattemptedReason` are otherwise null. Unobserved existence or
equality is null, never false. Retain observations made before a failed action;
do not inspect later destinations after failure. Where preparation fails before
comparison, both observation fields remain null.

This reconciles D7 with the existing Section 21.1 confinement contract: the
original candidate named only `PATH002`, but confinement refusals remain
`PATH001`. If a nonempty plan cannot establish its output root, attribute the
failure to the first destination requiring that root and leave later destinations
unattempted. This is report bookkeeping, not a second diagnostic. A zero-output
plan does not establish a root merely to create a failed entry.

`publicationOccurred` means at least one destination completed (`published` or
`unchanged`), **not** that at least one byte differs. `sideEffectsPossible` is set
immediately before the first potentially mutating configuration sink call,
including directory creation, and is never reset. A failure during the first
write can therefore yield `publicationOccurred: false` with
`sideEffectsPossible: true`. Report-file operations do not affect either flag.
The conservative flag does not claim that an unsuccessful mutation changed
anything. No rollback or later filesystem rescan is permitted.

### D3: explicit refusal and incomplete-plan outcomes

After valid operational CLI parsing, attempt a report for semantic failure,
warning refusal, zero-output success, successful publication and publication
failure, subject to report-path validation and report-sink availability.

`planComplete` is true only after all step-19 buffers and the complete validated
configuration path/directory plan exist. Otherwise it is false and `outputs`
is empty; do not leak a successful-looking subset from failed planning. A
complete plan includes every final folded destination, including when policy
refuses it. Report preparation is explicitly outside the configuration
publication sink prohibition; refusal still causes **zero configuration sink
calls**, including comparison reads, root/directory creation and destination opens.

The envelope `outcome` is separate from the four output statuses:

| Outcome | `exitCode` | Plan and publication facts |
|---|---:|---|
| `success` | 0 | Complete plan; zero destinations or all completed. Warnings are allowed when policy does not refuse. |
| `validationFailed` | 1 | Incomplete plan, no output entries, neither publication flag set. |
| `warningPolicyRefused` | 1 | Complete plan; every output unattempted for `warningPolicy`, observations null, neither publication flag set. |
| `publicationFailed` | 1 | Complete plan; exactly one failed destination, preceded only by completed destinations and followed only by unattempted destinations for `previousPublicationFailure`. |

An earlier blocking error takes `validationFailed`, not policy refusal. The
warning decision reads the complete unfiltered stream through serialization,
irrespective of verbosity, and never synthesizes a policy diagnostic.

### D4: report lifecycle and explicit failures

The report is **not** part of the configuration transaction or output-byte budget.
Do not use temporary files or promise atomic replacement. No configuration
rollback is added.

After semantic processing stops, validate the report path and its collision
rules against the complete final plan when one exists. If it is valid, open or
create/truncate the report **before any configuration publication**, retaining
the handle until final reporting. This preparation failure refuses configuration
publication. An existing report may already have been truncated on a preparation
failure; preservation of the old report is not promised.

If configuration publication is allowed, perform it and retain actual outcomes.
Serialize the final report to the prepared handle, then flush and close it.
Staged lengths and hashes always describe the intended configuration buffers,
never a partial destination's current bytes. The report itself is not an output
entry and never hashes itself.

Propose one new diagnostic, `REPORT001`, severity error, once per invocation,
for invalid/unsafe/colliding report paths or report open/write/flush/close
failure. It carries phase `publication`, the new report-lifecycle clause's anchor,
and `message` only: `destination` remains reserved for canonical configuration
paths. It sorts after configuration diagnostics carrying destinations under
Section 24 group 3. Report-only permission/path failures do not reuse `PATH002`'s
once-per-failing-configuration-destination slot. Malformed option syntax remains
`CLI001`. The normative specification and generated registry now contain the mapping.

A report failure always makes the process exit `1`, including after completed
configuration side effects. Do not attempt to rewrite the report to report its
own failure. It may be absent, stale, truncated, partial, or syntactically valid
but not durably completed, and is **not authoritative** in that case. Its
prospective `exitCode` is the overall exit code conditional on successful report
completion, not a promise that its own flush will succeed. `REPORT001` records
that invalidation in the existing diagnostic stream when visible.

Consumers needing authoritative reports for failed invocations must request
JSON diagnostics at verbosity `error` or more verbose, retain the process exit
code, and reject a report whenever `REPORT001` occurs, schema/semantic validation
fails, or its `exitCode` differs from the process result. With `critical` or
`none`, failure diagnostics can be hidden: after exit `1` the report must be
treated as unverified, never as proof of publication. A diagnostic-sink failure
retains Section 6.4.3's existing semantics; failed-run report authority cannot be
established when its required diagnostic stream was not received. This explicit
limitation avoids pretending that a file can attest its own failed close.

### D5: one entry per final destination, ordered contribution history

`outputs` has one entry per final folded destination, in Section 21.3 publication
order, not filename order. Its `format` is the final effective format, and
`byteLength`/`sha256` describe that destination's complete immutable buffer.

Each entry's nonempty `contributions` array records all transformed contributions
entering that destination's Section 17.5 fold in fold-key order, including those
later replaced. It is a history, not a claim that every contribution's payload
survived. Each contribution records:

- `source`: scheme path token with backslashes replaced by `/`, never absolutized;
- `sourceIndex`: one-based scheme-source occurrence index in concatenated `-s`
  token order, so repeated filenames remain distinct;
- `line`: the declaration's one-based physical line;
- `declaration`: canonical original output directive path, including `.output`
  (or `output` for the empty selector), without `=value`;
- `formatIndex`: zero-based position in the winning output value's format list;
- `matchIndex`: zero-based position among that declaration's concrete selector
  matches in Section 12.4 order; a literal declaration has index zero;
- `selector`: canonical concrete selector spelling with the Section 19.1 name
  encoding and `.` separator, empty for the root selector;
- `root`: canonical effective Section 16.3 wrapper path in the same encoding,
  empty when no wrapper is configured;
- `format`: this contribution's lowercase effective output format.

The tuple (`sourceIndex`, `line`, `declaration`, `formatIndex`, `matchIndex`,
`selector`) is its concrete instance identity. The selector is also the removed
selected prefix; `root` describes the wrapper subsequently applied. A final
destination need not have one unique selector or wrapper. Suppressed instances
and superseded declarations that produce no contribution are not report entries.

### D6: closed schema, exact bytes and compatibility

Schema version is the integer `1`. All object shapes are closed and all listed
members are required, using explicit null only where the schema permits it.
No additive field is compatible with version 1: any shape change gets a new
schema version; consumers reject unsupported versions. Product release versions
and contract revisions remain independent identities.

Integer facts are in signed 64-bit nonnegative range, with `sourceIndex` and
`line` positive. Hashes are lowercase 64-digit SHA-256. `contract.hash` is the
full SHA-256 of UTF-8 bytes formed by `specificationSha256`, one LF, then
`registrySha256`, with no final LF, matching Section 22's bundle digest
construction. Its first twelve digits equal the suffix of `contract.revision`.
The other two digest fields name the exact distributed artifacts; the schema
does not substitute regex matching for verifying these relationships.

Report encoding is UTF-8 without BOM, one compact JSON object followed by exactly
one LF, with Section 6.4.3 string escaping and unsigned ASCII decimal integers.
Members appear in the order listed by each object's schema `required` array.
JSON Schema does not enforce member order, escaping, termination, canonical
paths, fold ordering, identity uniqueness, digest correctness, or relationships
between separate entries; the normative specification provides additional checks.

Report bytes are identical only when invocation identity, staged buffers,
contribution history, destination observations and sink outcomes agree.
Pre-existing files and external storage failures intentionally affect reports,
not the Section 24 deterministic configuration-byte guarantee. Omit timestamps,
durations, absolute resolved paths, random run identifiers and localized prose.

### Concrete policy-refusal example

Use the comment's inputs `a.x=1` and `a.output=json`, with the directive on line
1 of `scheme.properties`, and leave `missing.properties` absent:

```text
namespace2xml -i input.properties missing.properties -s scheme.properties -o out --report-file report.json --diagnostics-format json --fail-on-warning --verbosity none
```

In the candidate, `report.json`'s parent is the existing working directory. With
no report-path failure, exit is `1`, stdout is empty and stderr is exactly `[]`
plus LF. `out` remains absent if absent initially. The report has a complete
13-byte staged `a.json` plan but no configuration destination comparison:

```json
{
  "outcome": "warningPolicyRefused",
  "planComplete": true,
  "publicationOccurred": false,
  "sideEffectsPossible": false,
  "outputs": [{
    "destination": "a.json",
    "existedBefore": null,
    "bytesDiffered": null,
    "status": "unattempted",
    "failureCode": null,
    "unattemptedReason": "warningPolicy"
  }]
}
```

This excerpt omits required identity/staging members for readability; the linked
example file is complete. Its product/contract identity deliberately uses the
reviewed 3.0.0 baseline for an illustrative shape, **not** a claim that this
baseline implements reports; an actual report names its own amended bundle.
At verbosity `none`, a failed invocation's report cannot establish its own
authority if the report sink also fails; use `--verbosity error` for a consumer
that needs the `REPORT001` invalidation signal.

With the same inputs and `a.output=ignore`, but without `--fail-on-warning`,
successful zero-output execution instead reports `outcome: success`,
`exitCode: 0`, `planComplete: true`, an empty `outputs` array and both publication
flags false. Zero publications and an empty visible diagnostic array therefore
never stand in for the explicit policy outcome.

### Schema checks

Run the schema checks with
`pwsh -NoProfile -File tools\test-publication-report-candidate.ps1`. They exercise
valid outcome shapes, reject malformed reports, and prove that allowing a fifth
output status breaks its rejection check. Runtime behavior needs separate tests.
