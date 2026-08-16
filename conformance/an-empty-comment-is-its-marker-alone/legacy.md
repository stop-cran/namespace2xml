# Legacy differential

- namespace2xml 2.4.0: **differs**. It drops the comment from all four outputs, writes CRLF line
  endings, and exits 0 with nothing said about the loss. It also diverges here in two ways this
  case is not about: `cfg.yaml` loses the `cfg` root and begins `a: 1`, and `cfg.xml` renders the
  scalar as the attribute `<cfg a="1" />` with no terminating newline.
- Contract: Section 24, which requires that no line of a text output end in a space or a TAB;
  Sections 19.1, 19.4, 19.5 and 19.6, which fix the comment bytes of each destination.
- Legacy observation: a comment whose text is empty is indistinguishable from no comment at all,
  because 2.4.0 emits neither.
- Clean behavior: the comment survives to every destination that has a comment form, and its
  emptiness is preserved rather than being rounded to absence. Because the text is empty, the space
  that normally separates the marker from the text is not written, so the line is the marker alone.
- The case is deliberately cross-format. The marker-and-space rule is stated separately for the
  namespace, YAML and INI encodings, and XML frames its comments differently again, so an empty
  comment is exactly the input on which three independently worded rules could disagree. They did:
  the YAML writer already emitted `#` while the INI and namespace writers emitted `#` and a
  trailing space, and no fixture in the corpus covered the difference. The XML destination is
  carried along because `<!---->` is the one spelling among the four that needs no such rule.
- The trailing space is not a cosmetic complaint. It would have been the only trailing whitespace
  the tool ever wrote, and trailing whitespace is invisible in an editor and stripped silently by a
  great deal of tooling — so it is the one class of specified byte a consumer could destroy without
  ever seeing it.