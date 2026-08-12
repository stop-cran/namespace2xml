# Legacy differential

- namespace2xml 2.4.0: **differs**. The entire output-options concept did not exist in 2.4.0, so
  `cfg.xmloutputoptions` was an unrecognized directive, was ignored without a diagnostic, and its
  contradictory content was never inspected. The baseline exits 0 and writes a file.
- Contract: Section 16.9's contradictory pairs and the `SCHEME001` cardinality of Section 22.
- Clean behavior: Section 16.9 lists `NoIndent` and `NewLineOnAttributes` among the four XML
  contradictory pairs, so naming both in one declaration is `SCHEME001`. Section 22 counts
  `SCHEME001` "once per declaration", so exactly one error is emitted, the run exits 1, and
  Section 21.2's validation gate means no output is written.
- Why the pair is contradictory rather than a combination: `NoIndent` "inserts no formatting
  whitespace" and `NewLineOnAttributes` requires a line break and two spaces before every
  attribute. On any element carrying an attribute the two flags demand different bytes, so no
  serialization satisfies both.
- Why refusal rather than precedence: the two available precedence readings each silently discard
  a flag the author wrote down. A discarded flag produces no diagnostic and no visible change, so
  an author who chose the wrong one of the two readings has nothing to observe. Refusing names
  both flags in the message and fails before any input is opened.
- The input carries both an attribute (`cfg.@a`) and an element child (`cfg.b`) so that the case
  would still be able to distinguish the two precedence readings if the pair were ever made legal;
  as specified it is rejected before serialization and neither reading is reachable.
