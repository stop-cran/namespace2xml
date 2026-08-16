# Legacy differential

- namespace2xml 2.4.0: **agrees** that `--help` exits successfully.
- Contract: Section 3.1 preserved behavior; Section 6.1 informational-mode precedence.
- Legacy observation: prints a CommandLineParser-generated help screen and exits 0.
- Clean behavior: prints the Section 6.2 option surface plus specification, diagnostics and
  reporting links, and exits 0.
- The difference is intentional: help text is prose and is not part of byte-identical
  determinism, but the exit status and the stdout channel are contractual.