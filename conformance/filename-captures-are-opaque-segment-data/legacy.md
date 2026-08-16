# A capture substituted into `filename` is opaque segment data

Acceptance items 51 and 64. Section 16.2.

## The rule this fixture is about

Section 16.2 gives a seven-step algorithm and orders it deliberately:

> 1. split the scheme-written path only at literally written `/` and `\`;
> 2. substitute captures and selector-derived parts as decoded opaque text inside the segment;

and states the consequence twice, in two different sentences:

> Only separators written literally in the scheme create directory hierarchy; separators
> originating inside captured data are encoded.

> Captured data cannot create traversal because it is encoded.

The ordering is the entire security argument. Splitting the *substituted* text instead — which is
the obvious implementation, because substitution is a string operation and splitting a string is
easy — produces the same answer for every input where no capture contains a separator, and silently
inverts the rule for every input where one does.

`wildcard-filename-substitutes-the-selector-captures` says of this rule that it "is not exercised
here; it belongs with the portable-segment cases". This is that case.

## What the inputs ask for

One selector, `svc.*`, with `filename=out/*.conf`. The `out/` separator is written in the scheme, so
step 1 splits there and a directory is created. The `*` is a capture, so whatever it holds is
substituted *into* the second segment and never splits it.

Five items choose five captures, each aimed at a different step:

| Capture | Step | Expected segment |
|---|---|---|
| `p/q` | 5 | `p%2Fq.conf` |
| `CON` | 4 and 7 | `%5FCON.conf` |
| `x y` | 5 | `x%20y.conf` |
| `.` | 5 | `..conf` |
| `plain` | — | `plain.conf` |

`a.b/c=1` is a legal profile line: `/` is an ordinary character in a name part, which is what makes
the first row reachable at all.

## Reading each row

**`p/q` → `p%2Fq.conf`, not `p/q.conf`.** The separator arrived inside a capture. Step 1 already
happened, against a template that has one `/` in it. Step 5 retains "ASCII letters, digits, `-`,
`_`, and `.`" and encodes "every other UTF-8 byte", so `/` becomes `%2F`. An implementation that
splits after substituting writes `out/p/q.conf` — a directory the scheme never asked for, from data
the tool does not control.

**`CON` → `%5FCON.conf`.** Step 4 records that "its portion before the first dot case-insensitively
equals one of `CON`, …". Step 7 prefixes `%5F`. Note the portion tested is `CON`, not `CON.conf`:
the extension does not rescue a reserved name on Windows, and Section 16.2 requires the same result
"identically on every operating system", so this file is named `%5FCON.conf` on Linux too.

**`x y` → `x%20y.conf`.** A space is not in step 5's retained set. Step 6 is not what encodes this
one — the space is interior, not trailing.

**`.` → `..conf`, and this is the subtle row.** A *statically written* `.` segment is prohibited and
must be rejected. A *captured* one is not written by the scheme, so the prohibition does not reach
it; it is a step 4 condition, and Section 16.2 says unsafe names "are deterministically renamed with
the prefix rather than rejected". But step 4 tests whether *the decoded segment* equals `.`, and the
assembled segment here is `..conf` — the capture is not the whole segment, because `.conf` follows
it. So no condition is recorded, no prefix is added, and step 5 retains both dots. The result is a
file literally named `..conf`, which is an ordinary name and not a dot segment.

The row is included because it is the one an implementation gets wrong in *either* direction: reject
it (treating the capture as written text), or prefix it (testing the capture rather than the
assembled segment). Both are visible here as a changed expected tree.

**`plain` → `plain.conf`.** The control. Without it, an implementation that encoded every capture
unconditionally would still pass.

## What this does not asserts

Whether a capture that constitutes the *entire* segment and equals `.` or `..` is prefixed. It
should be, by the same reading, but `filename=out/*` with capture `..` is a separate case and this
fixture does not smuggle it in.

The complementary rejection — a `..` the scheme wrote itself — is
`a-written-traversal-segment-is-rejected`.

## Legacy differential

- namespace2xml 2.4.0: **differs**. The baseline writes `out/CON.conf`, `out/p/q.conf` and
  `out/x y.conf` where the case expects `out/%5FCON.conf`, `out/p%2Fq.conf` and `out/x%20y.conf`
  (the harness records three `extra` files and three `missing` files). `out/plain.conf` and
  `out/..conf` are produced in both trees.
- Contract: Section 16.2 defines the portable-segment algorithm — split first at literally written
  separators, substitute captures into segments as opaque text, then encode. Section 3 does not
  enumerate this defect; the closest 3.2 clause, "caused by a synthetic internal root leaking
  into user-visible file names", is a different mechanism. The fixture pins Section 16.2 rather
  than a Section 3 preservation or correction.
- Legacy observation: 2.4.0 substitutes captures into the `filename` template as raw text and
  splits the result, which inverts the ordering the specification requires. A `/` inside a
  capture therefore becomes a directory separator (`p/q` yields `p/q.conf` under a directory the
  scheme never asked for), a reserved DOS name is not prefixed (`CON` yields `CON.conf`), and a
  space in a capture is not encoded (`x y` yields `x y.conf`). The two rows that agree do so
  because they exercise no ambiguity: `plain` has no reserved characters, and `..conf` is
  assembled from a `.` capture followed by a literal `.conf`, so the composite segment is not the
  `.` step 4 tests for and no encoding differs.
- Clean behavior: the algorithm splits the template only at literally written `/` or `\`, then
  substitutes each capture into a segment and applies steps 4–7 to that segment. Every unsafe
  outcome that would arise from captured data is encoded rather than materialized as directory
  hierarchy or a reserved name.
- The difference is intentional: captures come from data outside the scheme's control, and
  encoding-after-substitution is the safety guarantee the specification is written for.
