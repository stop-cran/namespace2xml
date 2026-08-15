# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes
  `<cfg ctl="" keptc="9" kepte="8">` with a lone `<kepte x="" />` child, on CRLF and with no final
  newline. Every scalar is hoisted into an attribute of the root, `1` and `3` are replaced by empty
  attribute values, the comments are gone, and `kepte` appears twice — once as an attribute holding
  the overlay value and once as an element holding the emptied child. Exit 0, nothing reported.
  **verified** — measured against the Appendix C.6 pinned 2.4.0 package.
- Contract: Section 19.5's "An overlay payload plus element children is represented as text or CDATA
  plus children when the effective XML type permits mixed content", its rule that "a payload
  carrying no ordering value is written first, ahead of every comment node the element carries",
  and Section 4.4's restriction of the exclusive-shape contest to destinations that require one.
- Legacy observation: XML output was not a rendering of the input document. A value and the content
  it was written beside could not both survive, and no diagnostic distinguished the result from a
  faithful copy.
- Clean behavior: XML holds a scalar and its siblings together, so a later scalar landing on a path
  does not evict what is already there. `keptc` takes the overlay value and keeps its comment;
  `kepte` takes the overlay value and keeps its element child; both place the incoming payload first
  because it carries no ordering value of its own. `ctl` receives no overlay and is unchanged, so a
  fix that merely stopped discarding content cannot pass by discarding nothing anywhere.
- Why the case is here: the loss it guards is silent and total. The overlay entry looks like an
  ordinary override, the output is well-formed, the exit code is 0 and the diagnostic stream is
  empty, so nothing in the run says that a comment or a child element was destroyed. It does not
  even need the value to change: `cfg.keptc=9` over a source `2` and over a source `9` lost the
  comment alike, because the trigger is the arrival of a later scalar contribution and not a
  difference in what it contributes. Section 4.4's contest is written "when a destination requires
  one exclusive shape", and XML is not such a destination for a payload against a container; a
  projection that applies it anyway passes every corpus case whose overridden paths happen to carry
  no siblings.