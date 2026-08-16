# Legacy differential

- namespace2xml 2.4.0: **differs**. Legacy had no `merge` directive, so the refusal in this case is
  new behavior; the baseline's silent success is a divergence rather than a claim about the same
  rule.
- Contract: Section 3.2 correction against relying on `merge` to control collisions; Section 16.10
  `append` — "other non-sequence use is an error"; Section 15.1 step 8; Section 26 item 25.
- Legacy observation: the baseline exits `0` and writes `a.properties`, so the fixture's empty
  expected tree gains one extra file and the exit code diverges. The measurement records `exit 0
  (expected 1); extra a.properties`, and standard error beyond the banner is empty. `merge=append`
  is not a recognized directive in 2.4.0, so the fold proceeds and the scalar plus the sequence
  contribution reach one output.
- Clean behavior: `append` refuses a path whose accumulator is not a sequence, in either position.
  One `TYPE001` at `§16.10` naming path `a`, and exit code 1.
- Why this case exists: Section 16.10 defines `append` in terms of "the later sequence
  contribution", and an implementation reading only that half validates only the later side. Step 8
  then supplies an innocent-looking excuse for the earlier side — "the earliest or sole contribution
  retains its supplied ordering values" — but that clause is about a path with nothing on it yet,
  not a path holding something unappendable. Falling through to a deep merge instead of reporting
  makes a run that asked to append to a scalar *succeed*, publishing the scalar and the sequence
  coexisting at one path, with exit code 0 and no diagnostic. Nothing downstream distinguishes that
  from a deliberate deep merge.
- How the case proves it: `a=5` arrives first and `a.0=x` second under `a.merge=append`. The later
  contribution is sequence-eligible, so the existing later-side check passes it; only a check on the
  accumulator refuses the merge. One `TYPE001` at `§16.10` naming path `a`, and exit code 1.

## Not asserted

- Which of the two contributions the diagnostic is located at. As Section 26 item 25 already
  records for `merge=error`, the condition is a property of the folded node rather than of either
  contribution, so the diagnostic names the path and no source.
- The same refusal under `filemerge`. Section 16.11 shares the strategy vocabulary but applies at
  step 18; `filemerge-append-*` covers that surface.
