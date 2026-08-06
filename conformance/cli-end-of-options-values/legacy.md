# Legacy differential

- namespace2xml 2.4.0: **differs**. 2.4.0 delegated argument parsing to CommandLineParser, whose
  grammar was never stated in any contract and whose failures carried no stable code and no
  machine-readable stream.
- Contract: Section 6.2 option-token grammar; Section 26 item 86.
- Legacy observation: `--` and a bare `-` were handled by the library's conventions, which
  differed from this grammar and were not part of any contract.
- Clean behavior: `--` ends option recognition and hands every following token to the immediately
  preceding list-valued option, so `-` and `--output` become ordinary input paths rather than an
  option and a value. A bare `-` is an ordinary value in this version and does not mean standard
  input.
- Why this case exists: this is the only way to name a file whose name begins with `-`, and the
  rule is worth pinning precisely because it makes a familiar-looking token stop being an option.
- How the case proves it: neither `-` nor `--output` exists, so each draws the Section 7.2
  missing-file warning naming it as a *source*. A tool that still treated `--output` as an option
  would emit one warning, or none, and would not name it. The run nevertheless succeeds and writes
  `app.properties`, because Section 7.2 makes a missing file warn-and-ignore rather than fail.