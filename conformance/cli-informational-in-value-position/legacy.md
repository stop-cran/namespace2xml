# Legacy differential

- namespace2xml 2.4.0: **differs**. 2.4.0 delegated argument parsing to CommandLineParser, which
  had no stated rule for an option token standing where a value was required.
- Contract: Section 6.1 informational precedence; Section 6.2 option-token grammar; Section 26
  items 61 and 86.
- Legacy observation: whatever the library did, undocumented.
- Clean behavior: Section 6.1 decides the informational mode "by scanning the raw token vector for
  the option token... up to the first --", and that scan "applies no other part of the grammar;
  in particular it does not work out which tokens are option values". So this invocation prints
  version information and exits 0 rather than reporting that `--diagnostics-format` has no value.
- Why this case exists: the alternative gives one token two readings at once — a value to the
  informational scan, and an option token to the Section 6.2 rule that a detached value may not be
  an option token. A tool that resolved the two differently in two places would be checkable
  against neither. This case pins which reading wins, at the only place both rules meet.
- How the case proves it: the case supplies args-diagnostics.txt explicitly, because Appendix C.4
  forbids appending `--diagnostics-format` to a vector that already contains it. Section 6.4.1
  gives an informational mode no diagnostic stream in either encoding, so no expected-diagnostics
  file is declared and standard error must stay empty.