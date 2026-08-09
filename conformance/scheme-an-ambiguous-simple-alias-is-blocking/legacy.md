# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits `0` and writes
  `<?xml version="1.0" encoding="utf-8"?>\n<r>\n  <a x="" />\n  <b x="" />\n</r>` with CRLF endings
  and no trailing newline, where 3.0 refuses the run with `SCHEME002` and writes nothing.
  **verified** — measured against the Appendix C.6 pinned 2.4.0 package.
- Contract: Section 15.2 — "if ordinary and XML components make that alias ambiguous at a matched
  location, selector expansion at pipeline step 13 emits blocking `SCHEME002` and lists the
  canonical alternatives".
- Legacy observation: the input is `<r><a x="1"><x>2</x></a><b x="3"><x>4</x></b></r>`. Each of `a`
  and `b` carries an attribute `x` and a child element `x`, and the single wildcard directive
  `r.*.x.type=string` is ambiguous at both of them. 2.4.0 reported nothing and returned success. It
  wrote `<a x="" />` and `<b x="" />`: all four values — the attributes' `1` and `3` and the
  elements' `2` and `4` — were gone, and both child elements were gone with them. The directive's
  own effect is not visible either way, because whichever component it reached no longer had a
  value to type.
- Clean behavior: Section 11.4 makes `a.@x` and `a.x` different components, so the unmarked scheme
  component `x` has two canonical alternatives at each matched location, and Section 15.2 makes that
  blocking rather than a choice for the tool. `SCHEME002` names both — `r.a.@x` and `r.a.x` — and
  the run writes no output. Marking the component resolves it in either direction: `r.*.@x.type`
  binds the attributes and `r.*.Q{}x.type` binds the elements, and neither is ambiguous.
- One declaration, two ambiguous locations, one diagnostic. Section 22 scopes `SCHEME002` to the
  declaration, not to the expansion, so the author is told once about the rule they wrote rather
  than once per place it happened to land. The reported location is the earlier of the two.
- The difference is intentional. This is the ambiguity that the alias in Section 15.2 makes
  possible, and refusing it is what keeps the affordance safe: the alternative is to pick one, which
  is what 2.4.0 effectively did by having no second component to pick — and the document lost data
  that no diagnostic mentioned. An error the author can fix in one keystroke is a better trade than
  a file that is quietly wrong.
