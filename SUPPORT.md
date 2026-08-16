# Support

## Before asking

| Question | Where the answer is |
|---|---|
| What is this tool supposed to do in case X? | [docs/specification.md](docs/specification.md) — the contract, and it is exhaustive by design |
| What does diagnostic `XXX999` mean? | [docs/diagnostics.md](docs/diagnostics.md), or the `spec` anchor in the diagnostic itself |
| Should I be using this tool for this at all? | [docs/usage-methodology.md](docs/usage-methodology.md) |
| Why did this change from 2.x? | [docs/migration-2.x-to-3.0.md](docs/migration-2.x-to-3.0.md) |
| Is this missing on purpose? | [KNOWN-LIMITS.md](KNOWN-LIMITS.md) |

Run with `--diagnostics-format json` to get the exact code, phase and specification anchor for
whatever went wrong. That anchor is usually a faster route to the answer than searching prose.

## Asking

- **A question about behaviour** — [open a discussion](https://github.com/stop-cran/namespace2xml/discussions).
- **Something surprised you** — [pick an issue form](https://github.com/stop-cran/namespace2xml/issues/new/choose)
  and read the routing table in [CONTRIBUTING.md §4.1](CONTRIBUTING.md#41-four-destinations-and-why-routing-matters)
  first. Routing matters more than usual in this project; a specification gap filed as a bug gets
  patched in code and the contract silently rots.
- **A security issue** — do not use either. See [SECURITY.md](SECURITY.md).

Always include the `contract-bundle` revision from `--version`.

## What you can expect

This is a volunteer-maintained project. There is no response-time commitment, with one exception:
security reports are acknowledged within seven days.

Reports that arrive in the form described in [CONTRIBUTING.md §4.2](CONTRIBUTING.md#42-the-report-form)
are acted on considerably faster, because they can be triaged without a conversation. A report that
already carries a failing conformance fixture is a pull request in all but name and is the most
useful thing you can send.

## Reports authored by AI agents

Welcome, and expected — but held to the rules in
[CONTRIBUTING.md §4.3](CONTRIBUTING.md#43-draft-then-submit):

- A human approves before filing. Always.
- Every claim is marked `verified-in-session` or `proposed-but-untested`. Reasoning presented as
  observation will get the report closed.
- Search for duplicates first, and add to an existing thread rather than opening a new one.

These are not bureaucracy. A feedback channel that cannot be triaged stops being read, and then the
loop this project depends on is dead.
