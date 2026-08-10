# A scheme reference that names nothing is a blocking error

Acceptance item 67. Sections 15.1 and 13.1.

## The rule this fixture is about

Section 13.1 makes missing references blocking errors, and Section 15.1 step 1 is where scheme
references are resolved. Section 15 also fixes what a scheme reference is *able* to name:

> the final qualified-name part identifies a directive

so a name whose last part is not one of Section 15's directives cannot resolve, however much data
exists at that path. A scheme reference reads the scheme, not the inputs.

The two rows here are the two ways that fails, and they are worth separating because an
implementation can easily get one right and the other wrong.

## What the inputs ask for

| Line | Written | Why it cannot resolve |
|---|---|---|
| 3 | `a.filename=${a.delimiter}.conf` | `delimiter` *is* a directive, but no entry declares `a.delimiter` |
| 4 | `b.filename=${b.name}.conf` | `name` is not a directive at all, though `b.name` exists in the input |

Both are `REFERENCE002`, reported in source order, and the run exits 1 having written nothing.

## Reading each row

**Line 3 — a real directive that was never declared.** The reference is well formed and names a
directive Section 15 recognizes. There is simply no entry for it. An implementation that resolves
references by looking up a directive *table* rather than the declared entries would find
`delimiter`'s default and quietly compose `.conf`.

**Line 4 — a name that exists in the input but is not a directive.** `b.name=y` is in
`inputs/profile.txt`, so a reference resolver shared with the profile phase has something to find
here. It must not find it: the scheme phase runs before any input is overlaid, and letting scheme
paths reach input data would make the destination of an output depend on the data being written to
it. The message says so explicitly, and the two messages differ, because the two mistakes have
different fixes — declare the directive, versus stop expecting input data.

**Both are reported.** Section 15.4 has each phase complete every independent check before it
aborts, so a scheme with two broken references reports two errors rather than the first one.

## What this does not assert

Cycles, which are `a-scheme-reference-cycle-is-blocking`, or the depth bound, which
`reference-resolution-crosses-the-depth-bound` covers on the profile side. Nor does it assert a
`column`: the flat scheme reader does not record where within a line a directive's value begins, and
Section 22's diagnostic schema makes the field optional.

## Legacy differential

- namespace2xml 2.4.0: **differs**. The baseline exits 0 and writes `a.properties` and
  `b.properties` — the two default destinations — with the expected content in each.
- Contract: Section 13.1 makes a missing reference a blocking error, and Section 15.1 step 1
  resolves scheme references. Section 3.2 lists the cause directly: legacy behavior "caused by
  discarding a scheme directive whose value could not be resolved, so that an unresolvable or
  cyclic reference silently selects the default destination instead of failing" is not preserved.
- Legacy observation: 2.4.0 resolves what it can and discards what it cannot. An unresolvable
  `filename` is not reported and not retained; the selector simply falls back to the default
  destination, which is the selector's own name with the format's extension. A typo in a reference
  therefore changes where output is written, with nothing on standard error to say so, and the run
  reports success.
- Clean behavior: the reference is reported with the path that could not be resolved and the
  reason, and nothing is written.
- The difference is intentional: silently substituting a different destination for the one the
  scheme asked for is the failure mode this tool is least able to tolerate, because the output
  looks correct and lands in the wrong place.
