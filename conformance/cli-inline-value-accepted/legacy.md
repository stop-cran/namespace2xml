# Legacy differential

- namespace2xml 2.4.0: **differs**. 2.4.0 delegated argument parsing to CommandLineParser, whose
  grammar was never stated in any contract and whose failures carried no stable code and no
  machine-readable stream.
- Contract: Section 6.2 option-token grammar; Section 26 item 86.
- Legacy observation: the inline `--name=value` form was whatever the library happened to accept,
  and was documented nowhere.
- Clean behavior: every long option accepts its value inline, so `--input=inputs/main.txt` is the
  same invocation as `--input inputs/main.txt`.
- Why this case exists: the uniform inline form is the amendment's whole point. A unit test can
  show the parser accepts it; only a corpus case shows the shipped tool does.
- Preview scope: the expected exit code is 70 only while the transformation pipeline is
  unimplemented. When the pipeline lands, this case must be updated with it.