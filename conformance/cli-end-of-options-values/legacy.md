# Legacy differential

- namespace2xml 2.4.0: **agrees**. The baseline reproduces the case's expected result observably,
  and this section explains why that is not evidence its CLI grammar is Section 6.2.
- Contract: Section 6.2 option-token grammar; Section 3.1 preserves "existing CLI option names"
  and "missing-file warning-and-ignore behavior"; Section 26 item 86.
- Legacy observation: the baseline writes `app.properties` with the expected `name=example` and
  exits `0`, matching the case's expected tree and exit code. Its standard error is empty beyond
  the banner.
- Clean behavior: `--` ends option recognition, and every following token — here `-` and
  `--output` — becomes an ordinary value for the immediately preceding list-valued option `-i`.
  Neither file exists, so Section 7.2 warns once for each missing source and continues; `-s
  schemes/main.txt` and `-i inputs/main.txt` still supply the plan, so `app.properties` is written.
- Why the observable agreement is not compatibility evidence: 2.4.0's CommandLineParser accepted
  the same three trailing tokens without failing the run — how it interpreted them is not
  determinable from the observable, since standard error is empty and only the plan bytes are
  compared. The uniform grammar of Section 6.2 is nowhere stated in any 2.4.0 contract; a future
  library change, or a different argument parser, could have refused the same input. The
  specification pins the answer so callers who want to name a file whose name begins with `-`
  can rely on it.