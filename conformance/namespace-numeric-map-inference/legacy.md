# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 8.7; Section 5.4 for dense rendering.
- Legacy observation: numeric path parts were ordinary mapping keys. A mapping written as `a.0`,
  `a.1` stayed a mapping, so it could not patch or concatenate with a JSON or YAML array at the
  same path, and cross-file sequence editing was not expressible.
- Clean behavior: a nonempty mapping whose surviving child names are all canonical nonnegative
  decimal ordering values projects as an explicitly indexed sequence at step 11. Explicit indices
  patch at their supplied values; native implicit items concatenate above the high-water mark.
  Leading-zero spellings and values above the supported maximum are ordinary keys and prevent
  inference for the whole mapping.
- The difference is intentional: Section 3 lists this as "a structural normalization exception"
  whose purpose is "controlled cross-file sequence patching". Section 8.7 also requires the
  `WARN004` compatibility warning when two sources contribute native implicit sequences at one
  path with no explicit `merge` directive, because concatenating and patching are both reasonable
  readings of that input and only one of them happens.
