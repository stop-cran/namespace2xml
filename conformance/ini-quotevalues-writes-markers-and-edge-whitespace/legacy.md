# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits `0` and writes every value unquoted, so
  `inioutputoptions=QuoteValues` changes nothing about the file it produces.
- Contract: Section 19.6 — "`QuoteValues` emits double-quoted values, escaping `\` as `\\` and `"`
  as `\"`" — together with the rule the option exists to relieve, that "a value beginning with `;`
  or `#`, or having leading/trailing whitespace, is an error unless `QuoteValues` is selected".
- Legacy observation, measured on this fixture's input under its own `args.txt`: 2.4.0 writes
  `trail=tail ` with the space standing between the value and its line terminator, and writes
  `semi=; not a comment` and `hash=# not a comment` with a comment marker as the first character of
  the value text. It reports nothing, exits `0`, and terminates every record with CRLF.
- Clean behaviour: the three shapes above are the ones the option exists for. Under the default
  `RejectMultiline` they are refused outright with `INI001`, and under `QuoteValues` they are
  written inside quotation marks, where the trailing space is bounded on both sides and the marker
  is not the first character of anything a reader inspects. Either answer is a decision the author
  made; writing them bare is neither.
- It exists because the corpus held no `.ini` output under `QuoteValues` at all, so the option's
  bytes were unpinned in both directions: nothing said which two characters are escaped, and
  nothing said that every value is quoted rather than only the values that would otherwise be
  refused. `plain` and `inline` are in the file for that second reading — a writer that quoted only
  what it had to would emit them bare and pass a fixture built from the interesting values alone.
- It is also an input to `tools/check-ini-interop.js`, which reads the quoting back through a
  parser that unquotes, and to `tools/check-ini-interop.py`, which names it out of envelope because
  `configparser` does not.
