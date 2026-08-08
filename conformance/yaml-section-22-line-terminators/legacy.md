# Legacy differential

- namespace2xml 2.4.0: **crashes**.
- Contract: Section 3.2 correction against unhandled user-input exceptions, together with
  Section 22's rule that "a line is terminated by LF, CRLF, or a lone CR, and by nothing else;
  consistently with Section 8.1, U+0085, U+2028, and U+2029 do not terminate a line", and
  Section 10.2's rejection of anchors.
- Legacy observation: the baseline exits with an unhandled-exception status
  (`System.InvalidOperationException: Sequence contains no elements`, exit `-532462766`) and
  also stages a partial `a.ini` file before crashing. The measurement records
  `exit -532462766 (expected 1); extra a.ini` with that stderr. An earlier draft of this note
  claimed the baseline reported wrong line numbers under Section 22; the actual baseline never
  reaches a diagnostic at all on these inputs.
- Clean behavior: Section 10.2 rejects each anchor as `PARSE001` at the Section 22 one-based
  line and column of the anchor token, and Section 22's line count excludes U+0085, U+2028, and
  U+2029, so `control.yaml` reports line 3 and the other three report line 2. The exit is `1`
  and no partial output is written; Section 15.4 requires that a failed source contributes no
  partial overlay and that transformation and planning errors never produce a partial output
  instance for a later phase.
- Why the difference is intentional: 2.4.0 has no `RestrictedYaml1` refusal, so it hands the
  four documents to the host YAML scanner and continues into the INI writer. Something in
  that pipeline — a sequence assumed nonempty by the writer, or by the mapping-to-INI
  projection — throws under user-visible conditions, which is exactly the unhandled-exception
  class Section 3.2 removes. Producing `a.ini` on the way past a crash also violates
  Section 15.4's rule that a failed source stages no partial output. Whether the baseline's
  specific exception path was in the YAML reader, the INI writer, or the projection between
  them is not readable from the observation, and the fixture pins only that the run ended in a
  crash rather than in a diagnosed refusal.
