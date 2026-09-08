# Legacy differential

- namespace2xml 2.4.0: **agrees** on the expected tree and exit code, but only because it rejects
  the unknown option rather than enforcing the 3.0 valueless-flag grammar. Its diagnostics differ.
- Contract: Section 6.2; Section 26 item 92.
- Clean behavior: a valued spelling of the valueless option is `CLI001`.
