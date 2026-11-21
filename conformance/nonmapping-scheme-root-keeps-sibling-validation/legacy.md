# Legacy differential

- namespace2xml 2.4.0: **agrees** on exit status and output tree. It exits `1` and creates no
  output root because its argument parser rejects repeated `-s`, before checking either scheme.
- Clean behavior: Sections 15 and 15.4 discard the non-mapping root and report `SCHEME003`, but
  still check the other source and report `SCHEME001` for its invalid `substitute` mode under
  Section 16.7. Both diagnostics are emitted before the scheme phase aborts.
