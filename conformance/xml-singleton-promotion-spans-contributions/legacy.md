# A singleton joins the sequence another contribution makes

Section 11.4: "a singleton `<b>` is addressed as `a.b`; after the merged model contains repeated
`<b>` children, their canonical paths are `a.b.<ordering-value>` and the former singleton path no
longer names a scalar or element. [...] Implementations must not silently retarget `a.b` to the
first repeated child."

The first document repeats `<d>`, which makes `c.d` a Section 5.4 sequence. The second supplies one
`<d>`, which on its own would be the scalar `c.d`. Section 17.4 settles the merged classification --
"if any source contribution contains more than one occurrence, every occurrence of that expanded
name forms one sequence and all occurrences concatenate in source order" -- so the singleton becomes
an item rather than sitting beside the sequence at a path that no longer names anything.

It concatenates rather than patching because it is implicit: Section 8.7 makes that the distinction
and reports `WARN004` so the choice is visible.

The same rule is why two documents that each supply one `<d>` still overlay. The occurrence count is
a property of a single contribution, so nothing about that case is repeated.

Legacy 2.4.0 does read XML input, contrary to an earlier draft of this note, so a comparison is
available.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 11.4's rule that "mixedness and repeated-child classification are properties
  of the merged common-model element", together with the accompanying "implementations must not
  silently retarget `a.b` to the first repeated child" for singleton-to-sequence promotion.
  Section 17.4 fixes the resulting concatenation model. Section 3 does not enumerate this
  specifically.
- Legacy observation: the baseline writes different bytes at `r.properties`. The measurement
  records `content r.properties` at exit `0` with no standard error beyond the banner.
- Clean behavior: the base contribution's two `<d>` children establish `c.d` as a sequence with
  items at ordering values `0` and `1`, and the overlay's singleton `<d>` becomes a third
  sequence item at `2`, producing `r.c.d.0=1`, `r.c.d.1=2`, `r.c.d.2=3`.
- Why the difference is intentional: 2.4.0 has no stated model for reclassifying a per-contribution
  scalar path when another contribution establishes the same path as a sequence, so a singleton
  meeting a repeated-child sequence can only land in either "coincidentally right" or one of the
  two ways Section 11.4 forbids — retargeting `a.b` to the first repeated child, or leaving the
  singleton scalar beside a sequence at a path that no longer names a scalar. Both readings
  would make one node reachable at two addresses. The observation records only that the bytes
  differ from the expected sequence; which of those readings the baseline used is not readable
  from one divergent file.

