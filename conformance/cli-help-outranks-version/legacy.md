# Legacy differential

- namespace2xml 2.4.0: **fails**. Given `--version --help` it writes the single line
  `namespace2xml 2.4.0+b1c230e974a04cb363b131aad027980502fe0321` to **standard error** and exits
  `1`. No help text is printed, so `--version` wins the combination, and an informational request
  is reported as a failed run on the stream reserved for diagnostics.
- Contract: Section 6.1 informational-mode precedence.
- Legacy observation: CommandLineParser decides the outcome, and 2.4.0 states no rule for the
  combination. The observed result is the opposite of the specified precedence, on the wrong
  stream, with the wrong exit code — three separate things an automated caller cannot rely on.
- Clean behavior: `--help` is checked first and wins regardless of token order, and no other
  argument is validated.
- The difference is intentional: precedence must be total so that an automated caller can rely
  on it.