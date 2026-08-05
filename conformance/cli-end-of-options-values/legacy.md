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
- Preview scope: the expected exit code is 70 only while the transformation pipeline is
  unimplemented. When the pipeline lands the two extra paths will not resolve, and this case must be
  updated with it.