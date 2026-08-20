# namespace2xml for Ansible — the contract in brief

This is a **summary**, shipped inside the collection so that the rules survive without a network
connection. It is not the contract. The contract is
[`docs/specification.md`](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md)
in the repository, and where this page and the specification disagree, **the specification wins**.

Every rule stated below as a blockquote is quoted verbatim from the specification and is checked
against it by `tools/check-specification-quotations.ps1` on every push, so a quotation here cannot
drift from the clause it came from. Prose outside the blockquotes is summary and may simplify.

Read this if you are deciding what the filter will do to your data. Read
[`README.md`](../README.md) if you are deciding how to call it.

The collection ships two plugins, and this page is about the filter unless it says otherwise.
Sections 1, 2, 6, 7 and 9 describe the tool and apply to both. Sections 3, 4, 5 and 8 describe the
mapping from Ansible data to profile text, which is the filter's work alone — the `render` module
sends the node's own files to the tool and never synthesizes a profile from your variables. What
the module adds on top of all this is idempotence, and the one rule that follows from it is in
[`README.md`](../README.md#the-render-module): a file the module reads is never a file it writes.

---

## 1. What the tool guarantees

Given the same input bytes, argument order, scheme bytes, environment-independent options and tool
version:

> the tool must produce byte-identical output files. Diagnostic codes, severities, structured
> fields, and ordering must be identical; localized human-readable prose may differ.

That is what makes the filter safe to call from a template that also feeds a `copy` task: the same
variables render to the same bytes, so `changed` means your data changed, not that the renderer
felt different today. It is also what the module's idempotence rests on — it renders to a scratch
directory and compares bytes, so a run that reports no change has measured one rather than assumed
it.

Both plugins check they are talking to a build that makes this promise. They read `--version`,
require a `contract-bundle` field, and refuse a tool that does not report one — that field is how a
3.x build identifies the specification revision it implements, and a 2.x build does not have it.

## 2. The pipeline in one paragraph

Your Ansible data is flattened into **namespace profile** text — one `name=value` record per line,
with `.` separating the parts of a qualified name. A **scheme** then says what to do with those
names: which output format, which document root, which parts are attributes. The tool reads both
and writes the rendered document. The filter generates the profile and, unless you supply your own
scheme, the scheme too; it then returns the rendered text as a string. The module skips the first
half of that: your files on the node *are* the input, you write the scheme, and the rendered
documents are written to a directory rather than returned.

## 3. What the filter does to your data

| Your value | Becomes |
|---|---|
| mapping | one record per leaf, keys joined with `.` |
| list | child names `0`, `1`, `2`, … — see section 5 below |
| empty mapping | the `{}` sentinel, so the element is emitted rather than vanishing |
| empty list | the `[]` sentinel, likewise |
| scalar | an encoded section 8.3 value |

Every name part and every scalar is escaped before it reaches the tool, so a key containing `.`,
`=`, `#`, `!`, `@` or a backslash means that literal character and does not become syntax. You do
not escape anything yourself, and you should not try: escaping text that is already escaped is the
one way to corrupt it.

The root of the generated profile is the `selector` argument, `cfg` by default. It only has to be
a name you are not otherwise using.

## 4. Values are typed by their spelling

For `json` and `yaml` output, an unmarked value is typed by what it looks like:

> Scalar inference for untyped namespace values is locale-independent.

`null`, `true` and `false` are matched case-insensitively; `[+-]?[0-9]+` becomes an integer; a
JSON-compatible decimal or exponent form becomes a decimal; anything else stays a string.

> Thousands separators, locale decimal commas, hexadecimal, `NaN`, and infinities are not inferred.

So a version string such as `1.10` is rendered as a number rather than as a string. To hold a value
to a type of your choosing, declare it in a scheme with a section 16.6 `type` directive — this is
the single most common surprise, and it is a specified behaviour rather than a defect.

## 5. Lists become sequences only if the indices are canonical

The filter emits `str(index)`, which is always canonical, so a plain Ansible list is always a
sequence. It matters when you build the names yourself:

> leading-zero spellings such as `00` and `01` are ordinary mapping keys and prevent sequence
> interpretation;

Gaps and nonzero bases are allowed. A single non-canonical child name turns the whole parent back
into a mapping.

## 6. Output formats

The `fmt` argument takes exactly one of six values, and the filter rejects anything else before it
builds a scheme from it:

`xml`, `json`, `yaml`, `ini`, `namespace`, `quotednamespace`

`namespace` is the profile text itself. It is the format to reach for when reporting a problem:
render with `fmt='namespace'` and you have the exact input the tool saw, in a form you can paste
into a `namespace2xml` command line.

## 7. Schemes, and why later wins

If you pass your own `scheme`, these three sentences are the whole precedence model:

> All scheme directives follow source order only.
>
> A later matching directive overrides an earlier matching directive for the same effective
> setting.
>
> Pattern specificity does not alter precedence.

Because a later directive silently overrides an earlier one, a value interpolated into a scheme can
change the meaning of the whole file rather than fail loudly. The filter therefore refuses a line
break in `root`, validates `fmt` against the list above, and encodes `delimiter`. If you build a
scheme yourself from untrusted data, you own that problem.

`scheme` and the synthesis arguments (`fmt`, `root`, `delimiter`) are mutually exclusive: pass a
scheme and you are describing the whole output yourself.

## 8. Diagnostics and failure

> No user-caused error may escape only as an unhandled exception.

Exit codes are only two:

| Code | Meaning |
|---:|---|
| `0` | Success, including success with warnings. |
| `1` | Invalid CLI, invalid input, invalid scheme, reference failure, rendering failure, path violation, or publication failure. |

Every diagnostic carries a **stable code** — `SCHEME002`, `XML002` and so on. The code is the
part that does not change between releases and the part worth searching for. Look it up in the
[diagnostics registry](https://github.com/stop-cran/namespace2xml/blob/master/docs/diagnostics.md),
which gives each code its meaning and the specification section that defines it.

When the tool fails, the filter raises `AnsibleFilterError` with the tool's own stderr attached,
plus a line pointing at the issue tracker of the exact build that ran. The module fails the task
with the same text, and reports the identity of the binary it ran in `tool_identity` even on
success, so the node that misbehaved can be named. Read the code first: it is usually a precise
statement about your data.

## 9. Fidelity limits

Four things to know before you trust the filter's output. [`README.md`](../README.md) has the detail
and the workarounds for each; this is the index. They are all consequences of turning Ansible data
into profile text, so none of them applies to the module, which sends your files to the tool as
they are.

1. **Types are inferred from value text** — section 4 above. A string that looks like a number
   becomes one unless a `type` directive says otherwise.
2. **Integer keys make their parent a sequence** — section 5 above. A mapping whose keys happen to
   be `0`, `1`, `2` is rendered as a list.
3. **Binary data is refused.** A value that is not expressible as a profile scalar is an error
   rather than a silent coercion.
4. **XML attributes, content tokens and qualified names need `convention: xmltodict`.** By default
   the filter escapes `@`, `Q{…}` and `#` into ordinary names, because the default encoding is
   total and every key must read back as itself. Pass `convention=xmltodict` and those markers are
   read as §11.4 addressing instead — `@id` is an attribute, `Q{urn:p}b` a qualified element,
   `#text` an element's own text, `#0`/`#1` ordered content. That convention is total too: `\@x`
   spells the literal name `@x`. `#text` beside a child element is refused rather than positioned
   by guesswork. The module's `variables` option remains the more direct route when the document
   is already on the node, because it passes names to the tool verbatim.

Rendering is also one-way: neither plugin reads a rendered document back into variables.

## 10. Where everything is

| What | Where |
|---|---|
| Full specification (normative) | <https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md> |
| Diagnostic code registry | <https://github.com/stop-cran/namespace2xml/blob/master/docs/diagnostics.md> |
| Known limits of the tool | <https://github.com/stop-cran/namespace2xml/blob/master/KNOWN-LIMITS.md> |
| This collection's usage guide | <https://github.com/stop-cran/namespace2xml/blob/master/ansible/README.md> |
| How to report a problem (binding) | <https://github.com/stop-cran/namespace2xml/blob/master/CONTRIBUTING.md#4-the-feedback-channel-binding> |
| Notes for AI agents working here | <https://github.com/stop-cran/namespace2xml/blob/master/AGENTS.md> |
| Issue tracker | <https://github.com/stop-cran/namespace2xml/issues> |
| The .NET tool on NuGet | <https://www.nuget.org/packages/namespace2xml> |
| Repository | <https://github.com/stop-cran/namespace2xml> |

Offline, the two documents that ship inside this collection are this page and `README.md`; both sit
next to the installed plugin, under
`~/.ansible/collections/ansible_collections/stop_cran/namespace2xml/`.

## 11. Found a problem?

Open an issue at <https://github.com/stop-cran/namespace2xml/issues/new/choose> and pick the
**Ansible collection** component. Route it by what is actually wrong:

| Symptom | Where it belongs |
|---|---|
| The filter misbehaves, or its arguments do | Bug report, component *Ansible collection* |
| The module misbehaves — wrong `changed`, a refusal you disagree with, a file it should not have touched | Bug report, component *Ansible collection* |
| The rendered document is wrong for the input | Bug report, component *CLI* — reproduce it with `fmt='namespace'` first |
| The documentation did not answer your question | Usage gap |
| You need a behaviour that is not specified | Feature request — say whether it is a specification amendment |

Include all three versions, because a mismatch between them is itself a common cause:

```bash
ansible --version
ansible-galaxy collection list stop_cran.namespace2xml
namespace2xml --version
```

For the module, run the last of those on the managed node rather than on the controller — the
task's `tool_identity` return value tells you which binary actually ran, and that is the one to
report.

A report is actionable when it carries the input data, the exact filter call or task, what you
expected, what you got, and the diagnostic code if there was one. If you are an AI agent, say so in
the issue, and quote the specification section you believe was violated rather than asserting the
behaviour is wrong — the specification is the thing being appealed to, and pointing at the clause
is what makes a report cheap to act on.
