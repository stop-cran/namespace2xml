# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 11.4's rule that "a sequence rendered as repeated sibling elements emits its
  items in canonical index order", that where content tokens and canonical index order disagree
  "canonical index order governs, and the item takes the position of the latest item preceding
  it", and that "content that is not an item of that sequence keeps the position its own token
  gives it". Section 11.4 also fixes the repeated-child address as
  `parent.child.<ordering-value>`.
- Legacy observation: the baseline exits 0 having written `<p c="" b="" />` and a two-line
  `q.txt` reading `c=` and `b=`. Every value is gone: `one`, `two`, `three` and `mid` appear in
  neither destination, the repeated children have collapsed to a single name, and both names have
  become empty attributes of the document element.
- Clean behavior: the XML destination emits `one`, `mid`, `two`, `three` in that order, and the
  namespace destination reports `b.0=one`, `b.1=two`, `b.2=three`, `c=mid`. Two `WARN004`s name
  `p.b` and `q.b`, where two sources each contribute a native implicit sequence.
- Why the difference is intentional: 2.4.0 has no repeated-child model at all, so a name that
  occurs twice in one document is not a sequence but a key written twice, and the second write
  wins with nothing left to write. The divergence is therefore not about ordering — it is about
  whether the data survives. The fixture nevertheless pins ordering, because that is what the
  clean implementation had to settle: the two destinations describe one model, and an XML stream
  whose elements ran in a different relative order than the addresses the namespace view reports
  for them would make `a.b.2` name one element for a reference and a different one for a reader.