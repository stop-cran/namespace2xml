# Legacy differential

- namespace2xml 2.4.0: **agrees**.
- Contract: Section 6.4.3, the array container under a threshold that filters everything; Section
  6.2 verbosity as an output threshold; Section 26 item 82.
- Legacy observation: 2.4.0 refuses the same unresolvable reference, exits `1` and publishes
  nothing, so it produces this case's expected exit code and expected (empty) tree.
- Legacy observation, not compared: 2.4.0 also honours `--verbosity none`. Without it the
  baseline writes six operational lines to standard error -- its version banner, one `Reading
  input` line per source, and the failure -- and with it, none. It has no structured diagnostic
  stream, so the `[]` framing this case exists to pin has no counterpart there and the
  differential lane does not compare it.
- Clean behavior: Section 6.4.3 says "the array container is always written, overriding the Section
  6.2 statement that `none` produces no diagnostic output. `--verbosity none`, and any threshold
  that filters every produced diagnostic, yields exactly the two bytes `[]` followed by one LF".
  The profile produces one `REFERENCE002`, so the stream is non-empty before filtering and empty
  after: the case distinguishes "wrote an empty array" from "wrote nothing", which a clean run
  cannot.
- Why this case exists: Section 26 item 82 already claimed "`[]` under `--verbosity none`", and
  no fixture in the corpus passed `--verbosity` at all. The claim was carried by the manifest
  rather than by evidence, and the implementation meanwhile ignored the option entirely -- every
  threshold, including `none` and `critical`, emitted every diagnostic. A coverage claim nobody
  exercises is the failure mode this corpus exists to prevent, so the fixture is named for the
  sentence it pins.
- Why the agreement matters here: 2.4.0 implemented both halves of Section 6.2 that the rewrite had
  lost -- it filtered by threshold and it emitted operational messages. This is therefore a Section
  3.1 preservation case, not a divergence. Reported as issue 78.

## Not asserted

- Which operational messages appear at `trace` and `debug`. Section 6.2 names categories
  ("per-file parsing", "pipeline-phase progress") rather than message texts, and Section 22 places
  operational messages outside the diagnostic registry, so their prose is deliberately not
  compared by any fixture. Their levels are gated by unit tests instead.
- The text encoding under `none`. Standard error is empty there, which the corpus cannot
  distinguish from a run that wrote nothing for another reason; the `json` container is what
  makes the filtering observable.