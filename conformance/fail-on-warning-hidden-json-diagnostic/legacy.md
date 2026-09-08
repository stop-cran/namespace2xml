# Legacy differential

- namespace2xml 2.4.0: **fails**. It does not recognize the 3.0 warning-publication policy.
- Contract: Sections 6.2, 6.4.3, and 21.2; Section 26 item 92.
- Clean behavior: JSON framing emits an empty array at verbosity `none`, but the hidden warnings
  still refuse publication and produce exit code 1.
