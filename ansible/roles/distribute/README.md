# `stop_cran.namespace2xml.distribute`

Render configuration with [namespace2xml](https://github.com/stop-cran/namespace2xml) **on the
controller**, then copy the result to a managed node. The node needs neither .NET nor the
transformer — only the controller does.

This is the companion to the [`render`
module](https://github.com/stop-cran/namespace2xml/blob/master/ansible/plugins/modules/render.py),
which does the opposite: it runs the transformer *on the node*, against files that already live
there. Both topologies are supported because both are real:

| | `render` module | `distribute` role |
|---|---|---|
| Where the transformer runs | the node | the controller |
| Where the inputs live | the node | the controller (or in the play) |
| What the node must have installed | .NET and namespace2xml | nothing beyond a Python interpreter |
| Best when | each node owns its own inputs | one source of truth feeds many nodes |

If your nodes cannot spare the disk for a .NET runtime, or if the configuration is generated from
data the controller holds anyway, use this role.

## Quick start

```yaml
- hosts: appservers
  become: true
  roles:
    - role: stop_cran.namespace2xml.distribute
      vars:
        namespace2xml_distribute_inputs:
          - files/base.properties
          - text: |
              app.port={{ app_port }}
              app.tier={{ app_tier }}
        namespace2xml_distribute_scheme:
          - data:
              app:
                output: xml
                root: app
        namespace2xml_distribute_dest: /etc/app
        namespace2xml_distribute_mode: "0640"
```

`files/base.properties` is read on the controller. `/etc/app/app.xml` is written on the node.

## Inputs and schemes

`namespace2xml_distribute_inputs` and `namespace2xml_distribute_scheme` take exactly the shape the
`render` module's `inputs` and `scheme` take — a list whose entries are each either a string naming
a file, or a mapping carrying one of `file`, `text` or `data`:

```yaml
namespace2xml_distribute_selector: app          # the namespace everything below shares

namespace2xml_distribute_inputs:
  - group_vars/base.properties          # a file on the controller
  - file: "{{ role_path }}/files/x.xml" # the same thing, spelled out
  - text: |                             # the configuration, in the play
      app.port: 8080
    format: yaml
  - data:                               # a structure, encoded for you
      host: "{{ inventory_hostname }}"
```

`format` belongs only to a `text` entry. A `file` entry takes its parser from the file's own
extension — the path is handed to the transformer rather than copied, so a `format` beside it could
not be honoured. A `data` entry is encoded into the transformer's own syntax by the collection, so
there is no other parser left for a format to reach. Both combinations are refused rather than
ignored.

> **A `data` entry is hung under the selector.** With the default selector, `data: {host: web1}`
> becomes `cfg.host=web1`. If the scheme is rooted at `app`, the two never meet: the render
> succeeds, the file is written, and the input is silently absent from it. Whenever a play mixes a
> `data` entry with text or file inputs, set `namespace2xml_distribute_selector` to the root those
> inputs already use — as above — and write the mapping relative to it.

Order matters: later entries override earlier ones, so a shared base can be followed by a per-host
override. See the [module's
documentation](https://github.com/stop-cran/namespace2xml/blob/master/ansible/plugins/modules/render.py)
for the full description of every entry key, and
[`docs/specification.md`](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md)
for what the transformer does with them.

## Variables

| Variable | Required | Default | Meaning |
|---|---|---|---|
| `namespace2xml_distribute_inputs` | yes | — | Ordered inputs, resolved on the controller. |
| `namespace2xml_distribute_scheme` | yes | — | Ordered schemes, resolved on the controller. |
| `namespace2xml_distribute_dest` | yes | — | Directory on the **node** the result is written under. |
| `namespace2xml_distribute_selector` | no | `cfg` | Namespace a `data` input is flattened under. See the warning below. |
| `namespace2xml_distribute_convention` | no | `escaped` | `escaped` or `xmltodict`; how a `data` key is read. |
| `namespace2xml_distribute_variables` | no | `{}` | Namespace entries applied after every input. |
| `namespace2xml_distribute_tool` | no | searched | Path to the transformer **on the controller**. |
| `namespace2xml_distribute_owner` | no | see below | Owner of every file and directory written on the node. |
| `namespace2xml_distribute_group` | no | see below | Group of the same. |
| `namespace2xml_distribute_mode` | no | see below | Mode of every file. Quote octal literals. |
| `namespace2xml_distribute_directory_mode` | no | see below | Mode of every directory the role creates. |
| `namespace2xml_distribute_backup` | no | `false` | Keep a timestamped copy of anything overwritten. |

Left unset, these four are not passed to the node at all, and the defaults are the ones
`ansible.builtin.copy` and `ansible.builtin.file` already have. That is not one rule but two.
Ownership follows the user the task ran as — the connection user, or `root` under `become: true`.
Permissions follow the node's `umask` for anything newly created, and are **left alone** on
anything that already exists, so a file whose mode was changed on the node keeps that mode across
runs until you name `namespace2xml_distribute_mode`.

Setting `namespace2xml_distribute_owner` or `namespace2xml_distribute_group` to anything other than
the connection user needs `become: true` in the play. Without it the change is refused by the node,
and the role stops on a message from `chown` or `chgrp` rather than a message of its own.

## Return values

The role leaves two registered variables behind for the play to inspect:

- `namespace2xml_distribute_render` — the `render` module's own result, including `files`,
  `diagnostics` and `tool_identity`, with paths pointing into the controller's staging directory.
- `namespace2xml_distribute_copied` — the `copy` loop's result. `results` has one entry per file;
  `selectattr('changed')` is how you find what the run actually altered on the node.

```yaml
- name: Reload only when something moved
  ansible.builtin.service:
    name: app
    state: reloaded
  when: namespace2xml_distribute_copied.results | selectattr('changed') | list | length > 0
```

Guard that `when:` with `is defined` if the play continues past an unreachable host. A host that
stopped answering never reaches the copy, so `namespace2xml_distribute_copied` is left as a skip
result with no `results` key at all, and `selectattr` on it raises rather than returning nothing.

Read those `results` rather than the play recap when you want to know whether the node changed. If
one file in the loop fails, the recap counts the host as failed and its `changed` column can stay
at zero even though earlier files in the same loop were written — the per-item entries still carry
`changed: true`, but the task-level tally does not. The role is not transactional: a run that fails
part way through has already copied everything it got to before the failure, and those files stay.

## Behaviour worth knowing

**It renders once per host, not once per play.** The render runs with that host's variables in
scope, so `{{ inventory_hostname }}` in an input or a scheme means what it says. A fleet whose
configuration is identical pays for one run of a fast local process per host; a fleet whose
configuration is not would otherwise be silently handed one host's output.

**Check mode is honest.** Under `--check` the render still happens, into a temporary directory on
the controller that is removed either way. Only the copy is held back — so what check mode reports
is the difference between what the render produced and what the node already has, which is the
question being asked. Nothing is written to the node.

**The staging directory is always removed.** It holds rendered configuration, which routinely
carries credentials, so the cleanup runs in an `always` block — a failed render or a failed copy
does not leave it behind.

**`become` is not taken to the controller.** `become: true` in a play means "escalate on the managed
node". The role's controller-side tasks set `become: false` explicitly, so a play does not end up
running the transformer as root on the controller or needing passwordless sudo there. If an input
really is only readable by root on the controller, read it in your own task and pass it as `text`.

**Directories are created before files are copied.** Paths the scheme asked for are preserved
beneath `namespace2xml_distribute_dest`, and every directory between the destination and the file
is named explicitly — including the destination itself — so that
`namespace2xml_distribute_directory_mode` applies to all of them and keeps applying on later runs.
A pass that only changes the mode converges the whole tree, not just its deepest directory.

**Symlinks standing where directories belong are refused, not followed.** If some path component
under the destination is a link, the role stops rather than writing through it, so a link planted
on the node cannot redirect rendered configuration outside the destination. A destination that is
*itself* a symlink is a deliberate choice by the play and keeps working normally.

**A directory standing where a file belongs is not detected.** If the node already has a directory
at the path an output renders to — `app.xml/` where `app.xml` should go — `ansible.builtin.copy`
reads the destination as a directory to copy *into* and writes `app.xml/app.xml` instead, reporting
success. Later runs then report no change, because from the module's point of view there is nothing
left to do. The role does not check for this: catching it would cost an extra round trip to the
node for every file rendered, on every run, to guard against a state nothing the role does can
produce. If something else on the node might create directories named after the files your scheme
renders, check for them in your own task before calling the role.

**A node that cannot be reached fails, it does not pass.** The copy is skipped and the staging
directory on the controller is removed, so nothing rendered — credentials included — is left
behind. The cost is that the host is reported as **failed** rather than **unreachable**: the role
turns the unreachable result into an ordinary failure so that its own cleanup can run first. Plays
that count on the distinction — `max_fail_percentage`, or anything reading the recap — should
count failures here, not unreachables.

**Nothing is ever deleted.** The role writes what the scheme produced and leaves everything else
alone. Drop an output from the scheme and the file it used to render stays on the node, unchanged
and now unmanaged. If a destination has to contain exactly what the scheme describes and nothing
else, remove the stale files yourself.

## Requirements

- ansible-core 2.15 or newer.
- .NET and [namespace2xml](https://www.nuget.org/packages/namespace2xml) **on the controller**.
- On the node: a Python interpreter, which Ansible needs anyway. Nothing else.

## Licence

Apache-2.0. See
[LICENSE](https://github.com/stop-cran/namespace2xml/blob/master/LICENSE).
