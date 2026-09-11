# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 11.6, 17.4, and 19.5; Section 26 item 16.
- Legacy observation: all ten Appendix C.6 samples exited 0 but emitted
  `<r toCdata="" toText="" />`, losing both winning scalar values and their text-versus-CDATA
  spellings.
- Clean behavior: replacement carries the winning source spelling, so `new` is CDATA under
  `toCdata` and ordinary text under `toText`.
- Intentional correction: the Section 3.3 supported-feature guarantee preserves both XML scalar
  value and node kind; replacing a value cannot turn it into an empty attribute or discard its
  winning CDATA-versus-text spelling.
