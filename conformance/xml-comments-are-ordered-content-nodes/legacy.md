# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 11.5, 11.4, 17.4, 19.5, 19.3, 4.5, and 3.
- Legacy observation: XML comments were not represented at all. A comment in an input document was
  read and dropped, so no output could re-emit one and no scheme directive could name one. Nothing
  distinguished an XML-to-XML run that preserved a document from one that silently deleted its
  annotations.
- Clean behavior: Section 11.5 retains comments "as ordered comment nodes", and does not force them
  "into a 'leading comment for the next value' representation because a comment may occur between
  mixed-content nodes or after the final child". This case supplies both of those positions. A
  comment therefore takes an ordinary Section 11.4 content token — `mixed` addresses its text at
  `#0`, its comment at `#1`, and its child element at `#2` — and Section 17.4's "comments alone do
  not make a parent mixed-content" keeps `only` addressing its child as `only.b` while its trailing
  comment sits at `only.#1`. Section 19.5 "emits retained XML comments", so `r.xml` reproduces both
  in place.
- The JSON half is the other side of the same rule. Section 19.3 "renders comments nowhere", so the
  comment nodes are omitted from `r.json` entirely rather than becoming string values at `#1`, and
  Section 3 requires "one summarized warning per output file and feature category" — one `WARN003`
  for the file, not one per comment. Section 4.5's bound-comment channel is for "a non-XML comment"
  and Section 4.5's own final bullet holds these out of it, which is why a comment cannot travel to
  a neighbouring value on the way out.
- The difference is intentional: a configuration transformer that reads a document and writes it
  back must not delete what it cannot interpret. Where a format genuinely cannot carry a comment,
  Section 3 requires the loss to be reported rather than silent.