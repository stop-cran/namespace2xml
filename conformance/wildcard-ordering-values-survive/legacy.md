# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 5.4, 8.6, 8.7 and 12; Section 15.1 steps 10 and 11.
- Legacy observation: numeric path parts were ordinary mapping keys, wildcards were resolved in
  enumeration order, and there was no stable identity for a sequence item. Nothing here had an
  answer, because none of the three concepts existed separately.
- Clean behavior: matching uses the **stable ordering value** and flat rendering uses a **fresh
  dense index**, and this case makes the two disagree so that only one of them can be right. A
  wildcard matching `hosts.*` captures `2` and `7`, the supplied ordering values, with the masked
  `5` absent and never renumbering its neighbours, while the rendered indices are `0` and `1`.
  Substituting the capture into the generated value writes the ordering value into the output,
  where a dense index would read `idx-0` and `idx-1`.
- The same case fixes both directions of Section 8.7's "surviving key names" test. Masking
  `hosts.name` removes the nonnumeric child, so the remaining keys are all canonical ordering
  values and `hosts` is inferred as a sequence. Generating `b.blocked.label` adds one, so
  `b.blocked` keeps its supplied numeric keys as ordinary mapping keys and is rendered as a
  mapping: a generated contribution counts for inference exactly as a parsed one does.
- The difference is intentional: Section 3 lists controlled cross-file sequence patching as a
  structural normalization exception, and an ordering value that survived deletion but not wildcard
  matching would make the addressed item depend on which step last touched it.
