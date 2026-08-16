# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `OutputRoot.properties`, exits 0, and reports
  nothing. Every value arrives intact, including the two trailing spaces of `cfg.trail`, so the
  disagreement is not about the data but about whether the file is safe to hand to a consumer.
- Contract: Section 19.1 refuses an entry whose emitted value ends in a space, and Section 24
  states the byte rule it protects — a text output must "contain no line ending in a space or a
  TAB, except where a Section 16.9 output option explicitly relaxes the rule for one destination".
- Legacy observation, measured on this fixture's input: 2.4.0 emits CRLF rather than LF, so the
  final scalar of every record is CR and the trailing spaces of `cfg.trail` sit one position
  earlier. They are still there — the line reads `cfg.trail=tail  <CR>` — and any consumer that
  normalizes CRLF to LF, which is the ordinary case on a checkout, exposes a line ending in two
  spaces that the next formatter or editor save may remove. 2.4.0 also writes `cfg.tabbed` with a
  literal TAB rather than the `\t` Section 19.1 requires, producing a second forbidden ending.
- Clean behavior: the value is data under Section 8.1, which does not trim a record, and Section
  8.3 gives namespace values no `\u{HEX}` form, so there is no escaped spelling to write it with.
  Section 19.1 therefore refuses the entry as blocking `NAMESPACE001` and names the two remedies:
  `quotednamespace`, which quotes the value, or `namespaceoutputoptions=AllowTrailingWhitespace`.
- The other four members are the boundary of the rule and none of them is refused. `cfg.lead`
  keeps its leading spaces because Section 8.1 takes every character after the separating `=` as
  the value and no line ends in them. `cfg.tabbed` is written as `body\t` under the Section 19.1
  escape list, so no entry ends in a literal TAB. `cfg.nbsp` ends in U+00A0 and is written
  literally, because Section 24 names a space and a TAB rather than every character carrying the
  Unicode `White_Space` property — the hazard is what a consumer strips, and a trailing U+00A0
  survives the checks that remove a trailing space.
- This is a deliberate 3.0 refusal of input 2.4.0 accepted, and it is the loud kind: the run fails
  and names its remedy, rather than writing a file whose last two bytes a later tool may delete.
