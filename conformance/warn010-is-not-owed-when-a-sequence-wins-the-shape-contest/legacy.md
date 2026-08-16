# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `cfg.json` as
  `{ "a": { "k": 1, "0": "x", "1": "y" } }` and exits 0, so the content differs from the
  replacement's `{ "a": [ "x", "y" ] }` and not merely the line endings. **verified** — measured
  against the Appendix C.6 pinned 2.4.0 package on .NET 9.
- Contract: Section 8.7, which infers a sequence only from "a nonempty mapping containing only
  canonical nonnegative decimal keys"; Section 17.1's "a destination requiring one container shape
  uses the later container contribution and warns"; Section 3.2's `WARN010`, owed for "a JSON or
  YAML mapping inferred at step 11 [that] remains projected as a sequence".
- Legacy observation: 2.4.0 has no sequence model, so a JSON array is read as a mapping keyed by
  its decimal indices. The two contributions here therefore never meet as two shapes: they merge as
  one mapping, and the array's indices sit beside the author's `k` as ordinary siblings. Nothing is
  reported because, in that model, nothing happened. The array is gone as a type, and a consumer
  reading `cfg.json` back gets an object where the document supplied a list. The baseline also
  retypes the string `"1"` the input wrote as the number `1`, which is the scalar-kind divergence
  `json-output-options-and-escaping` records.
- Clean behavior: the mapping and the sequence are two container contributions at one path. Section
  17.1 keeps the later one, so `cfg.json` holds the array and the mapping's `k` is dropped, and the
  loss is reported as `TYPE002` naming the projected key and the destination. Exit stays 0, because
  Section 17.1 makes this a defined resolution rather than an error.
- What the case is for is the diagnostic that is **not** raised. The mapping at `cfg.a` is keyed
  `k`, so Section 8.7 never inferred it, and Section 3.2 scopes `WARN010` to a mapping inferred at
  step 11 — none was. The implementation derived that test from the rendered shape instead, on the
  reasoning that inference is the only thing that can make a node some document wrote as an object
  render as a sequence. Section 17.1's shape contest does it too, so the run named `inputs/a.json`,
  told its author that the keys there were all canonically numeric when the only key is `k`, and
  offered `cfg.a.type=mapping` as a way to undo an inference that never ran. The fixture asserts
  the whole stream rather than one record, because the defect was an extra warning rather than a
  wrong one, and only a stream assertion can fail on an extra.
