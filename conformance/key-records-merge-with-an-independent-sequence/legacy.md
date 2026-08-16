# Legacy differential

- namespace2xml 2.4.0: **differs**, in three ways at once.
- Contract: Section 16.5, "If an independent sequence projection already exists at the same node,
  the transformed contribution combines with it as the later contribution under the Section 17.1
  sequence rules"; Section 17.2 on explicit numeric keys addressing matching ordering values;
  Section 26 item 22.
- 2.4.0 stamps the generated field onto the native sequence item as well, inventing `"name": 1` for
  an item that was never a mapping entry and therefore has no mapping key to record. Section 16.5
  transforms the mapping projection; an independent sequence item passes through untouched.
- 2.4.0 also writes the generated field as the number `0` rather than the string `"0"`, and omits
  the final newline. Both are covered elsewhere in the corpus; they are recorded here because a
  differential claim has to describe every divergence the case actually produces.
- Clean behavior: three items in Section 5.4 ascending ordering order. The record built from the
  numeric mapping child keeps ordering value 0, the untouched native item keeps 1, and the record
  built from `x` receives a fresh implicit value above the high-water mark and lands at 2. The
  native item therefore sits *between* two generated records, which is the observable consequence
  of folding rather than replacing.
- The case exists because it is the only one in the corpus where `key` meets an independent
  sequence at its own node. Every other `key` fixture targets a pure mapping, where the fold has
  nothing to fold onto and dropping the sequence projection outright is indistinguishable from
  merging with an empty one.
