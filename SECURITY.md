# Security policy

## Reporting a vulnerability

**Do not open a public issue.**

Use GitHub's private vulnerability reporting:
[Report a vulnerability](https://github.com/stop-cran/namespace2xml/security/advisories/new).

Include the `contract-bundle` revision printed by `--version`, the inputs and scheme that trigger the
behaviour, and what an attacker gains. A minimal reproduction in the conformance fixture layout
(specification Appendix C) is the fastest path to a fix.

Expect an acknowledgement within seven days.

## Supported versions

| Version | Supported |
|---|---|
| `3.0.0-preview.*` | Yes, on the latest preview only |
| `2.x` | No |

The 2.x line is superseded by the 3.0 rewrite and receives no fixes.

## What counts as a vulnerability here

This tool computes output file destinations from input data, and that input is frequently
untrusted — it comes from a repository, a pipeline variable, or a generated overlay. The security
surface follows from that.

Treat as a vulnerability:

- **Escaping the output root.** Any input, scheme or variable that causes a file to be written
  outside the configured output directory, including via symbolic links, directory junctions, other
  reparse points, `..` traversal, absolute paths, alternate data streams, or device names.
  Specification §21.1.
- **Writing despite a failed validation gate.** The specification requires that if *any* output fails
  validation, *no* output is written. A partial write after a validation failure is a data-integrity
  vulnerability, not a cosmetic bug. Specification §21.2.
- **Unbounded resource consumption.** Any input that causes memory or time growth not governed by a
  declared `--max-*` bound. The bounds exist so that a tool fed hostile configuration fails cleanly
  rather than taking the host down. Specification §23.
- **Entity or reference expansion attacks.** The XML reader rejects document type definitions and
  external entities outright. Any path that reintroduces them is a vulnerability.
- **Secret disclosure in diagnostics.** A diagnostic that echoes a value the user marked as hidden.

Not a vulnerability:

- Reading an input file the invoking user could already read.
- Writing to a destination inside the output root that the user did not expect, when the destination
  was derived correctly from the supplied inputs. That is a usage or specification issue — file it
  through the normal routes in [CONTRIBUTING.md](CONTRIBUTING.md).
- Resource exhaustion within a bound that was explicitly raised on the command line.

## Supply chain

Releases are published only from tags, never from a branch, and only from the `release` workflow.
Each release carries [build provenance attestation](https://github.com/stop-cran/namespace2xml/attestations),
so a package can be traced to the exact workflow run and commit that produced it. Symbol packages
are published alongside, and the build is deterministic and source-linked.

If you obtain a `namespace2xml` package whose provenance does not verify, treat it as hostile and
report it here.
