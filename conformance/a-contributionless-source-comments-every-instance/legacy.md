# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes both comment lines into `p.properties` and none into
  `q.properties`, so the note survives in one of the two documents it describes.
- Contract: Section 8.5, "a document-leading comment is bound to no path ... and it is emitted in
  every output instance the run produces"; Section 26 item 13.
- Legacy observation: the comment-only source contributes nothing, so its run is document-leading
  and belongs to no path. 2.4.0 attaches it to whichever instance is rendered first and drops it
  from the rest, silently — nothing in the run says a second document was expected to carry it.
- Clean behavior: both documents carry both lines. An output instance is a standalone file that has
  to be readable on its own, and the specification cannot name a winner among instances whose order
  is a rendering detail; announcing the omission from the others would still leave those documents
  missing a note whose whole purpose is to sit next to the settings it explains.
- The case exists because it is the only one in the corpus with **two** output instances and an
  ownerless comment. Every other comment fixture renders a single document, where "the first
  instance" and "every instance" are the same file and the rule is unobservable.
- The invocation also records a second divergence in passing: 2.4.0 rejects a repeated `-i` with
  "Option 'i, input' is defined multiple times" and requires `-i a b`, while Section 6.2 accepts
  both forms on a list option.
