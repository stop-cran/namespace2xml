# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 19.1 and 5.2.
- Legacy observation: the baseline emitted a bare `{}` for an empty mapping and a bare `[]` for an
  empty sequence, matching this case on `map`, `seq`, and `nested.inner`. It emitted the same two
  bare pairs for the JSON *strings* `"{}"` and `"[]"` -- measured, `text_map={}` and `text_seq=[]`
  -- so reading that output back produced two containers where the document held two strings, and
  the two source shapes were indistinguishable in the result. It also reordered the entries,
  emitting `nested.inner` ahead of the two text values rather than in source order.
- Clean behavior: Section 19.1 spells an empty container as the bare sentinel and a scalar whose
  text is exactly `{}` or `[]` as `\{}` or `\[]`, which is what makes the Section 8.3 reading its
  inverse. Section 5.2 keeps mapping children in source order, so `text_map` and `text_seq` stay
  where the document put them.
- The difference is intentional: an output that spells two distinct source shapes identically
  cannot be read back, and Section 3.3's normalized same-format round trip requires that it can.
