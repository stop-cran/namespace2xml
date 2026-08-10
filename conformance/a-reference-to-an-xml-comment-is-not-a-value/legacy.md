# Legacy differential

- namespace2xml 2.4.0: **agrees** on the observable.
- Contract: Section 13.1 comment invisibility and `REFERENCE002`; Section 13.3 non-scalar
  references and `REFERENCE005`; Section 11.5 comment retention; Section 26 item 9.
- Legacy observation: the baseline exits `1` with no output tree, reporting
  `Reference OutputRoot.r.a.#1 was not found` and `Reference OutputRoot.r.a.#9 was not found`.
  Both references are "not found", and for the baseline that is literally true: 2.4.0 dropped XML
  comments at read time, so `<a>t<!-- note --></a>` reduced to the scalar `r.a=t` and neither `#1`
  nor `#9` addressed anything. The measurement records no divergence in the observable.
- Clean behavior: Section 11.5 retains the comment as an ordered content node, so `r.a.#1` names
  something. Section 13.1 says comments "have no scalar payload and are invisible to
  format-agnostic reference resolution" and that "a canonical reference directly addressing an XML
  comment path fails as a non-scalar reference", which Section 22 codes as `REFERENCE005` against
  Section 13.3. `r.a.#9` addresses nothing at all, which Section 13.1 makes the missing-reference
  `REFERENCE002`. Section 24 orders the two by source position, so `REFERENCE005` precedes
  `REFERENCE002`.
- Why the observable agreement is compatible-looking but not sufficient: the exit code and the
  empty output tree are the same, so a verdict scored on those alone cannot see the difference.
  What differs is what the two runs *know*. The baseline gives one answer because it had discarded
  the comment; this build gives two answers because it kept it. The pair of references in this case
  is deliberately identical in shape — same node, same `#n` spelling, one digit apart — so the only
  thing that can separate the codes is whether the model still holds the comment. A build that
  reported `REFERENCE002` for both would satisfy the exit code, the output tree and the legacy
  verdict, and would be wrong.

## Not asserted

- The diagnostic messages. `expected-diagnostics.json` scores the code, phase, source position,
  path and anchor; the prose that names an XML comment is not compared.
