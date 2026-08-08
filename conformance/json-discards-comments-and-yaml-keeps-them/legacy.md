# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 19.3; Section 19.4; Section 16.9; Section 4.5.
- Legacy observation: neither format existed, so comments had nowhere to be kept or discarded.
- Clean behavior: Section 19.3 "renders comments nowhere and emits a summarized discard warning
  when comments exist" — one warning for the destination, not one per comment. Section 19.4 emits
  retained comments in normalized positions, so the leading comments bound during namespace
  parsing precede the entries they were bound to. Section 16.9's `DiscardComments` suppresses
  them without a diagnostic, because discarding is what the option asked for.
- The difference is intentional: silence about a discarded comment would make JSON output look
  lossless when it is not, and a per-comment warning would bury the fact in noise.
