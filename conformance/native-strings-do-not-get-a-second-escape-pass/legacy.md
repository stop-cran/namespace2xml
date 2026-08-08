# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes the same content to both outputs — `"native":
  "\\\\hit"` in `ns.json` and in `nj.json` alike — and exits 0. The case expects the two to be
  different: `ns.json` carries `"\\hit"` and `nj.json` carries `"\\${nj.target}"`. The baseline is
  wrong twice over. It emits two literal backslashes where the source text `\\` specifies one, and
  it resolves a reference inside a decoded native string where Appendix A.5 leaves the text alone.
- Contract: Appendix A.3, Appendix A.5, and the worked example at the end of Appendix A.3.
  Section 3.2 as a correction.
- Legacy observation: 2.4.0 ran one escape and reference pass over every value it held, whatever
  the value's origin. A string that arrived already decoded from a JSON document was scanned again
  by the rules written for namespace-file text, so `\\${nj.target}` — which the JSON reader had
  already turned into the characters `\`, `$`, `{`, … — was re-read as an escape followed by a
  reference. Emitted text was rescanned for the same reason, which is what doubles the backslash.
- Clean behavior: the two grammars are deliberately different and the specification states both.
  In a namespace value (Appendix A.3) `\\` is a recognized escape meaning one literal backslash,
  and a `${…}` following it is an ordinary reference, so `ns` resolves to a backslash plus `hit`.
  In a decoded native string (Appendix A.5) only `\*` and `\${` are escapes; any other backslash
  "emits itself and consumes no following scalar", so the `\` stands alone and the `${nj.target}`
  after it is literal text. Appendix A.3 gives exactly this pair as its worked example. In neither
  case is emitted text rescanned.
- The output is JSON because Section 6.4.3 escapes `"` and `\` with a backslash, which renders one
  literal backslash as `\\` and makes the count unambiguous in the fixture file. A namespace output
  would have re-encoded the value and hidden the very distinction under test.
- The difference is intentional: applying namespace-file escaping to data that a JSON or YAML
  reader has already decoded corrupts any value that legitimately contains a backslash or a dollar
  sign — a Windows path or a shell template, say — and does so silently, since the corrupted text
  is still valid output.
