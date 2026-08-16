# Legacy differential

- namespace2xml 2.4.0: **agrees**.
- Contract: Section 3.2 correction against insecure XML document-type or external-entity
  processing; Section 11.1's outright rejection of a document type definition; Section 22 for
  `XML001`.
- Legacy observation: the baseline exits `1` and produces no output tree, so the observed result
  matches this case's expected result (exit code `1`, empty tree). The measurement records no
  divergence and no standard error beyond the banner. An earlier draft of this note asserted
  that 2.4.0 resolved the document type definition, expanded internal entities, and could
  retrieve `local.dtd`; that assertion was written from reasoning rather than observation and is
  wrong.
- Clean behavior: Section 11.1 refuses each `<!DOCTYPE` outright with `XML001` at the token's
  Section 22 one-based line and column, so `local.dtd` is never read, the internal subset's
  entities are never defined, and `SYSTEM` identifiers do not cause retrieval. Every failing
  source reports in Section 7.3 command-line order in a single run.
- Why the observable agreement is not compatibility evidence: the case exists to pin that no
  external resource is retrieved and no entity is expanded, but the baseline's exit `1` and
  empty tree are silent about both. `local.dtd` sits unread whether the baseline refused
  parsing before the identifier was looked at or refused it after; the fixture cannot tell the
  two apart from the observable, and it cannot tell either from a run that would have
  retrieved. The baseline's exit `1` also carries no `XML001` at the specified positions, but
  diagnostics are not part of the observable the verdict is claimed against — that
  discrimination belongs to `expected-diagnostics.json`. The clean tool's refusal is the
  Section 11.1 pre-scan running before parsing begins; the baseline's is whatever mechanism its
  host XML reader defaults use, which is deliberately unspecified here because the two refusals
  are unrelated even when they land on the same exit code.
