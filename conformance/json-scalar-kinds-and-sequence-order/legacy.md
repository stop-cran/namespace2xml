# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 9.1, 18, 19.6, and 5.4.
- Legacy observation: JSON numbers were read through a binary floating-point type, so precision
  beyond a `double` was lost silently and a number's written form did not decide its kind. Property
  names were split on `.`, so one JSON key could become several namespace parts.
- Clean behavior: a JSON number's *lexical* form decides its kind, so `1e0` is a decimal that
  Section 18 renders `1.0` while `1` is an integer rendered `1`, and an integer wider than any
  binary floating-point type survives exactly. `-0` has no fraction or exponent, so it is an
  integer, and integers have no negative zero. Each property name is exactly one literal part, so
  `a.b` stays one INI key rather than becoming a section.
- The difference is intentional: Section 9.1 fixes both rules, and either legacy behavior loses
  information the source contained.
