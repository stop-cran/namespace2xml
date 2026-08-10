# A cycle among scheme references is a blocking error

Acceptance item 67. Sections 13.1, 15.1 and 22.

## The rule this fixture is about

Section 13.1 states that "reference cycles are blocking errors", and Section 22 fixes how one is
reported:

> a cycle is reported once per canonically distinct reachable cycle

with the canonical form being the least rotation of the member names. Both halves matter. The first
makes the report independent of which member the resolver happened to reach first; the second is
what stops a ring of *n* members producing *n* identical reports.

Section 15.1 step 1 resolves references among scheme entries, so the rule applies there in exactly
the form Section 13.1 states it. Nothing in Section 15 makes the scheme phase a special case.

## What the inputs ask for

Two independent cycles, so that the fixture asserts both the canonical rendering and the fact that
each cycle is reported separately.

| Cycle | Members | Reported at |
|---|---|---|
| two-member ring | `a.filename` → `b.filename` → `a.filename` | line 4, `a.filename` |
| self-reference | `c.filename` → `c.filename` | line 6, `c.filename` |

The run exits 1 having written nothing.

## Reading each row

**The two-member ring is reported once, at `a.filename`.** Both members are cyclic and both would be
discovered by a resolver walking the entries in order, so the naive implementation reports two
errors that describe the same defect. The canonical form is the least rotation of `[a.filename,
b.filename]`, which starts at `a.filename`, so that is the member the report is located at — line 4
— and the chain is rendered from there. Reversing the two declarations in the scheme would not
change the report.

**The self-reference is a cycle of one.** It is the degenerate case, and it is included because the
canonicalization has to handle a single-member rotation without special-casing it, and because a
resolver that detects cycles by comparing the *current* key against the chain rather than by
checking membership will not see this one at all: `c.filename` is the chain's first element and its
own referent.

**Two reports, not one.** The cycles are canonically distinct, so Section 22's "once per canonically
distinct reachable cycle" produces one report each. They are ordered by source, which is the order
the entries were declared in.

## What this does not assert

That a cycle is detected across a chain longer than two, or that a cycle reachable from several
entries is still reported once — `reference-cycle-report-source-order-first` and
`reference-cycle-report-source-order-reversed` cover the canonicalization on the profile side, and
the two resolvers share the Section 22 implementation. This fixture asserts that the scheme phase
applies the rule at all.

A self-reference is not always a cycle: if a later declaration wins the same directive, the
reference reads the winner and resolves. That is a Section 15.2 property and belongs with
`scheme-reference-filename-separators-are-encoded-data`, which pins the winner rule.

## Legacy differential

- namespace2xml 2.4.0: **differs**. The baseline exits 0 and writes `a.properties`,
  `b.properties` and `c.properties` — the three default destinations — with the expected content
  in each.
- Contract: Section 13.1 makes a reference cycle a blocking error. Section 3.2's "silently ignored
  directive" family covers the resulting defect.
- Legacy observation: 2.4.0 has no cycle detection among scheme entries. It resolves what it can
  and discards what it cannot, so all three cyclic `filename` directives are dropped and each
  selector falls back to its default destination. The failure is indistinguishable from the
  missing-reference one — see `a-missing-scheme-reference-is-blocking` — because the baseline's
  recovery is the same in both cases: forget the directive.
- Clean behavior: each canonically distinct cycle is reported once, at its canonically first
  member, with the whole chain rendered so that the author can see what closes the ring.
- The difference is intentional: a cycle means the author asked for something that has no answer,
  and producing output from a silently discarded directive hides that.
