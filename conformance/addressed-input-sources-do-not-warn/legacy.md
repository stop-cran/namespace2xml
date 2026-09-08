# Addressed input sources do not warn

- namespace2xml 2.4.0: **fails**.
- Contract: Section 14.5; Section 26 item 91.
- Legacy observation: under this case's repeated `-i` arguments, 2.4.0 exits 1 before reading any
  source, reports that the input option is defined multiple times, and writes no output.
- Clean behavior: the selected defaults value is overwritten, the support value is reached only
  through a reference, and the hidden value is selected only by `output=ignore`; all three source
  occurrences count as addressed and the JSON diagnostic array is empty.
- Why the divergence is specified: 3.0 admits repeated input occurrences and preserves their CLI
  order, which is required to exercise source-level accounting across overlays.
