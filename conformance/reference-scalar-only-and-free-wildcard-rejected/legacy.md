# Legacy differential

- namespace2xml 2.4.0: **partly matches**. References were one-level in practice, but "not a
  subtree copy" was an implementation property rather than a stated rule, and a reference naming a
  node with children had no defined outcome. Wildcard references had no capture rule at all.
- Contract: Section 13.1 scalar-only resolution and `REFERENCE002`; Section 13.3 explicit capture
  binding and `REFERENCE001`; Section 26 item 46.
- Legacy observation: legacy items 99, 102, 91 and 92 — scalar/null references only, references
  never copying mappings or sequences, the six reference error classes separated, wildcard
  references valid only when captures are explicitly bound, and free wildcard references rejected
  with a stable diagnostic.
- Clean behavior: `${app.subtree}` names a node that has a descendant and no scalar payload, which
  Section 13.1 makes a missing reference rather than a subtree copy or an empty string.
  `${app.*[n]}` binds no capture that the referring name defines, so Section 13.3 refuses it as a
  free wildcard reference.
- Why this case exists: both are constructs a permissive implementation would rather accept than
  reject — one by flattening a subtree, one by treating an unbound capture as a match-anything.
  Refusing them is the contract.
- How the case proves it: the two defects are in one file at lines 2 and 3, so their emission order
  also exercises the Section 24 ordering rule. A tool that ordered by code would emit `REFERENCE001`
  first; the expected stream requires `REFERENCE002` first, because the ordering key is where the
  defect was written, not what it is called.

## Not asserted

- The distinction between `REFERENCE002` and `REFERENCE005` for a node carrying an explicit empty
  container mark. `app.subtree` here has a descendant, which Section 13.1 names outright; the
  empty-mapping case reads differently against the Section 22 registry row and is recorded in
  `KNOWN-LIMITS.md` rather than pinned here.
