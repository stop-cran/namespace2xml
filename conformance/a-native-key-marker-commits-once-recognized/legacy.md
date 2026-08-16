# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits 0 and writes `a.properties` containing the two
  records `@=v` and `#01=v`. The case expects exit 1 and two `PARSE001` diagnostics, one per
  offending key.
- Contract: Section 9.1 and Section 10.4 marker recognition in native keys, which "commits
  exactly as in Section 8.2: a key that begins like a marker without completing the
  production is `PARSE001`". §3.2 correction against behaviour that produces output no
  reader can consume.
- Legacy observation: 2.4.0 treated every native key as opaque literal text, so `@` and
  `#01` passed through untouched, and its namespace writer applied no escaping on the way
  out. The result is worse than a wrong value. `#01=v` is a **comment** to the namespace
  reader, so feeding `a.properties` back to 2.4.0 silently discards that entry, and `@=v`
  is a record whose name is a bare marker. The tool produced a file it cannot read back,
  with no diagnostic, and reported success.
- Clean behavior: §9.1 gives a key beginning with an unescaped `@`, `#`, or `Q{` the typed
  component that marker introduces, and commits to that reading. `@` alone completes no
  attribute production, because an attribute marker introduces a name; `#01` completes no
  content production, because a content ordering value "is written without leading zeros".
  Neither can be silently demoted to ordinary text — that is exactly the demotion that
  produced 2.4.0's unreadable file — so each is blocking `PARSE001` naming the escape that
  expresses the literal intent, `\@` and `\#`.
- The difference is intentional: the escape hatch exists and is one character, so refusing
  the ambiguous spelling costs an author nothing and buys the guarantee that a key which
  parses means what it says. The neighbouring
  `xml-typed-components-recognized-and-an-escaped-json-key-stays-literal` case pins the
  accepting side of the same rule.
