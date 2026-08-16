# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits 0 and loses comments in both destinations, in two
  different ways.
  - `cfg.ini` carries no comment at all, although `HashComments` is selected. All three — the
    document-leading note, the note owned by the section's first key, and the closing note — are
    gone. It also writes a blank line after the global key and another at end of file, which the
    Section 19.6 layout does not describe.
  - `cfg.properties` keeps the document-leading and owned comments but **drops the closing note**,
    so the baseline loses a document-trailing comment even in a format that plainly represents one.
- Contract: Section 20 INI comment emission and placement — document-leading comments precede the
  first global key or section, a comment owned by a section's first key follows the section header,
  and document-trailing comments are emitted at end of file. Section 16.9 `inioutputoptions`.
- The case renders the same input to INI and namespace so the two can be compared directly. The
  point of the pairing is that the three placements agree: choosing INI must not silently move or
  delete a note. That is what both baseline files fail, in opposite directions.
- Issue #87 asked only where a document-trailing comment belongs in INI, and the measurement
  behind it — that the note vanished — turned out to be the smaller half. 3.0 before this change
  dropped every comment INI could not attach to an entry, document-leading included, and said
  nothing; the `WARN003` that announces discarded comments was raised only when some surviving
  entry still owned one.
