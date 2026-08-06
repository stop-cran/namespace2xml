# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 10.1, 9.1, 18, 19.6, and 5.4.
- Legacy observation: YAML was read through the host library's own schema, so YAML 1.1 spellings
  such as `yes` became Booleans, a plain `1.` became a number, and numbers were resolved through a
  binary floating-point type that lost precision and let the runtime rather than the written form
  decide a value's kind. Property names were split on `.`, so one YAML key could become several
  namespace parts.
- Clean behavior: Section 10.1 fixes the schema as `RestrictedYaml1` "rather than an underlying
  library's advertised YAML 1.1 or 1.2 mode". Only `true` and `false` are Booleans, resolved
  case-insensitively, so `tRuE` is a Boolean and `yes` is the string it is written as. Only the
  exact spellings `null`, `Null`, `NULL`, `~` and an empty plain scalar are null, so `nULL` is a
  string. Only JSON-compatible numbers are numbers, so `+1`, `.5` and `1.` stay strings, while
  Section 9.1's lexical rule makes `1e0` a decimal that Section 18 renders `1.0` and `-0` an
  integer with no negative zero. Each key is exactly one literal part, so `a.b` stays one INI key
  rather than becoming a section.
- The difference is intentional: Section 10.1 makes the subset normative precisely so the answer
  does not depend on which YAML library is linked, and every legacy behavior above loses
  information the source contained.
