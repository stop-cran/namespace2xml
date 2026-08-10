# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits `0` and writes the same two records with the same two
  keys, but orders them `b` then `c` where this case expects `c` then `b`. Content is identical;
  only sibling order differs. CRLF-terminated, under the Section 24 divergence.
- Contract: Section 10.4 for extraction, Section 5.3 for where the generated entry sorts.
- Why the expectation is `c` first. `args.txt` lists `template.yaml` before `data.yaml`, so the
  wildcard rule is a Section 4.7 CLI source ordinal of 1 and the concrete `b` is 2. Section 5.3
  says generated entries "inherit the rule's precedence position", Section 4.7 makes that ordinal
  the first component of the stable ordering key, and Section 5.2 states that "the position mark is
  the Section 4.7 stable ordering key". The generated `c` therefore precedes `b`.
- **Section 10.4's own worked example prints the opposite**, while introducing the template as the
  first input. That contradiction is filed as
  [#73](https://github.com/stop-cran/namespace2xml/issues/73). 2.4.0 matches the printed example;
  3.0 matches the rule. This expectation is authored from Section 5.3 rather than from Section
  10.4's rendering, and it flips if #73 is decided the other way — which is precisely why the
  companion case
  `a-yaml-wildcard-key-enriches-each-record-of-an-earlier-file` exists: it fixes the same
  enrichment under an argument order where both readings agree, so a #73 decision cannot silently
  take the capability with it.
- Legacy observation: 2.4.0 produced `b` first for **both** argument orders, so its ordering is
  insensitive to where the template file appears. Under Section 5.3 the order is a function of
  source position, so the two orders must differ. The legacy behaviour is not a different rule so
  much as no rule.