# Legacy differential

- namespace2xml 2.4.0: **differs**. It wrote `cfg.json` as `{"m": "direct"}` over three CRLF-
  terminated lines with no final newline, and `cfg.yaml` as `m: direct` with CRLF, reporting
  nothing and exiting 0. **verified** — measured against the Appendix C.6 pinned 2.4.0 package.
- Contract: Section 16.6; Section 24.
- Legacy observation: the question this fixture asks could not be put to 2.4.0. `type=multiline`
  was inert in both formats — the sequence items `one` and `two` were discarded and the scalar
  rendered alone — and no diagnostic was produced at all, so there was no report whose count could
  be right or wrong. It also wrote `Environment.NewLine` and ended `cfg.json` without a final
  newline, so the same input produced different bytes per platform.
- Clean behavior: Section 16.6 counts the omission "once for that path and output instance", and
  one declaration naming two formats is two instances. Each writes its own file and each loses its
  own copy of the scalar, so `cfg.json` and `cfg.yaml` are both reported. Section 24 places a
  per-output-instance diagnostic in group 2 — carrying a destination and no source ordering key —
  and orders that group by the Section 21.3 destination order, which is why `cfg.json` precedes
  `cfg.yaml` here rather than the two tying on code and path.
- Why this is separate from `multiline-joins-the-sequence-and-omits-the-scalar`: that case declares
  one format, so a single record satisfies it whether the count is per instance, per declaration or
  per path. Only a declaration with two instances tells those apart, and that distinction is the
  whole content of the cardinality rule.
