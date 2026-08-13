# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `<cfg lone="" mixed=""><elems p="" /></cfg>` — every
  value is replaced by an empty attribute and all three comments are gone — with CRLF and no final
  newline. Exit 0, nothing reported. **verified** — measured against the Appendix C.6 pinned 2.4.0
  package.
- Contract: Section 19.5's comment placement and indentation rules; Section 11.4's content-token
  ordering; `KNOWN-LIMITS.md` section 1.21.
- Legacy observation: XML output was not a rendering of the input document. Text became attributes,
  comments vanished, and no diagnostic distinguished the result from a faithful copy.
- Clean behavior: three placements, one rule each. `mixed` has every run as a content node with its
  own ordering value, so `x`, the comment and `y` keep their order. `elems` holds only elements and
  a comment, so the comment takes its own line at the children's indentation, ahead of `p` where its
  ordering value puts it. `lone` is the documented limit: Section 11.4 exposes its single text run
  as the scalar at the element path, so that run holds no ordering value, and the scalar is written
  before the comment even though the source wrote the comment first.
- This case exists to hold that limit still. It is the one place where an XML to XML round trip is
  not byte-identical, and the failure is quiet — the comment is present, the value is present, and
  only the side changes. A characterization fixture is the only thing that would notice the day the
  behaviour drifts, in either direction: toward a fix nobody recorded, or toward losing the comment
  outright. The other two elements are here so that a change to the general ordering rule cannot
  hide behind the limit.