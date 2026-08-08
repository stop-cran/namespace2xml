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

Legacy 2.4.0 had no XML input at all, so there is nothing to compare against.
