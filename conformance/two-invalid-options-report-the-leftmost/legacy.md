# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 22's rule that where a cardinality admits fewer records than the run detects,
  "the survivor is the one detected first in the traversal that phase specifies", and that
  "command-line parsing traverses arguments left to right under Section 6, so an invocation
  carrying two invalid option values reports the leftmost"; Section 22's `CLI001` cardinality of
  "once per invocation"; Sections 6.2 and 6.4.1 for the two option values.
- Legacy observation: the baseline throws an unhandled `InvalidOperationException` out of its
  argument parser before examining either value, prints a stack trace naming its own
  `Program.Main`, and writes no output tree.
- Clean behavior: one `CLI001` anchored at Section 6.4.1, the leftmost of the two invalid values,
  and exit 1.
- Why the difference is intentional: this case exists as one half of a pair with
  `swapping-two-invalid-options-swaps-the-reported-one`, which supplies the same two options in the
  other order and expects the anchor to swap. Either case alone would also be satisfied by an
  implementation that checked `--diagnostics-format` before `--verbosity` for reasons of its own,
  so neither is evidence about position without the other. The baseline cannot distinguish them
  because it reaches neither value.
