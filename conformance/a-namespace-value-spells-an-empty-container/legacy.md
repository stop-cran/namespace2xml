# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 8.3, 16.7, and 18.
- Legacy observation: the baseline read a bare `{}` and a bare `[]` as an empty mapping and an
  empty sequence, and left every value that merely contains a bracket pair alone -- measured, it
  agrees with this case on `bare_map`, `bare_seq`, and all five near misses. It had no escape.
  `\{}` read as the three-character string `\{}` and `\\{}` as the four-character `\\{}`, because
  value backslashes were never decoded, so the two-character string `{}` could not be written at
  all: every spelling of it was either the container or a string still carrying its backslashes.
  `substitute` did not exempt it either -- under `cfg.literal.substitute=None` the baseline still
  produced a container.
- Clean behavior: Section 8.3 keeps the whole-value sentinel and adds the escape the baseline
  lacked. `\{}` is the two-character string `{}`, and the three-character string the baseline gave
  that spelling is now written `\\{}`, because Section 8.3's `\\` decodes to one backslash before
  the remaining `{}` is ordinary text. Section 16.7 gives a second route: the sentinels are value
  syntax, so `substitute=None` reads all four spellings as the literal text they contain.
- The difference is intentional: a convention with no escape is not expressible, and the value it
  displaced was unreachable. Section 19.1 emits `\{}` for a scalar that is exactly `{}`, so the two
  readings are inverse rather than merely disjoint.
