# Legacy differential

- namespace2xml 2.4.0: **fails**. Given `--version` it writes the single line
  `namespace2xml 2.4.0+b1c230e974a04cb363b131aad027980502fe0321` to **standard error** and exits
  `1`. Standard output is empty.
- Contract: Section 3.2 deliberately corrected behavior; Section 22 contract bundle;
  Section 6.1 for an informational request being a successful run on standard output.
- Legacy observation: the banner is an assembly version and nothing else, it is emitted on the
  stream reserved for diagnostics, and the successful answer to a question is reported as exit `1`.
  A caller that checks the exit code — which is what a caller should do — reads this as a failure.
- Clean behavior: prints one `<field>: <value>` line per field, including the
  `contract-bundle` revision and the specification and registry digests it covers.
- The difference is intentional: a defect report must be able to name the exact contract the
  observed behavior was measured against, which the legacy banner cannot express.