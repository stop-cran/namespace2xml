# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.3; Section 10.1; Section 19.4; Section 24.
- Legacy observation: YAML was neither read nor written, so no writer had to decide which strings a
  YAML reader would give back unchanged.
- Clean behavior: Section 3.3 requires a same-format round trip to preserve "data structure" and
  "scalar type", which constrains the spelling a Section 19.4 writer may choose beyond the one
  explicit `RestrictedYaml1` rule. A reader detects a literal block scalar's indentation from its
  first non-empty line, so a value whose first content line is indented is quoted rather than
  blocked; a line reading `...` is the document-end marker, so that string is quoted; U+FEFF is a
  byte order mark that Section 24 forbids and a reader discards, so it is escaped; and a
  supplementary character is one scalar value, so it is written as itself rather than as two
  surrogate escapes that would spell a different string. U+2028 and U+2029 are line breaks to a
  YAML reader exactly as LF is, and are outside both control ranges, so they are escaped rather
  than written as themselves — written literally they end the line and the file no longer parses.
  A value ending in a blank line would need a keep-chomped block, whose last two bytes are line
  breaks; Section 24 requires a text output to "end with exactly one LF", so the block spelling is
  declined for such a value everywhere rather than only where it happens to fall last.
- The difference is intentional: Section 19.4's single explicit rule is about *meaning* — which
  strings would resolve to a non-string kind. These values resolve to strings correctly and are
  still lost or altered under a naive spelling, so the writer applies the syntactic rules the round
  trip requires as well as the semantic one the section names.