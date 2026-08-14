# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `OutputRoot.properties` rather than
  `output.properties`, terminates every record with CRLF rather than LF, writes `cfg.tabbed` with
  a literal TAB rather than the Section 19.1 `\t`, and reports nothing at all.
- Contract: Section 16.9's `namespaceoutputoptions=AllowTrailingWhitespace`, which relaxes the
  Section 24 byte rule for one destination, and Section 19.1, which writes the entry and reports
  `WARN013` for each value it admits.
- Legacy observation, measured on this fixture's input: 2.4.0 has no such option and no such
  rule. It writes `cfg.trail=tail  <CR>` unconditionally and silently, which is the same payload
  this fixture expects but reached without a decision. The agreement on that one value is
  therefore accidental in the sense that matters: 2.4.0 would write it identically had the author
  never wanted it, and there is nothing in the run to tell them the line is fragile.
- Clean behavior: the option is opt-in, so the bytes are written only where an author asked for
  them, and `WARN013` names the path each time so the fragility is on the record. Section 8.1
  preserves a value's trailing spaces on read, which is why the option exists at all — without it
  this format could carry a value it could not write.
- The remaining members are unaffected and unreported. The option relaxes one rule for one
  ending, so `cfg.lead`, `cfg.tabbed`, `cfg.nbsp`, and `cfg.plain` are written exactly as they are
  under the default and produce no diagnostic.
