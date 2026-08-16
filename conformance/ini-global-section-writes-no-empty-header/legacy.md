# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 19.6 — "When no global key survives, `GlobalSection` writes nothing: there is no
  empty `[global]` header."
- Legacy observation: 2.4.0 has no `inioutputoptions` directive, ignores it in silence, and exits 0.
  It reaches the same set of sections with the same keys, and diverges only on the blank line it
  writes before each section header, which Section 19.6 forbids.
- The difference is intentional: the emptiness rule is what keeps `GlobalSection` a framing option
  rather than a structural one, so a profile that happens to have no one-part path produces the same
  file whether or not the option is selected.
- This case exists because a mutation that emitted the header unconditionally survived the whole
  corpus and was caught only by a unit test. A rule stated in the specification and pinned nowhere
  in the oracle is a rule the corpus does not enforce.