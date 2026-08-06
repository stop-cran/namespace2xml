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
- How the case proves it: every option in `args-diagnostics.txt` uses the inline form, and the run
  produces `app.properties` with no diagnostics. A tool that rejected the form, or that read
  `--input=inputs/main.txt` as a path, could not produce that file.