# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits 0 rather than the expected 1 and writes
  `cfg.properties` (not `cfg.sh`) containing one line, `a-b="1"`, with the value spelled in
  *double* quotes. Two divergences from what §19.2 requires: the key `a-b` is written to a
  file at all — a hyphen is not part of the POSIX shell identifier grammar — and the file
  written is the namespace-profile file, not the shell file, with double-quoting instead
  of the single-quote escape §19.2 selects.
- Contract: Section 19.2 shell-identifier rule and its blocking `SHELL001` diagnostic.
  Section 22 fixes the cardinality of `SHELL001` at "once per projected key and output
  instance". Section 3 does not enumerate this correction; it is a substantive rule of
  §19.2 that 2.4.0 did not implement.
- Legacy observation: 2.4.0 recognized `output=quotednamespace` as a synonym for the
  namespace-profile output rather than as a distinct format with its own validation. It
  wrote the `properties` extension, joined path parts with `.` rather than with `_`, and
  never checked the resulting text against the POSIX shell identifier grammar
  `[A-Za-z_][A-Za-z0-9_]*` — so an invalid identifier reached what the baseline believed
  was a shell file that a POSIX shell in fact cannot source. The double-quoting choice
  came from the same conflation: the baseline's namespace-value encoder emits a
  quoted value here rather than the single-quoted shell escape §19.2 defines.
- Clean behavior: §19.2 states that "keys must be valid shell identifiers after applying
  root and delimiter: `[A-Za-z_][A-Za-z0-9_]*`. Invalid keys are `SHELL001`." The path `a-b`
  contains a hyphen, which is not in the identifier alphabet, so exactly one `SHELL001`
  is emitted at the path `a-b` and the destination `cfg.sh` under §22's cardinality. §21.2
  aborts publication, so nothing is written and the run exits 1.
- The difference is intentional: quoted-namespace output is defined as POSIX shell
  assignment output, and the shell's identifier grammar is what makes such a file
  `source`-able. An implementation that publishes an invalid identifier gives its caller a
  file that cannot be read by the very consumer the format exists to serve, and that
  publishes it under an extension (`properties`) that names a different consumer, so no
  automated shell caller can tell it apart from a valid one until it is executed.
