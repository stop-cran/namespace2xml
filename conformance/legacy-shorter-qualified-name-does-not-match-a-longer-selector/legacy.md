# Legacy differential

- namespace2xml 2.4.0: **agrees**.
- Contract: Section 3.2 removes matching based only on overlapping qualified-name parts, and
  Section 14.2 requires a candidate to have at least as many parts as the selector.
- Legacy observation: across ten Linux arm64 samples the baseline exits 0 and reproduces
  `out.properties` byte-for-byte. The shorter scalar `a` does not enter this output.
- Clean behavior: selector `a.b` selects only `a.b` and descendants, so the file contains exactly
  `keep=right`.
- This measured agreement disputes the proposed item 69 ownership: although the expected bytes pin
  the Section 14.2 rule for the replacement, this input does not expose the legacy overlapping-name
  defect and therefore cannot own the Section 3.2 correction claim.
