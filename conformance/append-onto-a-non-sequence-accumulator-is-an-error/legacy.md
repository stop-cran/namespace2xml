# Legacy differential

- namespace2xml 2.4.0: **unclassified**. Legacy had no `merge` directive, so it makes no claim here.
- Contract: Section 16.10 `append` — "other non-sequence use is an error"; Section 15.1 step 8;
  Section 26 item 25.
- Clean behavior: `append` refuses a path whose accumulator is not a sequence, in either position.
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
