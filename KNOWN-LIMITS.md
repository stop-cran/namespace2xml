# Known limits

**Describes `3.0.0` at contract bundle `r118+33da7b7912fc`. Dated 2026-09-11.**

`3.0.0` carries this contract bundle. This file lists only current, verified boundaries of that
release and work deliberately deferred beyond it. Resolved preview findings live in
`CHANGELOG.md`; they are not limits of the stable binary.

If a boundary here blocks a real deployment, add that case to its linked issue. A concrete input,
consumer, or failed workflow is the evidence that should change the next contract revision.

## Runtime and platform

- The tool targets `net10.0` and requires the .NET 10 runtime. The documented installation path
  uses the .NET 10 SDK.
- Linux, Windows, and macOS on x64 and arm64 are the supported platform/architecture pairs.
- `--max-depth` has a hard ceiling of 4096. Several phases recursively traverse the model; the
  ceiling prevents a caller from raising the limit beyond the stack size the process reserves.
- The publication sink refuses symbolic links, junctions, reparse points, and path traversal below
  the configured output root. Hard links are outside that guarantee: an existing hard link is
  indistinguishable from the original directory entry on the supported filesystems.
- An ill-formed UTF-16 command-line token is `CLI001` when it reaches the managed host-vector
  boundary. The .NET apphost can replace an unpaired surrogate with U+FFFD before managed code sees
  it; the tool cannot distinguish that replacement from a U+FFFD the caller intentionally supplied.

## Interfaces deferred to 3.1

- **No stdin/stdout transformation mode.** Inputs, schemes, and outputs are file-backed in 3.0.
  [#26](https://github.com/stop-cran/namespace2xml/issues/26) owns the channel, buffering,
  transaction, and broken-pipe contract for a future streaming interface.
- **Diagnostic-sink failure has no expanded compatibility contract.** Section 6.4.3 defines the
  current JSON behavior; [#92](https://github.com/stop-cran/namespace2xml/issues/92) owns future
  text/JSON sink-failure semantics and additive diagnostic-code compatibility.
- **No machine-readable publication report.** Callers can consume canonical diagnostics but must
  inspect expected destination files themselves after success.
  [#116](https://github.com/stop-cran/namespace2xml/issues/116) owns a future report derived from
  the immutable output plan and staged bytes.

## Interoperability envelopes

- The documented `PortableIni1` dialect is continuously checked against Python `configparser` and
  npm `ini` 6 with the exact reader settings in `docs/format-ini.md`. No claim is made for other INI
  parsers. Five corpus shapes are intentionally outside both named readers' lossless envelopes:
  unquoted `EscapeMultiline`, a global key colliding with a section name, and three preamble-bearing
  shapes whose decimal-looking keys JavaScript reorders.
- The Ansible collection checks source-tree runtime behavior, packaged file inventory, and
  `ansible-doc` discovery. It does not yet execute both installed plugins from the built Galaxy
  tarball outside the checkout; [#121](https://github.com/stop-cran/namespace2xml/issues/121)
  tracks that artifact-level hardening for the Ansible 3.0.x line. This does not limit the .NET
  package's 3.0.0 contract.

## Deliberate non-features

- There is no snapshot-update mode. Expected conformance output is independently authored; the
  implementation never rewrites its own oracle.
- There is no locale-sensitive mode. Invariant globalization is part of the determinism guarantee.
- There is no `--force` publication path. A blocking diagnostic or `--fail-on-warning` refusal
  publishes nothing.
- Releases are created only from signed annotated tags after the exact `master` commit has passed
  the required workflows. There is no branch or manual publication path.

## Reporting a gap

Include the `contract-bundle` line from `namespace2xml --version`, exact inputs and schemes, complete
stdout/stderr, exit status, and platform. Use the
[issue chooser](https://github.com/stop-cran/namespace2xml/issues/new/choose), or GitHub private
vulnerability reporting for a security issue.
