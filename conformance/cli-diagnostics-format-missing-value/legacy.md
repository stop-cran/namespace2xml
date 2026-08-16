# Legacy differential

- namespace2xml 2.4.0: **agrees**, but for an unrelated reason: the `--diagnostics-format` option
  does not exist in 2.4.0.
- Contract: Section 3.2 deliberately corrected behavior; Sections 6.4.1 and 6.4.3.
- Legacy observation: the baseline exits `1` and produces no output tree, matching the case's
  expected exit code and empty tree. Standard error beyond the banner is empty; whatever 2.4.0
  wrote about the unfamiliar option went elsewhere, or nowhere, and the harness compares only the
  tree and the exit code.
- Clean behavior: the pre-scan resolves the encoding from the surviving valid occurrence, then
  ordinary validation reports `CLI001` for a missing value in that encoding, and the process exits
  1.
- Why the observable agreement is not compatibility evidence: 2.4.0 has no `--diagnostics-format`
  option, so whatever reason it exited `1` for is not the reason this case's expected diagnostic
  names. 3.0 emits one `CLI001` in the encoding the pre-scan resolved, then exits `1` before any
  input or scheme is opened. Both runs exit `1` with an empty tree, but only 3.0 emits a stable
  code, a `spec` anchor, and — when the surviving occurrence resolves to `json` — a machine-readable
  diagnostic stream. Section 3.2 exists so an invalid command line can still be reported in the
  encoding the caller asked for; an automated caller cannot read the baseline's failure.