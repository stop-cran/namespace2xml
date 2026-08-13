# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 19.6 — under `GlobalSection` the global keys are written inside a section named
  `global`, placed where the preamble would have been.
- Legacy observation: 2.4.0 has no `inioutputoptions` directive at all. It reads the scheme, ignores
  the key in silence, exits 0, and writes the preamble the option exists to remove. It also writes a
  blank line before each section header, which Section 19.6 forbids everywhere.
- The difference is intentional twice over: the option is new, and the layout it produces is fixed
  by the byte rules of Section 19.6 rather than by the writer's taste.
- The profile declares `app.db.host` before `app.name` on purpose. `[global]` must therefore be
  moved ahead of `[db]` rather than landing there by the order the keys arrived in, so the expected
  bytes are not reproducible by an implementation that emits sections in encounter position.
- Silently ignoring an unrecognized directive is the 2.4.0 behavior this specification replaces with
  `SCHEME001`; that replacement has its own cases and is not what this one measures.