# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits `0` and writes **no output file at all** — the output
  directory is empty. This case expects exit `70` and no output.
- Contract: Section 10.4. "Extraction is entry-by-entry", and an extracted entry names one scalar.
- Section 10.4 shows a mapping under a wildcard key and says nothing about a sequence under one.
  The shape is genuinely under-determined: a native sequence item takes an implicit ordering value
  from the destination path's Section 5.4 high-water mark, and under a template the destination is
  not known until Section 12.4 expansion, so there is no mark to allocate against at extraction
  time. This build therefore refuses rather than choosing one of the readings.
- Exit `70` is the refusal status, deliberately outside the normative `0` and `1`: the run decides
  no outcome, publishes nothing, and names on standard error the capability it lacks. The message
  identifies the template as `a.*` and offers both remedies — write the branch without a wildcard
  key, or write `\*` for a literal asterisk.
- **The legacy behaviour is worse than the refusal, not better.** Exit `0` with no file is a
  success status for work that was not done: a caller that checks the exit code and then reads its
  output gets a missing-file error at a distance, or silently keeps a stale file from a previous
  run. The refusal is the same absence of output with an honest status attached.
- Once Section 10.4 settles what a template over a sequence extracts to, this fixture's
  `expected/` and `expected-exit-code.txt` change together at that commit. Recorded in
  `KNOWN-LIMITS.md` section 1.2.