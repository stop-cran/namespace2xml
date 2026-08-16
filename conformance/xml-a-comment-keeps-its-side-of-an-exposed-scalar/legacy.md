# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `<cfg lone="" mixed=""><elems p="" /></cfg>` — every
  value is replaced by an empty attribute and all three comments are gone — with CRLF and no final
  newline. Exit 0, nothing reported. **verified** — measured against the Appendix C.6 pinned 2.4.0
  package.
- Contract: Section 19.5's comment placement and indentation rules; Section 11.4's content-token
  ordering and its retention of an exposed run's ordering value on the scalar payload.
- Legacy observation: XML output was not a rendering of the input document. Text became attributes,
  comments vanished, and no diagnostic distinguished the result from a faithful copy.
- Clean behavior: three placements, one rule each. `mixed` has every run as a content node with its
  own ordering value, so `x`, the comment and `y` keep their order. `elems` holds only elements and
  a comment, so the comment takes its own line at the children's indentation, ahead of `p` where its
  ordering value puts it. `lone` exposes its single text run as the scalar at the element path under
  Section 11.4, and that run's ordering value travels with the payload, so the comment written
  before the value is written before the value.
- Why the case is here: `lone` is the one shape where the value's position is not carried by the
  node, so a projection that emitted the payload before consulting the ordering of anything else
  would still satisfy every other case in the corpus. The failure it guards is quiet — the comment
  is present, the value is present, and only the side changes — which is exactly the kind an
  eyeballed diff approves. The other two elements are here so that a regression in the general
  ordering rule cannot hide behind the exception, and so that a fix which simply moved every payload
  to the end fails them.
