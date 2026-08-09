# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `r.xml` as
  `<?xml version="1.0" encoding="utf-8"?>` on one line then `<r target="" use="" />` on
  the next, and exits 0. Both scalar values (`value1` and its resolved reference) are
  lost, and both elements are collapsed onto the root element as empty attributes. The
  case expects the full three-child `<r>` with `<target>value1</target>`, the
  `<!--annotation-->` comment retained as a node, and `<use>value1</use>` after reference
  resolution.
- Contract: Section 11.5 "XML comments are retained as ordered comment nodes"; Section 13.1
  format-agnostic reference resolution, in which "XML comment content-token paths never
  enter the simple alias index; comments have no scalar payload and are invisible to
  format-agnostic reference resolution". Section 3.2 as a correction of insecure XML
  handling only obliquely — the correction here is the neighbouring §11.5/§13.1 rule that
  a comment is a document node, not a candidate for reference matching.
- Legacy observation: 2.4.0's XML reader flattened every child element into an attribute
  of the containing element, so the two `<target>` and `<use>` elements arrived at the
  overlay as `@target=""` and `@use=""` with their text lost. Whatever the baseline did or
  did not do with the intervening XML comment for alias resolution is invisible under this
  reader shape, because there is no `<use>` element for a reference to survive into.
- Clean behavior: the input's three ordered children are: `<target>value1</target>`, an
  XML comment `<!--annotation-->`, and `<use>${r.target}</use>`. §11.5 keeps the comment
  as a document node in its ordered position. §13.1 resolves the reference `${r.target}`
  through the simple alias index; the comment content-token path never enters that
  index, so it does not compete with `r.target` and the reference resolves unambiguously
  to the string `value1`. §19.5 emits all three children in source order and the comment
  survives as `<!--annotation-->`.
- **The fixture's discrimination is weak in one direction.** An XML comment has no scalar
  payload — §13.1 says so explicitly, "comments have no scalar payload and are invisible
  to format-agnostic reference resolution" — and a wrong implementation that admitted
  comments to the alias index would find no scalar to alias to at the comment path. So
  either implementation resolves `${r.target}` to `value1` for this input; the case pins
  the correct behaviour of the two clauses rather than trapping the specific defect of
  admitting comments to the index. A separate fixture whose scheme creates a genuine
  alias competition between an element name and a comment position would be needed to
  trap it, and this preview does not carry one.
- The difference is intentional: an XML comment is deliberately not a value, and using a
  comment as a candidate for reference resolution would let a document author change what
  a live value resolves to by adding or removing an annotation. The 3.0 reader also
  preserves the child elements a legacy attribute-collapsing reader loses; the correction
  makes the reference contract meaningful at all, because a run that lost its scalars
  before the reference pass would fail the reference pass for entirely unrelated reasons.
