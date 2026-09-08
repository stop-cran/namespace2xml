# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 11.4 canonical XML addressing and `WARN011`; Section 11.7
  `NormalizeFormattingWhitespace`; Section 19.5 XML rendering.
- Legacy observation: 2.4.0 exits `0` and replaces `<host>` after formatting-whitespace
  normalization, but writes CRLF line endings rather than the expected LF bytes and provides no
  stable diagnostic explaining the normalization.
- Clean behavior: normalization exposes the element at `server.host`, the ordinary overlay
  replaces it without `WARN011`, `WARN007` reports the discarded formatting nodes, and the writer
  emits the canonical LF bytes.
- The difference is intentional: the remedy is explicit and diagnosed while preserving the
  deterministic output-byte contract.
