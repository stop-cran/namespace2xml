# Legacy differential

- namespace2xml 2.4.0: **differs**. Across ten Linux arm64 samples it exits 0 and writes both
  destinations, but its XML is
  `<?xml version="1.0" encoding="utf-8"?><cfg a="one" b="two" />` instead of the expected child
  elements.
- Contract: Section 3.1 preserves the deprecated aliases in Section 15.3:
  `namespacedelimiter`, `xmloptions`, `xmlns`, and `xmlnssuffix`.
- Legacy observation: the baseline accepts `namespacedelimiter` and `xmloptions=NoIndent`; it also
  accepts both legacy type values, but projects the two selected values as XML attributes.
- Clean behavior: the replacement exits 0, writes the same colon-delimited namespace destination,
  applies `NoIndent`, leaves both legacy type values as no-ops, renders the values as child
  elements, and reports four source-separated `WARN002` records.
- The measured run therefore proves successful legacy alias acceptance while recording the
  intentional Section 3.2 XML-model divergence rather than relying on an invalid option value.
