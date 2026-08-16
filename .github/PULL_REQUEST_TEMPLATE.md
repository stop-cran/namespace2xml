<!--
The fields below exist so a reviewer can verify rules C1-C6 in CONTRIBUTING.md without a
conversation. A pull request that leaves them blank cannot be reviewed against the contract, and
will be sent back.
-->

## What this changes

<!-- One paragraph. What observable behaviour is different after this than before it? -->

## Type

- [ ] Behaviour change (needs acceptance items and fixture evidence — C1)
- [ ] `refactor-only` (observable corpus output must be byte-identical; needs maintainer approval)
- [ ] Specification amendment (needs the contract-bundle revision to move — C3)
- [ ] Documentation only
- [ ] Infrastructure only

## Specification sections (C2)

<!--
Every section this relies on, e.g. §16.6, Appendix B. If this is an amendment, state which clauses
changed and what they now require.
-->

## Acceptance items (C1, C4)

<!--
Item numbers from conformance/assertions.json. State which move from `pending` to `required`, if any.
Marking an item required before it is genuinely covered turns an honest gap into a false claim — do
not do it to make the gate go green.
-->

## Fixture evidence (C1)

<!--
Which conformance cases prove this, and the commit or run showing they FAIL before and PASS after.
Reminder: expected output is authored from the specification or the pinned 2.4.0 baseline. Output
captured from this tool is not evidence of anything.
-->

## Legacy differential

<!--
For every fixture added or changed: does 2.4.0 agree or deliberately differ? Each case's legacy.md
must say, because docs/migration-2.x-to-3.0.md is generated from them.
-->

## Checklist

- [ ] `dotnet build namespace2xml.slnx` and `dotnet test namespace2xml.slnx` pass locally
- [ ] Derived artifacts regenerated if the specification or corpus changed
      (`tools/sync-diagnostics-registry.ps1`, `sync-contract-bundle.ps1`, `sync-assertion-manifest.ps1`, `sync-docs.ps1`)
- [ ] `CHANGELOG.md` updated, including what was **removed** and which report caused this
- [ ] `KNOWN-LIMITS.md` updated if this closes or opens a gap
- [ ] **C6** — if this touches publication, path handling or the validation gate, the §21 fixtures
      were re-run first and pass. This includes refactors that merely pass through those paths:
      those invariants never fire on a successful run, so a change that breaks them looks healthy.

## Related reports

<!-- Closes #, or "none". If an inbound report caused this, name it — CHANGELOG.md records the link. -->
