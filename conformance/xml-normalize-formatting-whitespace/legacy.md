# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 11.7's `NormalizeFormattingWhitespace` compatibility mode — "non-whitespace
  text is preserved; whitespace in mixed content is preserved; whitespace under
  `xml:space="preserve"` is preserved; whitespace-only text between element children must be
  discarded as formatting indentation" — together with Section 16.8's placement of the
  root-level `xmlinputoptions` directive. Section 3 does not enumerate this specifically; the
  substantive contract is Sections 11.7 and 16.8.
- Legacy observation: the baseline writes different bytes at `r.xml`. The measurement records
  `content r.xml` at exit `0` with no standard error beyond the banner.
- Clean behavior: the whitespace-only text between `<a>` and its `<b>` child, and between
  `<r>` and `<a>` and `<m>`, is formatting indentation and is discarded on input;
  `NormalizeFormattingWhitespace` is a valid `xmlinputoptions` value at the scheme root. The
  serializer then re-indents each element-only parent with two spaces per depth. `<m>` is
  mixed content, so its whitespace — including the whitespace-only text after `<b>2</b>` — is
  preserved as data, which is why the expected file's `<m>` block is not re-indented.
- Why the difference is intentional: 2.4.0 has no `xmlinputoptions` directive and no
  `NormalizeFormattingWhitespace` mode, so the scheme entry is either an unknown-directive
  no-op or is treated as data; either way the XML reader keeps every whitespace-only text
  node in element-only content, and the serializer's own indentation is added on top. That
  produces different bytes at `r.xml` and would also weaken the same-format round-trip
  guarantee the fixture uses to distinguish the mode's opt-in effect from the default.
