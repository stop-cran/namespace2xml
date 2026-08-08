# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 16.8's rule that "selector-qualified input-option directives are blocking
  scheme errors because input parsing occurs before output instances exist", together with
  Section 15's rule that "unknown directives are blocking errors". Section 3 does not
  enumerate this specifically; the substantive contract is Sections 15 and 16.8.
- Legacy observation: the baseline accepts the selector-qualified `r.xmlinputoptions`
  directive, produces `r.xml`, and exits `0`. The measurement records
  `exit 0 (expected 1); extra r.xml` with no standard error beyond the banner.
- Clean behavior: `r.xmlinputoptions=NormalizeFormattingWhitespace` is `SCHEME001` because the
  directive can only be root-level, no output is planned, and the run exits `1`.
- Why the difference is intentional: 2.4.0 has no `xmlinputoptions` directive at all, so the
  scheme entry either falls under an "unknown directive" path that 2.4.0 does not diagnose,
  or is treated as data at path `r.xmlinputoptions` and folded into the output tree, or both;
  in every reading the run continues. Selector-qualified input options cannot mean anything
  in the specified pipeline, because Section 15.1 step 2 compiles root-level input options
  before output instances exist, and letting the run continue with a directive whose scope
  cannot be honored publishes a plausible-looking file for a request the tool cannot act on.
  The fixture pins only that the tool must not continue.
