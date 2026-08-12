# Legacy differential

- namespace2xml 2.4.0: **crashes**. The baseline has no diagnostic stream, no stable code, and no
  cardinality rule, so there is nothing here to compare against; `expected-diagnostics.json` is the
  whole point of the case.
- Contract: Section 12.2; Appendix B; Section 22.
- Clean behavior: `a.*[0].copy=lit*[9]` substitutes a capture its own name never defines. Section
  12.2 says "an undefined capture outside a reference is an error", and Appendix B maps that
  condition to `WILDCARD001` by scoping the code to a capture "outside a reference". Three names
  match the template and the stream carries **one** diagnostic, because the fault is a property of
  the rule as written and Section 22 counts `WILDCARD001` "once per rule".
- The count is the assertion. An implementation that evaluated the rule per item would report the
  same code three times and satisfy every sentence about *which* code applies, so a case pinning the
  code alone would not notice. The input carries three matching names for that reason.
- The member set is the second assertion. Section 22 gives `path` to a condition that "concerns one
  overlay node or one projected output key", and a template's declared name is neither -- it names a
  rule, which is what the `rule` member exists for: "an array of Appendix A canonical wildcard-rule
  names, holding one element per rule the condition holds responsible". The condition also supplies
  `line` without `column`, because Section 22 says a condition "raised over a compiled declaration or
  a wildcard rule rather than over the text that produced it, supplies `line` without `column`".
- The companion case `an-unbound-capture-inside-a-reference-names-each-owning-value` writes the same
  mistake inside a reference and gets a different code, a different phase and a different count.
  Between them the two cases fix the division that Section 12.2 and Section 13.3 would otherwise
  each appear to claim.