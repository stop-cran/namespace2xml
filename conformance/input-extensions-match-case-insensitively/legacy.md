# Legacy differential

- namespace2xml 2.4.0: **differs**. It reads all four inputs as namespace profiles, reports
  `Error parsing input: unexpected ...` once per file — `j.JSON` at line 1 column 2, `y.YAML` and
  `s.YML` at column 5, `x.XML` at column 38 — exits 1, and writes nothing. The case expects
  `cfg.properties` with one entry contributed by each of the four formats.
- Contract: Section 7.1, and Section 3.2 as a correction.
- Legacy observation: 2.4.0 selected the input reader by comparing the file extension with
  ordinal case-sensitive equality against the lowercase spellings. `data.JSON` matched none of
  them and fell through to the "every other extension" branch, so a JSON document was handed to
  the namespace-profile parser. The column numbers in the four errors are where each format's
  syntax first stops looking like a `name=value` record.
- Clean behavior: Section 7.1 states that input file extensions "are matched case-insensitively",
  and lists `.json`, `.yaml`, `.yml`, and `.xml`. Only after none of those matches does the file
  use namespace-profile parsing. Each of the four inputs here contributes exactly one entry under
  `cfg`, and their order in the output follows CLI source order.
- The failure this catches is loud rather than silent, which is the only reason it was ever
  survivable: a document that fails to parse at least says so. The same defect is silent whenever
  the mis-read file happens to be valid namespace-profile text — a `.YML` file of `a: 1` lines
  parses as namespace records with no error and produces a tree nobody asked for.
