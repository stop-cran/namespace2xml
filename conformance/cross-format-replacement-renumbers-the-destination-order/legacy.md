# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 21.3, 17.5 and 24.
- Legacy observation: there was no publication key and no destination order, so a run that
  diagnosed several destinations reported them in whatever order the work finished in.
- Clean behavior: Section 21.3 orders destinations "by the minimum Section 17.5 fold key among
  contributions whose data or retained destination high-water state survives into the final folded
  plan", and Section 17.5 says a cross-format replacement "discards the complete accumulated plan"
  and therefore resets that key to the replacing contribution. Section 24's second group is ordered
  by the resulting index.
- Why the case is shaped this way: the surviving key is not known while the fold is running. Here
  `shared.txt` is reached first, by `p.output`, and `r.txt` second, by `r.output`; numbering a
  destination when it is first reached would put `shared.txt` before `r.txt`. But `q.output`
  replaces `shared.txt` across formats, so its surviving key is `q.output`'s, which is later than
  `r.output`'s, and the two destinations swap. A `WARN005` at `r.txt` and a `WARN005` at
  `shared.txt` therefore separate the two orders using nothing but the index, since they share a
  phase, a group, a code and an absent source ordering key.
- The third diagnostic is here to close a second reading. `shared.txt` also collides with
  `t.output` under `filemerge=error`, so it carries a `COLLISION001` as well. Ordering group 2 by
  code and path instead of by destination index would emit `COLLISION001` at `shared.txt`, then
  `WARN005` at `r.txt`, then `WARN005` at `shared.txt` -- a third order, distinct from both the
  specified one and the first-reached one. The expected stream keeps the two `shared.txt`
  occurrences adjacent, which only an index shared by both produces.
