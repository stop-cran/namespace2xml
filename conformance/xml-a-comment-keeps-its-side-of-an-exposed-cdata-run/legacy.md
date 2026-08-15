# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `<cfg before="" after="" />` — both CDATA sections and
  both comments are gone and the document element is empty and self-closing — with CRLF and no final
  newline. Exit 0, nothing reported. **verified** — measured against the Appendix C.6 pinned 2.4.0
  package on the .NET 9 runtime it targets, under this case's own `args.txt`.
- Contract: Section 19.5's placement of a scalar exposed at an element path; Section 11.4's
  content-token ordering; Section 11.6's preservation of imported CDATA.
- Legacy observation: XML output was not a rendering of the input document. A CDATA section became
  an empty attribute, both comments vanished, and no diagnostic distinguished the result from a
  faithful copy.
- Clean behavior: `before` holds the comment at content token 0 and the CDATA run at 1, so the
  comment is written first; `after` holds them the other way round and so is written the other way
  round. Section 11.6 keeps both runs spelled as CDATA, so the ordering rule is exercised on a
  payload whose spelling had to survive the same journey as its position.
- Why the case is here: the ordering value of an exposed run travels on the scalar payload, and
  `AsCdata` builds a new payload. A change that rebuilds a payload without carrying the position
  across would leave text placement correct and CDATA placement wrong, which no text-only case can
  see. The two elements are mirror images so that a rule which merely wrote the value first, or
  merely wrote it last, fails one of them whichever it picked.
