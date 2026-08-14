# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `<cfg lone="" />` — the value and both comments are
  gone, the element is empty and self-closing — with CRLF and no final newline. Exit 0, nothing
  reported. **verified** — measured against the Appendix C.6 pinned 2.4.0 package on the .NET 9
  runtime it targets.
- Contract: Section 11.4's content-token ordering, and specifically its rule that an element
  exposing a lone text run as the scalar at the element path leaves that run unaddressable while
  the index it would have occupied is "consumed rather than reassigned"; Section 15.2's `WARN009`
  for a directive that matches no path.
- Legacy observation: XML output was not a rendering of the input document. The text became an
  empty attribute, both comments vanished, and no diagnostic distinguished the result from a
  faithful copy — so neither the addressing this case pins nor the directive written against it
  had anything to act on.
- Clean behavior: the comments occupy `cfg.lone.#0` and `cfg.lone.#2`, so `cfg.lone.#2.type=ignore`
  removes the second one and the first survives. `cfg.lone.#1` is the index the exposed run
  consumed; it names nothing, so the directive written against it emits one `WARN009` and changes
  no output. The value is written ahead of the surviving comment under the Section 19.5 limit.
- Why the difference is intentional: 2.4.0 had no content-token model at all, so the question this
  case asks could not be put to it. The addressing exists so that a comment can be selected without
  naming its text, and the gap is the observable consequence of Section 11.4's one exception. A run
  of indices with no gap would mean the exposed run had been renumbered out of existence, and a
  directive that silently matched the wrong comment is exactly what the `WARN009` prevents.