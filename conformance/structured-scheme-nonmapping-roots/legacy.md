# Legacy differential

- namespace2xml 2.4.0: **agrees**. It exits 1 and publishes no output, matching the Appendix C.6
  exit/tree oracle even though 2.4.0 did not provide the clean typed-root diagnostic contract.
- Contract: Section 15 and Section 26 item 94.
- Clean behavior: each valid nonmapping root is `SCHEME003`; the valid sibling source is checked in
  the same phase but no output is published after the blocking failures.
