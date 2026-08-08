# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 14.1; Section 16.3.
- Legacy observation: neither format existed, so there was no bare-scalar or empty-view behavior
  to compare.
- Clean behavior: Section 14.1 lets JSON and YAML "emit a scalar document", so a view that is
  itself a payload is written as a top-level scalar with no synthesized key — unlike the flat
  formats, which require an explicit `root`. A view that selects nothing emits an empty mapping.
  Section 16.3's `root=cfg.app` wraps the document in nested single-member mappings rather than
  prefixing a key, and the original selector name is not retained because it is not present in the
  `root` value.
- The difference is intentional: a structured format can spell a scalar document, so requiring a
  key would invent a name the source never had.
