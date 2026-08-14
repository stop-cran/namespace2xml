# Legacy differential

- namespace2xml 2.4.0: **crashes**.
- Contract: Section 22's rule that where a cardinality admits fewer records than the run detects,
  "the survivor is the one detected first in the traversal that phase specifies", and that
  "command-line parsing traverses arguments left to right under Section 6, so an invocation
  carrying two invalid option values reports the leftmost"; Section 22's `CLI001` cardinality of
  "once per invocation"; Sections 6.2 and 6.4.1 for the two option values.
- Legacy observation: the baseline throws an unhandled `InvalidOperationException` out of its
  argument parser before examining either value, prints a stack trace naming its own
  `Program.Main`, and writes no output tree.
- Clean behavior: one `CLI001` anchored at Section 6.2, the leftmost of the two invalid values,
  and exit 1.
- Why the difference is intentional: this is `two-invalid-options-report-the-leftmost` with the two
  options exchanged, and the reported anchor moves with them. That is the observation the pair
  exists to make: the surviving occurrence is chosen by position in the argument vector and not by
  any ranking among the options themselves, so correcting the first invalid value reveals the
  second rather than changing which one the tool considers important.
