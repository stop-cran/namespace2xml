# Legacy differential

- namespace2xml 2.4.0: **differs**. 2.4.0 delegated argument parsing to CommandLineParser, whose
  grammar was never stated in any contract and whose failures carried no stable code and no
  machine-readable stream.
- Contract: Section 6.2 option-token grammar; Section 26 item 86.
- Legacy observation: a trailing option with no value produced the library's own usage text and a
  nonzero status, with no stable code and no machine-readable stream.
- Clean behavior: an option token that reaches the end of the argument vector still requiring a
  value is `CLI001` with exit 1, reported in the requested encoding.