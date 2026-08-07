# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 4.4 and 17.1; Section 22 `TYPE002`; Section 24 ordering.
- Legacy observation: a path that received both a mapping and a sequence had no stated rule. Which
  container survived depended on which reader ran, and nothing was reported either way, so a silent
  pick was indistinguishable from a merge.
- Clean behavior: both container projections coexist in the overlay, and a flat output, which can
  spell exactly one container shape at a path, keeps the later contribution under Section 17.1
  precedence and warns that the other is omitted. Both directions appear here, decided only by
  source order: `app.seqwins` is a mapping first and a sequence second, `app.mapwins` the reverse,
  and the surviving shape follows the second contribution in each case. The omitted shape is not
  merged into the survivor, so the mapping child of `seqwins` and the sequence item of `mapwins`
  are simply absent from the output.
- `TYPE002` is a warning, not an error, so the run still publishes.
- The conflicting nodes are deliberately **below** the output root. A diagnostic's `path` is
  expressed in the output instance's own frame, so a conflict at the root itself has an empty path
  and Section 6.4.3 then omits the member entirely -- which would make this case silent about the
  one thing it exists to pin.
- Section 24 puts both occurrences in group 2, ordered by the Section 21.3 destination order. They
  share one destination and one code, so the remaining tie is broken by the qualified path compared
  as unsigned UTF-8 bytes, which reports `mapwins` before `seqwins` even though the scheme and the
  output file both present `seqwins` first.