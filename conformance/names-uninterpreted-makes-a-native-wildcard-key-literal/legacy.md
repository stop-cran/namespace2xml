# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `d.c=XXX` and `d.b=1`, having expanded the template
  anyway: `substitute=None` had no effect on a native mapping key. This case expects `\*.c=XXX` and
  `d.b=1` — the key kept as a literal name and the sibling record left alone. CRLF-terminated,
  under the Section 24 divergence.
- Contract: Section 16.7, where `None` is "names interpreted: no"; Section 15.1 step 6, which
  matches a `substitute` pattern against "an entry's declared pre-expansion path"; Section 21 for
  the escape on the way out.
- The case exists because `\*` and `substitute=None` reach the same place by different routes, and
  only one of them was covered. `a-backslash-asterisk-in-a-native-key-is-a-literal-asterisk` proves
  the escape; this proves the directive. A mutation that stopped literalizing the key on the
  directive path survived the whole corpus until this case was added, because every other native
  case leaves names interpreted and never enters that branch.
- Literalizing is what stops the token rather than merely ignoring it. A `*` that is not
  interpreted must cease to be a token at the point it is read, because a concrete path carrying a
  live token is read back as a pattern by every later matcher — the wildcard evaluator, an output
  selector, a reference. Turning off interpretation and leaving the character as syntax is the one
  outcome that is wrong under every reading of Section 16.7.
- Legacy observation: 2.4.0 supported `substitute` for namespace input only, so a native key was
  never subject to it. That is why the mode is invisible here rather than partially applied.