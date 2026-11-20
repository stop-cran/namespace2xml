# Legacy differential

- namespace2xml 2.4.0: **agrees** on exit and output tree, but for a different reason. Under these
  exact arguments it reports the unknown `report-file` option and exits 1 before processing the
  input; the input remains unchanged. The differential lane does not compare the diagnostic.
- Contract: Section 21.5; Section 26 item 100.
- The report must not truncate an input, and configuration publication must not begin.
