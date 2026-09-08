# Legacy differential

- namespace2xml 2.4.0: **fails**. Its command-line grammar does not implement the 3.0 `--` contract.
- Contract: Section 6.2; Section 26 items 86 and 92.
- Clean behavior: after `--`, the token is an input path rather than an enabled policy. Its missing
  path warns, ordinary warning behavior remains exit 0, and the valid source is still published.
