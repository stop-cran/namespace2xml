# Legacy differential

- namespace2xml 2.4.0: **agrees**. The baseline reproduces the case's expected result observably,
  and this section explains why that is not evidence its CLI grammar is Section 6.2.
- Contract: Section 6.2 option-token grammar; Section 3.1 preserves "existing CLI option names";
  Section 26 item 86.
- Legacy observation: the baseline writes `app.properties` with the expected `name=example` and
  exits `0`, matching the case's expected tree and exit code. Its standard error is empty beyond
  the banner.
- Clean behavior: every long option accepts its value inline, so `--input=inputs/main.txt` is the
  same invocation as `--input inputs/main.txt` and `--scheme=schemes/main.txt` is the same as
  `--scheme schemes/main.txt`. The scheme selects `app.output=namespace` and the profile supplies
  `app.name=example`, so `app.properties` is written.
- Why the observable agreement is not compatibility evidence: 2.4.0's CommandLineParser
  incidentally supported the `--name=value` inline form because that is a convention of the
  library, not because any 2.4.0 contract fixed it. A caller who read the 2.4.0 documentation
  could not learn that the inline form was accepted, and a future library change could have
  removed it. Section 6.2 pins the uniform inline form so callers can rely on it, and this fixture
  discriminates a shipped tool that stops accepting it.