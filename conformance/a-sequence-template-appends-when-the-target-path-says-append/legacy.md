# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits `0` and writes `x.tags.0=red`, `x.tags.1=blue` —
  the same bytes it writes without the `merge` directive at all, so it discards the destination's
  `green` and ignores `append` for a generated contribution. CRLF-terminated, under the Section 24
  divergence.
- Contract: Section 10.4 for extraction, Section 5.4 for the rebase arithmetic, Section 16.10 for
  `append`.
- This case exists because Section 10.4 now tells a reader what to do about the behaviour its
  sibling `a-native-sequence-template-overrides-the-destination-items-it-addresses` pins: a template
  spelled as a sequence replaces the items it addresses, and `merge=append` at the target path is
  the way to add to them instead. That sentence is normative, so it is asserted here rather than
  left as advice.
- Why the expectation is `green`, `red`, `blue`. `green` is the sole contribution at `a.x.tags` from
  the earlier source and Section 5.4 does not rebase "a first or sole source contribution … merely
  because `merge=append` is configured", so it keeps the implicit value `0`. The generated
  contribution supplies explicit `0` and `1`; Section 5.4 rebases an appended explicit item in
  ascending original ordering value, raising the high-water mark to at least the supplied value and
  then allocating `high-water + 1`. From a high-water mark of `0`: `red` (supplied `0`) allocates
  `1`, then `blue` (supplied `1`) allocates `2`. Rendering emits dense indices `0`, `1`, `2`.
- The contrast with the sibling is the point. Identical inputs, one directive apart, and the two
  readings of Section 10.4 that #75 raised are exactly the two results — except that here the
  appending result is reached deliberately, by a directive, rather than by an extraction rule the
  user cannot see. Raised as
  [#75](https://github.com/stop-cran/namespace2xml/issues/75).
