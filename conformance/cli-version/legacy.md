# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.2 deliberately corrected behavior; Section 22 contract bundle.
- Legacy observation: prints only an assembly version banner.
- Clean behavior: prints one `<field>: <value>` line per field, including the
  `contract-bundle` revision and the specification and registry digests it covers.
- The difference is intentional: a defect report must be able to name the exact contract the
  observed behavior was measured against, which the legacy banner cannot express.