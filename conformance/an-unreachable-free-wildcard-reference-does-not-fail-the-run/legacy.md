# Legacy differential

- namespace2xml 2.4.0: **agrees**.
- Contract: Section 14.4 reference closure, both sentences; Section 13.3 free wildcard references;
  Section 22 counting `REFERENCE001` once per reachable owning value; Section 26 item 46.
- Legacy observation: the baseline exits `0` and writes `out/app.properties` holding `k=1` and
  `out=1` — byte for byte `expected/app.properties` on the Linux differential lane. It reports none
  of the six defective references under `dead` and `orphan`. Measuring the same case on Windows
  shows 12 bytes against 10, because 2.4.0 writes `Environment.NewLine`; that is the runner, not
  the tool, which is why Appendix C.6's lane is Linux-only and why the verdict here is agreement
  rather than a line-terminator divergence.
- Clean behavior: Section 14.4 says "missing, cyclic, ambiguous, free-wildcard, and non-scalar
  references in entries unreachable from every concrete output instance do not fail the run", and
  its last paragraph says a selector whose winning declaration is `output=ignore` "creates no
  output instance and no reference-reachability root". The profile writes both kinds of
  unreachable owner. Under `orphan`, which no declaration selects, it writes one of each defect the
  first sentence names. Under `dead`, which `output=ignore` suppresses, it writes the free-wildcard
  case in both of its spellings. None of the eight may be reported, and the run must exit `0`
  having written only the reachable subtree.
- Why the agreement matters here: this is a Section 3.1 preservation case rather than a divergence.
  The rewrite had regressed it. 2.4.0 accepted `${app.*}` in an unreachable entry and exited `0`;
  3.0.0-preview.3 refused the same profile with `REFERENCE001` and exited `1`, because the
  free-wildcard case alone was refused by the value lexer in the input phase, before any output
  instance existed and therefore before reachability could be known. The other four members of
  Section 14.4's list were already evaluated at Section 15.1 step 15, where the test is available,
  which is why they behaved correctly and made the one exception hard to see. Reported as issue 77.

## Not asserted

- The reachable case. A free-wildcard reference an output instance does reach is still
  `REFERENCE001`, and `reference-scalar-only-and-free-wildcard-rejected` pins that with the
  `*[identifier]` spelling. Asserting both halves in one fixture is not possible, because the
  reachable half fixes the exit code at `1` and would mask the suppression this case exists to
  show.
- Whether an undefined capture inside a reference is `REFERENCE001` against Section 13.3 or
  `WILDCARD001` against Section 12.2. That boundary was raised separately and is now settled:
  Section 12.2 scopes its error to a capture "outside a reference", Section 13.3 governs one
  written inside a reference, and Appendix B draws the same line. The two cases
  `an-undefined-capture-outside-a-reference-names-its-rule-once` and
  `an-unbound-capture-inside-a-reference-names-each-owning-value` pin both sides, including the
  differing cardinalities that make the division observable.