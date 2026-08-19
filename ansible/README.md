# `stop_cran.namespace2xml`

Render Ansible data structures as XML, JSON, YAML, INI or namespace text, through the
[`namespace2xml`](https://github.com/stop-cran/namespace2xml) transformer.

```yaml
- name: Render a logback configuration and place it on the target
  ansible.builtin.copy:
    content: "{{ logback | stop_cran.namespace2xml.render('xml', root='configuration') }}"
    dest: /opt/app/logback.xml
    mode: "0644"
  vars:
    logback:
      appender:
        name: STDOUT
        encoder:
          pattern: "%d{HH:mm:ss} %-5level %msg%n"
      root:
        level: info
```

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appender>
    <name>STDOUT</name>
    <encoder>
      <pattern>%d{HH:mm:ss} %-5level %msg%n</pattern>
    </encoder>
  </appender>
  <root>
    <level>info</level>
  </root>
</configuration>
```

## Why this exists

Ansible can already write XML, in the sense that Jinja can concatenate strings. What it cannot do
is guarantee the result is well-formed, correctly escaped, and the same document every time. This
filter hands the problem to a transformer whose behaviour is fixed by a
[normative specification](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md),
so the rendering is a contract rather than a template you have to review character by character.

That specification is 300 KB and is not shipped here. What is shipped, next to this file, is
[`docs/specification-summary.md`](https://github.com/stop-cran/namespace2xml/blob/master/ansible/docs/specification-summary.md):
the rules that decide what the filter does to your data, each one quoted verbatim from the
specification and checked against it in CI, with links to everything else. Read it if you are
offline, if you are an agent that needs the contract without fetching 300 KB, or if you want the
short version first.

## Requirements

| | |
|---|---|
| ansible-core | `>=2.14` |
| Controller | the [`namespace2xml`](https://www.nuget.org/packages/namespace2xml) .NET tool, 3.0 or later |
| Target nodes | nothing |

The filter evaluates on the **controller**, where templating happens. Target nodes need neither
.NET nor the tool.

```bash
dotnet tool install --global namespace2xml --prerelease
ansible-galaxy collection install stop_cran.namespace2xml
```

`--prerelease` is not optional today. The 3.0 line is still on `3.0.0-preview`, and without that
flag NuGet resolves the newest *stable* version, which is 2.4.0. That build accepts the same
arguments and the same scheme spellings, so nothing would look wrong — it would simply render
under the previous contract. The filter therefore reads `--version` and **refuses** any binary
that does not report a `contract-bundle`, rather than proceeding and producing a document whose
rules you did not choose. When 3.0.0 is tagged stable the flag becomes unnecessary and this
paragraph goes away.

The filter finds the binary at `$NAMESPACE2XML`, then on `PATH`, then in the dotnet global-tools
directory. That last step is not redundant: `dotnet tool install --global` writes to
`~/.dotnet/tools` whether or not your login shell puts it on `PATH`, and a filter runs inside
`ansible-playbook`, whose environment was fixed before the play began. A filter that only
consulted `PATH` would fail on a host where the tool is installed and working — and fail again
when a play installs the tool in one task and templates with it in a later one.

## Usage

### Pair it with `copy`, not with `content` alone

The filter returns text. It does not write files, and it deliberately does not try to: pipe it
into `ansible.builtin.copy`, which already owns idempotence, check mode, diff, backup, ownership
and SELinux context. A second run over unchanged data reports `changed=0` because `copy` compares
the rendered text against what is on the node.

### Arguments

| | |
|---|---|
| `fmt` | *(positional, required)* `xml`, `json`, `yaml`, `ini`, `namespace`, `quotednamespace` |
| `root` | the XML document element name. Required whenever the data has more than one top-level key |
| `scheme` | explicit scheme text, for anything the synthesized minimal scheme does not cover |
| `selector` | the top-level name the data is written under. Default `cfg` |
| `delimiter` | output delimiter, for the flat formats |
| `tool` | path to the binary. Authoritative — a value given here resolves or the filter fails |
| `memoize` | reuse an identical earlier render in the same run. Default `true` |
| `workdir` | parent for the temporary marshalling directory |

Full documentation, including the specification sections each argument corresponds to:

```bash
ansible-doc -t filter stop_cran.namespace2xml.render
```

### `root` is required for XML with more than one top-level key

An XML document has exactly one document element. The filter does not guess its name, because
the name is a fact about the target document rather than about your data — `configuration` and
`beans` and `Project` are all correct for the same dictionary.

### Schemes

The filter synthesizes the smallest scheme that expresses your arguments. Pass `scheme` for
anything beyond that — `type`, `substitute`, `hidden` and the rest of the
[scheme rules](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md).
The selector the scheme declares must match `selector`, and a rule's pattern must match the
value's full path in the generated profile — `cfg` plus your keys.

```yaml
scheme: |
  cfg.output=ini
  cfg.*.version.type=string
```

### Memoization

Rendering spawns a process, so identical renders are cached within a run. The key covers the
entire marshalled input *and* the tool's version and contract revision, so the cache cannot
survive a tool upgrade or a contract change. Pass `memoize=false` to force every call to spawn
the tool.

## Fidelity limits

The mapping from Ansible data to profile text is lossy in four places. All four are inherent to
representing a data structure as namespace records; none is a defect to be fixed in a patch
release. Read this section before adopting the filter for a format where any of them matters.

### 1. Types are inferred from value text

Payload types come from how a value is spelled, not from its Python type
([§18](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md#18-scalar-inference)). The
string `"true"` and the boolean `true` produce the same record, as do `"3"` and `3`. A version
number written `version: "3"` renders as the integer `3`.

A `type` scheme rule is the only way to force a string:

```yaml
scheme: |
  cfg.output=json
  cfg.version.type=string
```

The pattern must match the value's **full path** in the generated profile, which is
`cfg` + your keys. For `{"version": "3"}` that path is `cfg.version`; for
`{"section": {"version": "3"}}` it is `cfg.section.version`, matched by `cfg.*.version`. A
pattern that matches nothing is not an error — the value simply keeps its inferred type — so
check the result rather than assuming the rule applied.

### 2. Integer keys make their parent a sequence

A name part that is a canonical decimal integer makes its parent a sequence
([§8.7](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md#87-numeric-paths-and-ordered-sequences)). This is
what lets lists round-trip, and it applies to *any* mapping whose keys happen to look like
indices:

```yaml
ports:
  "0": http
  "1": https
```

renders as a sequence, not as a mapping with numeric keys. A leading zero disables the inference
for the whole parent, so `"00"`, `"01"` stays a mapping — a sharp edge if some of your keys are
zero-padded and some are not.

### 3. Binary data is refused

Bytes have no value spelling. The filter raises rather than guessing an encoding. Base64-encode
explicitly if you need bytes in output:

```yaml
blob: "{{ raw | b64encode }}"
```

### 4. XML attributes, content tokens and qualified names cannot be addressed

**This is the significant one.** The encoder is *total* — every key has an escaped spelling, so
every key reads back as itself. That guarantee is what costs the addressing: the three name
forms XML needs are exactly the three the encoder escapes away.

| You want | You get | Result |
|---|---|---|
| `<node id="1">` | key `@id` → `\@id` | element named `\@id`, rejected as not an `NCName` |
| `<x>text</x>` mixed with children | key `#text` → `\#text` | element named `\#text` |
| `<ns:local>` | key `Q{urn}local` → `\Q{urn\}local` | element named literally |

A `@id` key produces a **blocking `XML002` error** naming
[§11.2](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md#112-supported-xml-subset),
not a silently wrong document. That is the intended behaviour for v1.0.0: the filter refuses
rather than guesses.

So this collection renders **element-only XML**. If your target format needs attributes, render
the element structure here and post-process, or use the CLI directly with a hand-written profile.
Making the addressing convention selectable is tracked as
[issue #103](https://github.com/stop-cran/namespace2xml/issues/103) and would arrive as a new
argument, so nothing you write today would break.

## Versioning

This collection is at 1.0.0 while the tool is at 3.x. They are separate artefacts with separate
compatibility promises: the collection pins no tool version, and the two are released under
different tags — `v3.*` for the tool, `ansible-v*` for this collection.

## How this is tested

The unit tests compare the encoder against profile text **authored by hand from the
specification**, never captured from the tool. An expectation captured from the code under test
asserts only that the code still does what it did, which is the one thing that needs no test. The
integration tests then run the real tool over that same hand-authored profile and compare its
output against what the filter produced from the data — so the two sides can only agree if both
match the specification.

## Found a problem?

Good — that is what the preview is for, and the project is built to absorb it.

Every diagnostic this filter surfaces carries a **stable code** and a **specification anchor**
naming the clause it enforces, so a disagreement can be reported precisely rather than described.
The codes are listed at
[docs/diagnostics.md](https://github.com/stop-cran/namespace2xml/blob/master/docs/diagnostics.md).
The filter passes the tool's diagnostics through unchanged — it never rewrites or summarises them.

Before filing, ask one question: **what would have to change so this never surprises anyone
again?** The answer picks the form:

| Answer | File this |
|---|---|
| The filter or the tool should have matched the specification | [Bug report](https://github.com/stop-cran/namespace2xml/issues/new?template=bug_report.yml) |
| The specification does not say, or says two things | [Specification ambiguity](https://github.com/stop-cran/namespace2xml/issues/new?template=spec_ambiguity.yml) |
| Both are right; I could not find out how to do this from the docs | [Usage gap](https://github.com/stop-cran/namespace2xml/issues/new?template=usage_gap.yml) |
| Neither the filter nor the tool can express this at all | [Feature request](https://github.com/stop-cran/namespace2xml/issues/new?template=feature_request.yml) |

Set the **Component** field to `Ansible filter` so it reaches the right place.

### What a report needs

A report that cannot be reproduced cannot be acted on. Collect these four before filing — the
whole point is that whoever picks it up should not have to ask you anything:

```bash
ansible-galaxy collection list stop_cran.namespace2xml   # collection version
ansible --version                                        # ansible-core and the Python it runs on
namespace2xml --version                                  # ALL of it, especially contract-bundle
```

1. **The `contract-bundle` revision** from `namespace2xml --version`. A report against an unknown
   contract revision cannot be acted on, because the specification it was judged against is
   unknown. `--version` also prints the exact `specification:` and `report:` URLs for the build
   that ran, which is more reliable than any link written here.
2. **The collection version and `ansible --version`.**
3. **A minimal play that reproduces it** — the smallest `vars` block and the exact filter call.
   Inline data, not a reference to your inventory.
4. **What you expected and what you got**, and *which clause of the specification* says you were
   right. If you cannot find one, that is a specification ambiguity, not a bug — file it as one.

If the tool itself is what disagrees with you, reproduce it without Ansible in the loop. Rendering
the same data with `fmt='namespace'` gives you profile text you can save and pass to
`namespace2xml` directly, and a report against the CLI is easier to act on than one wrapped in a
playbook.

Agent-authored reports are welcome and are held to the same standard as any other: state what was
verified by running it and what was only inferred, and never file a claim you have not reproduced.
The full protocol, including the report form and flood control, is in
[CONTRIBUTING.md](https://github.com/stop-cran/namespace2xml/blob/master/CONTRIBUTING.md#4-the-feedback-channel-binding).
A fix PR is welcome too, and it does not have to be complete — a failing test that encodes the
disagreement is a contribution on its own.

## Licence

Apache-2.0. See [LICENSE](LICENSE).
