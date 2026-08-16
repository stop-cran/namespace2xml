# Legacy differential

- namespace2xml 2.4.0: **fails**. Under this case's own arguments it never reads an input at all:
  its parser refuses the repeated `-i` with `Option 'i, input' is defined multiple times`, prints
  its usage banner, exits `1` and writes no file. Section 6.2 accepts both `-i a b` and
  `-i a -i b` on a list option; 2.4.0 accepts only the first, so the invocation this case is
  written in is itself outside what the baseline can express.
- Contract: Section 8.5, "a document-leading comment is bound to no path ... and it is emitted in
  every output instance the run produces"; Section 6.2 for repeated list options; Section 26 item 13.
- Legacy observation, from a reduced probe rather than from the differential lane: rewriting the
  invocation into the single-flag form 2.4.0 does accept reaches the comment behaviour underneath.
  The comment-only source contributes nothing, so its run is document-leading and belongs to no
  path. 2.4.0 attaches it to whichever instance is rendered first and drops it from the rest,
  silently — nothing in the run says a second document was expected to carry it. That measurement
  is what the clean behaviour below is contrasted against; it is not what the lane observes here,
  and the verdict above is.
- Clean behavior: both documents carry both lines. An output instance is a standalone file that has
  to be readable on its own, and the specification cannot name a winner among instances whose order
  is a rendering detail; announcing the omission from the others would still leave those documents
  missing a note whose whole purpose is to sit next to the settings it explains.
- The case exists because it is the only one in the corpus with **two** output instances and an
  ownerless comment. Every other comment fixture renders a single document, where "the first
  instance" and "every instance" are the same file and the rule is unobservable.
- The invocation also records a second divergence in passing: 2.4.0 rejects a repeated `-i` with
  "Option 'i, input' is defined multiple times" and requires `-i a b`, while Section 6.2 accepts
  both forms on a list option. That refusal is what the verdict above measures; it fires before any
  input is read, so under this case's arguments the comment behaviour is never reached.
