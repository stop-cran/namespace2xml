# Legacy differential

- namespace2xml 2.4.0: **agrees**, modulo CRLF line endings under the Section 24 divergence.
- Contract: Section 9.2 — "Within that one part, unescaped `*` and `*[identifier]` tokens retain
  their wildcard-template meaning for compatibility" — with the extraction and expansion rules of
  Section 10.4 and Section 12.4.
- Section 10.4 is titled for YAML, and Section 9.2 states the same rule for JSON in its own words.
  This case exists so that the JSON half is fixed by a fixture rather than inferred from the YAML
  one: the two formats share a reader, and a change that gated extraction on the YAML front end
  alone would pass every YAML case in the corpus.
- The generated `c` precedes the concrete `b` under Section 5.3, because `template.json` carries
  the earlier Section 4.7 CLI source ordinal. See
  [#73](https://github.com/stop-cran/namespace2xml/issues/73) for the Section 10.4 worked example
  that prints the opposite order.
- Legacy observation: 2.4.0 produced these exact two lines, so JSON template extraction is a
  Section 3.1 preservation rather than a Section 3.2 correction. What it did not preserve is the
  ordering, which it reached by a different route — see the note in
  `a-yaml-wildcard-key-enriches-each-record-of-a-later-file`.