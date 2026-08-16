# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `a=1` and `b=2` and nothing else, dropping both the
  inline comment on `b` and the document-trailing comment.
- Contract: Section 19.1, which normalizes an inline comment to a full-line comment before the key
  it annotates and keeps a document-trailing comment at end of file; Section 26 item 13.
- Legacy observation: the two comments are read from the YAML source and then lost on the way to
  the namespace encoding, silently. Nothing in the run says a comment was discarded.
- Clean behavior: the namespace encoding has no inline comment form, so an inline comment becomes
  a full-line comment immediately above its key rather than being dropped. Section 19.1 states this
  rather than leaving it to the writer, because the alternative reachable answers — discard, or
  append after the record — are each defensible and mutually incompatible, and a comment that
  explains a setting is worth less the further it drifts from it.
- The inline comment is deliberately on the **second** key. On the first key, "before its key" and
  "at the top of the document" are the same line, and the case would pass against an owner binding
  that had been lost entirely. Rebinding the comment to the document instead of to `b` moves it,
  and the case fails, which is the property being fixed here.
