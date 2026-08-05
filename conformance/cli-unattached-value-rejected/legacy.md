# Legacy differential

- namespace2xml 2.4.0: **differs**. 2.4.0 delegated argument parsing to CommandLineParser, whose
  grammar was never stated in any contract and whose failures carried no stable code and no
  machine-readable stream.
- Contract: Section 6.2 option-token grammar; Section 26 item 86.
- Legacy observation: a bare token in a leading position was consumed or ignored according to the
  library's own positional rules, which this tool never declared.
- Clean behavior: a value appearing when no option is accepting values is `CLI001` with exit 1.
  This tool has no positional parameters.
- Why this case exists: silently ignoring an argument the author wrote is the failure mode that
  produces a correct-looking run against the wrong inputs.