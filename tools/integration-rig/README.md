# Docker integration rig

A real Ansible controller and three real managed nodes, in containers, talking over real SSH.

## What this proves that the other suites cannot

The collection already has three test layers, and none of them reaches this far:

| layer | what it exercises | what it cannot see |
|---|---|---|
| `ansible-test units` | the plugins as Python functions | anything about a managed node |
| `ansible-test integration` | the plugins through Ansible, on localhost | remote execution, per-node divergence |
| `ansible-test sanity` | documentation, imports, style | behaviour of any kind |

The `render` module exists because a managed node's *own* files should decide that node's state.
Proving that needs two nodes with different inputs, a transport between them and the controller, and
the tool installed on the node rather than on the controller. That is what this rig is.

The `distribute` role exists for the opposite arrangement, where the node cannot host the tool at
all. Proving *that* needs a node with no .NET and no transformer on it, which is what `node3` is:
`Dockerfile.node-bare` asserts their absence at build time, `rig.ps1` asserts it again from the
controller, and playbook 07 asserts it a third time before it tests anything that rests on it. A
base image that quietly started shipping .NET would otherwise turn the whole exercise into a
tautology.

It is a harness a maintainer runs deliberately -- before a release, or when changing the module's
contract with the node. **It does not run in CI**: it needs a Docker daemon and pulls a ~1 GB base
image, which is a poor trade for every push.

## Prerequisites

- A running Docker daemon (Linux containers).
- PowerShell -- Windows PowerShell 5.1 or `pwsh` 7+.
- `ssh-keygen`, which ships with OpenSSH and is present by default on Windows 10+, macOS and every
  mainstream Linux.
- Outbound network for the base image, the collection and the package.

## Running it

```powershell
./rig.ps1                       # down, up, test -- the full cycle
./rig.ps1 -Command up           # build images and start containers
./rig.ps1 -Command test         # run the playbooks against a running rig
./rig.ps1 -Command down         # remove the containers and the network
```

On Windows these can fail before Docker is touched, with *"running scripts is disabled on this
system"* -- Windows PowerShell ships a `Restricted` execution policy on client SKUs, where a script
run from disk is refused. Windows Server defaults to `RemoteSigned`, where an unblocked local script
runs. `Get-ExecutionPolicy` reports where you actually stand; don't infer it from the SKU. Invoke
through a process-scoped bypass rather than relaxing the machine's policy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\rig.ps1 -Command up
```

`pwsh -NoProfile -ExecutionPolicy Bypass -File ./rig.ps1 -Command up` is the PowerShell 7 equivalent;
`pwsh -NoProfile -File` is the form the rest of this repository uses for its scripts. One case the
bypass will not rescue: if `Get-ExecutionPolicy -List` shows a policy set under `MachinePolicy` or
`UserPolicy`, Group Policy is deciding and it outranks the command line.

By default the rig installs the tool version declared in `Directory.Build.props` from **nuget.org**
and the collection from **Galaxy**, so it tests what an operator actually receives. To test a
working tree instead:

```powershell
./rig.ps1 -PackageSource local
./rig.ps1 -PackageSource local -Collection local
./rig.ps1 -PackageSource local -Collection ../../ansible/stop_cran-namespace2xml-3.0.0.tar.gz
```

`-Collection local` stages `ansible/` from the working tree into the build context, builds a tarball
inside the controller image and installs that. Docker cannot read a path outside its build context,
so a tarball given by path is copied in the same way rather than mounted.

### The idempotence check

Run `./rig.ps1 -Command test` **twice**. On the second pass playbooks 01, 03 and 04 must report
`changed=0`; playbook 08 asserts in memory and never writes, so it reports `changed=0` on every
pass. Playbooks 02, 05, 06 and 07 legitimately report changes: 02 and 05 delete their target
first so the run genuinely creates something, 06 modifies a value on purpose to exercise the diff
path, and 07 runs a check-mode pass whose reported change is precisely what it asserts.

## What each playbook covers

| playbook | covers |
|---|---|
| `01-node-render.yml` | node-local inputs decide node-local state; two nodes diverge; a second render changes nothing |
| `02-check-and-order.yml` | `--check` writes nothing but still produces a diff; multiple `src` layers, later wins |
| `03-failure-surface.yml` | five deliberate failures -- missing input, bad scheme, absent tool, a binary that is not a 3.x build, unwritable dest |
| `04-yaml-scheme.yml` | `scheme_yaml`; the self-overwrite guard; `'*'` selectors; escaped dots; multi-format output |
| `05-filter.yml` | the controller-side filter, which renders play data rather than node files |
| `06-vars-and-safety.yml` | `-v` variables; a scheme file living on the node; `dest` is never cleaned; diff on modification |
| `07-distribute.yml` | the `distribute` role against `node3`, which has no .NET and no tool: all three input shapes, a scheme that renders into a subdirectory, file and directory modes, idempotence, check mode writing nothing, and no staging left on the controller |
| `08-scheme-spellings.yml` | the three ways to hand the filter a scheme -- entry list, `scheme_text`, `scheme_yaml` -- agree, an explicit `type` rule wins over inference, and a bare-string `scheme` is refused by name rather than read as a path |

## Things that will bite you

Each of these cost real debugging time; they are recorded so they cost it only once.

- **`failed_when: false` overwrites the `failed` flag.** `result is failed` is then always false and
  an assertion that something failed silently passes. Use `ignore_errors: true` instead.
- **`x is changed` breaks on a converged system.** That is the module behaving correctly. Either
  assert success, or delete the target first so the run is genuinely predicting a creation.
- **`ansible-playbook a.yml b.yml` stops at the first failing playbook.** `rig.ps1` runs each one
  separately so a single failure cannot hide the rest.
- **Ansible ignores `ansible.cfg` in a world-writable directory.** `/play` is a bind mount and looks
  world-writable from inside the container, so `rig.ps1` passes `ANSIBLE_CONFIG` explicitly. Without
  it the inventory silently disappears too.
- **`pip install ansible-core` can fail where apt succeeds.** On some networks
  `files.pythonhosted.org` answers pip with `SSLV3_ALERT_HANDSHAKE_FAILURE` while `pypi.org/simple/`
  returns 200. Both Dockerfiles use apt.
- **The images must be .NET SDK images, not `ubuntu:24.04`.** The tool targets `net10.0`, which
  Ubuntu's apt does not carry -- and the controller needs the binary too, because the *filter* runs
  controller-side.
- **`dotnet tool install` straight from nuget.org fails in a container.** `api.nuget.org`'s V3 index
  resolves IPv6-only on some networks and the default Docker bridge has no IPv6 route. `rig.ps1`
  downloads over the V2 endpoint on the host and installs from `--source /pkg`.
- **`check_mode` is not accepted on `include_role`.** Ansible rejects the play at parse time with
  `'check_mode' is not a valid attribute for a IncludeRole`. Wrap the include in a `block:` and put
  the keyword there -- a block passes it down to everything it contains.
- **A check-mode `copy` result has no `dest`.** Ansible templates a task's arguments whether or not
  an assertion fails, so a `fail_msg` reaching for `dest` turns a passing assertion into an
  undefined-variable error. The loop's own `item` is always present.
- **PowerShell variable names are case-insensitive.** A local named `$collection` silently
  overwrites the `$Collection` parameter, and the failure surfaces much later as an unrelated build
  error.
- **Never write a Markdown backtick into a file through PowerShell string manipulation.** Backticks
  are escape characters in double-quoted strings *and* in `@"..."@` here-strings, so `` `f `` and
  `` `a `` become a form feed and a BEL inside the file. Edit these files with a real editor.
- **nuget.org's copy of a package is not byte-identical to the GitHub release copy.** The gallery
  adds a `.signature.p7s` counter-signature. Every other entry matches by CRC32; a size or hash
  difference between those two channels is expected, not a supply-chain problem.

## Housekeeping

Everything the rig creates is named `n2x-*`, and `rig.ps1 -Command down` removes exactly those by
name. **Never `docker prune` to clean up after it** -- a maintainer's machine usually carries
unrelated stopped containers.

`.keys/` and `pkg/` are generated and gitignored: a throwaway SSH keypair for containers that live
for one test run, and a release artefact that is not source.

## See also

- [`ansible/README.md`](../../ansible/README.md) -- the collection itself
- [`docs/specification.md`](../../docs/specification.md) -- normative; binds over the code
- [`CONTRIBUTING.md`](../../CONTRIBUTING.md) -- how to report what this rig finds
