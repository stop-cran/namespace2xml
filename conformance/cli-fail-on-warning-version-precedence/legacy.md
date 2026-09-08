# Legacy differential

- namespace2xml 2.4.0: **fails**. Its `--version` mode exits 1 and writes to standard error.
- Contract: Sections 6.1 and 6.2; Section 26 item 92.
- Clean behavior: informational pre-scan wins before validation, so the otherwise invalid valued
  form is ignored and version information is written to standard output with exit 0.
