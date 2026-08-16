# Legacy differential

- namespace2xml 2.4.0: **fails**. It terminates with an unhandled
  `System.Xml.XmlException: Name cannot begin with the '@' character, hexadecimal value 0x40.`,
  exit `-532462766` (`0xE0434352`, an unhandled managed exception), and writes nothing. The
  output-options concept did not exist in 2.4.0, so `cfg.xmloutputoptions` is an unrecognized
  directive that is ignored without a diagnostic and whose contradictory content is never
  inspected — but the run does not survive to render, because 2.4.0's XML writer has no Section
  11.4 marker syntax and hands the literal `@` name to `XmlWriter`. The contradiction this case is
  about is therefore not something the baseline can be observed to have an opinion on.
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
