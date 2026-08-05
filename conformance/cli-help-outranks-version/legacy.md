# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 6.1 informational-mode precedence.
- Legacy observation: CommandLineParser treats the combination as a verb conflict and does not
  guarantee which informational mode wins.
- Clean behavior: `--help` is checked first and wins regardless of token order, and no other
  argument is validated.
- The difference is intentional: precedence must be total so that an automated caller can rely
  on it.