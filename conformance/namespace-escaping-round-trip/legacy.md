# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 8.2 and 19.1; resolved legacy issue 41.
- Legacy observation: namespace output had no general escape vocabulary, so a name part
  containing the delimiter, a leading `#`, or a leading `!` could not be written back in a form
  that reads as the same name.
- Clean behavior: name encoding is total and injective. A scalar beginning a delimiter occurrence
  is always `\u{HEX}`, so a literal dot is `\u{2E}` and never `\.`; a leading `#` on an ordinary
  component and a record-leading `!` take their Section 8.2 forms `\#` and `\!`.
- The difference is intentional: without it a profile could not round-trip through its own output
  format, which is the property every other guarantee in Section 19 rests on.
