# Legacy differential

- namespace2xml 2.4.0: **agrees** on the expected tree and exit code, but only because it rejects
  the unknown option rather than applying a warning policy after hidden diagnostics are collected.
  Its diagnostic output differs.
- Contract: Sections 6.2, 6.4.3, and 21.2; Section 26 item 92.
- Clean behavior: JSON framing emits an empty array at verbosity `none`, but the hidden warnings
  still refuse publication and produce exit code 1.
