# Legacy differential

- namespace2xml 2.4.0: **crashes**. It terminates with `System.Xml.XmlException: Name
  cannot begin with the '0' character, hexadecimal value 0x30.` from
  `Namespace2Xml.Formatters.XmlFormatter.ToXmlValueSingle`, and exits 134 on Linux (the
  runtime's SIGABRT convention). A zero-length `main.xml` is left behind in the output
  directory before the process aborts. The case expects exit 0 and two files, `main.xml`
  and `servers.xml`, with the sequence contents of `cfg.port` and `cfg.server` rendered
  as repeated sibling elements.
- Contract: Section 19.5 XML sequence projection — "a sequence-valued mapping child
  therefore renders as repeated sibling elements whose expanded name is the sequence path's
  final element component"; §3.2 corrections against behaviour "caused by unhandled
  user-input exceptions" and against outputs "opened before the complete output plan was
  validated" (the zero-length file is a partial output that a §15.4 pre-publication check
  would prevent).
- Legacy observation: 2.4.0 had no §19.5 XML sequence projection. It walked the common
  model naively into the XML writer, so a sequence node's numeric ordering-value children
  reached `ToXmlValueSingle` as element name strings — the first is `0`, which is not a
  legal XML NCName because NCNames may not begin with a digit. `XmlException` propagated
  through the writer, and the file it had already opened for writing was left as an empty
  file rather than deleted. The `servers.xml` output was never reached because the process
  aborted on `main.xml`.
- Clean behavior: §19.5 states that "a sequence-valued mapping child therefore renders as
  repeated sibling elements whose expanded name is the sequence path's final element
  component". `cfg.port` therefore renders as `<port>8080</port><port>8081</port>` under
  `<cfg>`, and `cfg.server` — a sequence of records — renders as two repeated `<server>`
  elements each containing `<name>` and `<host>`. §19.5's rule that "an output view whose
  document root is itself a sequence requires `root` with at least two element
  components" is what makes `servers.xml` legal: `cfg.server.root=servers.server` creates
  the wrapping `<servers>` and names each item `<server>`.
- The difference is intentional: the whole point of §19.5's sequence projection rule is
  that XML has no anonymous sequence node, so a sequence must be expressed by *repetition*
  of a named element rather than by naming each item after its ordering value. Writing the
  index `0` as an element name is illegal XML and is refused by every conforming reader,
  so an implementation that emits it produces a document nothing can consume. The 3.0
  correction also fixes the partial-file symptom: no output is opened until §15.4's
  validation phase completes, so a run that would have crashed never touches its
  destination and its caller can rerun without cleaning up stale bytes.
