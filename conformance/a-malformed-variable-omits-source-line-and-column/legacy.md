# Legacy differential

- namespace2xml 2.4.0: **agrees**. Both tools reject the malformed variable, write nothing and
  exit 1, so a migrating run's observable result is unchanged. The correction this case pins is
  entirely in the diagnostic stream, which Appendix C.6 excludes from the verdict, and the prose
  below is where it is recorded.
- Contract: Section 8.1's rule that "a diagnostic reporting a condition inside a command-line
  variable omits `source`, and therefore also omits `line` and `column`", because "the Section
  6.4.3 `source` member names an input or scheme file, and a variable is neither; a synthetic file
  name there would be indistinguishable from a real one". The variable "is identified in the
  diagnostic's message by its one-based position in `-v` token order".
- Legacy observation: the baseline reports `Error parsing input: Unexpected end of input reached,
  file: <command line>, line: 1, column: 15` and exits 1.
- Clean behavior: the run reports one `PARSE001` in the input phase carrying no `source`, no
  `line` and no `column`, and exits 1.
- Why the difference is intentional: `<command line>` is exactly the synthetic file name Section
  8.1 rules out. A consumer reading the baseline's stream cannot tell that string from a real path
  without knowing the convention, and `line: 1, column: 15` locates the fault inside a file that
  does not exist. The location members are also the wrong instrument here: with two `-v` tokens the
  baseline's `line: 1` is true of both, so the one thing the reader needs — which argument failed —
  is the one thing the message does not say. 3.0 drops all three members, which makes the absence
  of a file explicit rather than simulated, and names the argument as `-v[2]` in the message
  instead.