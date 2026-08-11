# Legacy differential

- namespace2xml 2.4.0: **differs, and silently**. It exits 0, writes **nothing at all**, and
  reports `Success! Exiting...`. The scheme file is opened — the run logs
  `Reading input ...s.xml` — and contributes no directives, so the `app` output instance the
  author asked for is never created and no diagnostic says so.
- Contract: Section 15's scheme file extensions and `PARSE001`'s Section 22 cardinality of once
  per failing source.
- Legacy observation: two controls locate the behaviour precisely. The identical XML content
  saved as `s.txt` is handed to the namespace parser and reports
  `Error parsing input: Unexpected end of input reached, file: s.txt, line: 1, column: 38` at
  exit 1, so the silence is caused by the `.xml` extension rather than by the content. A working
  `app.output=namespace` scheme over the same profile writes `app.properties` containing
  `name=example` and `x=1`, so the missing output is not an artifact of the invocation.

  | Scheme file | 2.4.0 result |
  |---|---|
  | `s.xml` holding `<app><output>namespace</output></app>` | exit 0, nothing written, no diagnostic |
  | `s.txt` holding the same XML text | parse error against the namespace grammar, exit 1 |
  | `s.txt` holding `app.output=namespace` | exit 0, writes `app.properties` |

  So XML scheme files were never supported. The `.xml` extension selected a reader that produced
  no directives, and the run reported success for work it did not do.
- Clean behavior: Section 15 gives scheme files the `.json`, `.yaml`, and `.yml` extensions and
  excludes `.xml` by name, because it defines no projection from an XML document to a qualified
  directive path. The file is rejected as `PARSE001` against Section 15 in the scheme phase
  before it is read, once per failing source, and the run exits 1 having written nothing.
- The difference is intentional, and the choice of anchor is the substance of it. Reading the
  file as a namespace profile — the `s.txt` control above — reports the right *code* against the
  wrong *rule*: it names Section 8.1 at a document whose author never wrote it to that contract,
  and advises adding an `=` to syntax that is already correct for the contract they were aiming
  at. Naming Section 15 says the true thing, which is that the file's format is not one a scheme
  may use.
- Section 15 excluding XML is not a narrowing of 2.4.0: it makes an existing silent no-op
  legible. Support could be added later without breaking anyone, whereas shipping a guessed
  projection and correcting it afterwards could not.