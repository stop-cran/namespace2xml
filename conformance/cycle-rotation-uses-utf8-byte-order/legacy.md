# Legacy differential

- namespace2xml 2.4.0: **agrees**, but for an unrelated reason: only the tree and the exit code are
  compared, and the byte-order distinction this case pins is invisible outside the diagnostic
  stream.
- Contract: Section 22 — "Rotate the ring so its lexicographically smallest canonical path under
  unsigned UTF-8 byte order is first"; Section 13.1 `REFERENCE003`; Section 26 item 5.
- Legacy observation: the baseline exits `1` and produces no output tree, matching the case's
  expected exit code and empty tree. Standard error beyond the banner is empty. Whatever 2.4.0 did
  with the two-node reference ring is not visible on either channel this harness compares.
- Clean behavior: the rotation is chosen under unsigned UTF-8 byte order, which is the order the
  specification names everywhere it orders text. The report is located at line 2 column 6 with
  path `m.a豈`.
- Why the observable agreement is not compatibility evidence: this fixture's whole substance is
  the `path`, `line`, and `column` of one diagnostic, and the harness does not compare a legacy
  run's diagnostics against `expected-diagnostics.json` — it compares only the tree and the exit
  code. C#'s native string comparison is ordinal over UTF-16 code units and picks a different
  rotation on this ring, so 2.4.0's report — if it produces one at all — points at line 1, column
  6, and path `m.a` + `U+10000`. Both runs exit `1` with an empty tree, so the observable evidence
  is silent about which rotation was chosen. That is the classic Appendix C.6 case where the
  verdict says "the observable result is the same" and the prose says "for reasons the observable
  cannot separate".

## Not asserted

- Column numbering in the presence of an astral scalar *before* the column point. Both column
  positions here are preceded only by BMP scalars, so the case does not depend on whether columns
  count scalars or UTF-16 units.