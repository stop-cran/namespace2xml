# Legacy differential

- namespace2xml 2.4.0: **fails**. It does not recognize the 3.0 XML normalization or warning
  publication policies.
- Contract: Sections 11.7 and 21.2; Section 26 item 92.
- Clean behavior: normalization still emits `WARN007`; the opt-in warning policy therefore refuses
  publication and preserves the existing XML destination.
