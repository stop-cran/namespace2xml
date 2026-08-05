# Legacy differential

- namespace2xml 2.4.0: **differs**. The option does not exist in 2.4.0.
- Contract: Section 3.2 deliberately corrected behavior; Sections 6.4.1 and 6.4.3.
- Legacy observation: CommandLineParser rejects the unknown option with its own message and a
  nonzero status, with no stable code and no machine-readable stream.
- Clean behavior: the pre-scan resolves the encoding from the surviving valid occurrence, then
  ordinary validation reports `CLI001` for an unrecognized inline value in that encoding, and the process exits 1.
- The difference is intentional: an invalid command line must still be reportable in the
  encoding the caller asked for, or an automated caller cannot read its own failure.