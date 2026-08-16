# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `cfg.properties` as five lines — `empty=[]`,
  `lone=hello`, `mixed.0=one`, `mixed.1=null`, `mixed.2=three` — and exits 0. The case
  expects three lines: `lone=hello`, `empty=` (an empty value, not the literal `[]`), and
  `mixed=one\n\nthree` with the two-character `\n` escape between items and the middle null
  contributing an empty logical line. Three defects: `type=multiline` is not implemented at
  all, so the empty sequence leaks the reader's textual `[]` into the value, the three-item
  sequence is projected as three separate indexed keys instead of being joined, and the
  emitted keys are reordered by the baseline's dictionary iteration.
- Contract: Section 16.6 `multiline` transformation; Section 19.1 namespace value encoding
  (LF as `\n`); Section 3.2 corrections against "silent loss of multiline values in JSON or
  XML" and "dictionary iteration order".
- Legacy observation: 2.4.0 had no `multiline` type. The three scheme lines addressing it
  were unrecognized `type=` values, ignored rather than reported, so each of `lone`,
  `empty`, and `mixed` reached the namespace writer with whatever shape the reader
  produced. YAML's empty flow sequence `[]` was carried through as a mapping value with no
  container structure the writer knew how to spell, so its `ToString()` — the literal
  `[]` — was written as the value. The `mixed` sequence was projected as an indexed
  mapping and each item emitted as its own indexed key; `~` was decoded to the string
  `null` by the YAML reader. And the three top-level keys were emitted in the order the
  baseline's underlying dictionary produced them, which put `empty` first and `mixed`
  before `lone`.
- Clean behavior: §16.6 states that under `multiline` "a lone scalar is a one-line value
  and is unchanged; an empty sequence becomes the empty string; a nonempty sequence must
  contain only scalar or null payloads; null contributes an empty line". §19.1 then
  represents the joined scalar in one physical namespace record with "LF as `\n`". So
  `lone=hello` is unchanged, `empty=` is the joined empty string, and `mixed=one\n\nthree`
  spells the three-item join `one`, empty line, `three` with two `\n` escapes.
- The difference is intentional: an implementation that spells a sequence as a mapping of
  indices in the namespace file has thrown away the fact that the source was one
  logical multi-line value, and a consumer reading the file back has no way to distinguish
  that shape from a genuine numeric-map. `multiline` exists in §16.6 for the same reason
  §19.1 refuses to write a literal LF between records: the flat format cannot represent a
  sequence and a joined scalar the same way, so the scheme has to choose, and choosing
  silently is worse than either.
