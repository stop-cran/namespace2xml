# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 13.1 format-agnostic simple-alias resolution; Section 13.2 kind forwarding and
  mixed literal/reference concatenation; Section 26 item 9. Section 3.1 preserves value references
  themselves, but does not enumerate the format-agnostic simple-alias index that reduces `@host` to
  the alias `host`; that addressing rule is a new specification requirement introduced in Section
  13.1 rather than a Section 3.2 correction.
- Legacy observation: the baseline exits `1` and writes no `app.properties`. The measurement
  records `exit 1 (expected 0); missing app.properties`. Standard error is empty beyond the
  banner.
- Clean behavior: `${app.database.port}` forwards the referent's settled kind; `${app.database.@host}`
  addresses the XML attribute canonically; `${app.database.host}` addresses the same attribute
  through its format-agnostic alias, which is admissible only because `host` is unique at that
  node; a mixed literal/reference `endpoint` value is a string under Section 13.2 regardless. The
  run exits `0` writing `app.properties`.
- Why the difference is intentional: 2.4.0 had one-level scalar references (legacy item 100) and
  canonical XML components (item 109), but the format-agnostic simple-alias index that reduces
  `@host` to the alias `host` was added only for the rewrite. In the 2.4.0 model, the reference
  `${app.database.host}` on line 4 has no matching canonical path -- the payload lives at
  `app.database.@host` and there is no rule for reducing one to the other -- so the reference fails
  to resolve and the run exits nonzero without emitting a file. This is the divergence the case
  exists to document: the specification's alias index is what makes the same reference portable
  across formats, and a caller relying on it sees a run that produces nothing under the baseline
  rather than one that produces `app.properties`.

## Not asserted

- Which kind a forwarded value carries in a format that renders kinds distinguishably. The two flat
  serializers this fixture can use render a forwarded integer and its text identically; Section
  13.2 kind preservation is pinned by unit tests against the resolver instead.
