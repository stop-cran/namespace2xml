# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits `0` and writes `quoted.ini` and `bare.ini`
  byte-identically, so the two destinations this case exists to contrast are indistinguishable in
  its output.
- Contract: Section 19.6 — "`EscapeMultiline` additionally emits LF as `\n`, CR as `\r`, and tab as
  `\t`", and "under `EscapeMultiline`, a literal backslash is always emitted as `\\` before LF, CR,
  and tab escaping, whether or not `QuoteValues` is also selected". Appendix A.3 supplies the input
  side: `\n`, `\r`, `\t` and `\\` are escapes of a namespace value producing LF, CR, tab and one
  backslash.
- Legacy observation, measured on this fixture's input under its own `args.txt`: `quoted.ini`
  differs by the quotation marks, and `bare.ini` agrees on content and differs only in line
  endings, which are CRLF.
- That agreement is reached without a decision, and the distinction matters here more than usual. A
  reduced probe with no `inioutputoptions` line at all — `p.k=a\nb` and `p.s=a\\b` to an INI
  destination — produces exactly the same `k=a\nb` and `s=a\\b`, so 2.4.0 neither decodes the
  Appendix A.3 escapes on read nor encodes them on write. The bytes match because two omissions
  cancel: the value 2.4.0 holds is the four characters of `a\nb`, not the three of `a`, LF, `b`. A
  tool that agreed by implementing the rule would have written the same line from a different
  value, and this fixture is the one place that difference is visible.
- Clean behaviour: `bare.ini` and `quoted.ini` are written from one value each, and differ only in
  the quoting `QuoteValues` adds. `slash` is the discriminating member: it is `a\\b` in both files,
  which is what "always emitted as `\\`" requires "whether or not `QuoteValues` is also selected",
  and it is the member that would read `a\\\\b` under an implementation that let the two options
  each double the backslash once.
- It exists because the corpus held no `.ini` output under `EscapeMultiline` at all, and because
  the sentence about backslashes is the only place in Section 19.6 where two options interact.
  `bare.ini` is out of envelope for both named parsers and is checked by neither; it is here
  because the specification distinguishes the two files and the corpus is what records that.
