# Legacy differential

- namespace2xml 2.4.0: **differs**. It reads `.json` inputs, so the case can be posed to it, but it
  emits YAML through its serialization library's default settings and has no `RestrictedYaml1`.
  Style selection is therefore whatever that library prefers, which is precisely the decision
  Section 19.4 now takes away from the library.
- Contract: Section 19.4's "YAML output bytes".
- Clean behavior: exactly one of four styles is chosen for every scalar, by first match — literal
  block, then double-quoted, then single-quoted, then plain. Each key in this case selects a
  different rule, and its name says which.
- Why the selection is specified at all: YAML offers more spellings of one string than any other
  supported format, and every one of them is valid. A writer choosing among them by its own taste
  satisfies YAML and breaks Section 24, which asks two conforming implementations for identical
  bytes. Nothing else in the corpus distinguishes a legal spelling from the specified one.
- Why `indicator_mid`, `colon_mid`, `hash_mid` and `dot_first` are present: they are the
  negative half of each rule. A conservative implementation that quotes everything round-trips
  perfectly and produces different bytes, so the case has to fail on over-quoting as readily as on
  under-quoting.
- Why the strictness beyond YAML's own productions is pinned: YAML admits `-`, `?` and `:` as the
  first character of a plain scalar when the next character is not a space, and refuses a flow
  indicator only in flow context. This specification refuses both positionally. An implementation
  applying the productions literally emits `indicator_first` and `flow_mid` plain and fails here,
  which is the intent — the value's spelling must not depend on where it is written.
- Why `merge`, the `<<` key and `stays` are present: all three are portably typed under Section
  19.4, so all three are quoted. An earlier revision of that section wrote a *value* of `<<` plain,
  reasoning that nothing resolves a merge key in value position, and this case was authored to pin
  that reasoning. It was wrong: a YAML 1.1 reader resolves the merge tag in either position, and a
  reader with no constructor for it abandons the whole document rather than that one node. The case
  now pins the corrected rule, and the episode is why the specification spells key and value by
  exactly the same conditions — an exception granted to one position is an exception nothing in a
  byte-comparing corpus can see, because the bytes were stable and wrong together.
- Why `nbsp` is present: U+00A0 is not YAML whitespace, so it neither forces quoting nor is
  escaped. It distinguishes "escape what YAML cannot carry" from "escape what is not ASCII".
- `del` and `ctl` pin the uppercase hexadecimal digits of `\uXXXX`, and `cr` pins that CR takes
  the short form `\r` while a control with no short form does not.
