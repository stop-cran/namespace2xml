# `stop_cran.namespace2xml`

Render configuration as XML, JSON, YAML, INI or namespace text through the
[`namespace2xml`](https://github.com/stop-cran/namespace2xml) transformer — either from data held
in play variables, on the controller, or from a managed node's own files, on the node.

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

## Two plugins, two topologies

| | `render` filter | [`render` module](#the-render-module) |
|---|---|---|
| Runs on | the controller | the managed node |
| Transforms | data held in play variables | files already on the node |
| Produces | text, for `copy` to place | files under `dest`, converged in place |
| Needs on the node | nothing | .NET and the tool |

Pick by where the truth lives. When the configuration is assembled from play variables, the
filter renders it on the controller and `ansible.builtin.copy` places it. When the node ships
its own input files — a template inside the deployed application, a package default, a fragment
written by another role — the module runs the tool *there*, so each node's own files decide that
node's state and nothing has to be fetched back to the controller to be transformed.

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
| ansible-core | `>=2.15` |
| Controller | the [`namespace2xml`](https://www.nuget.org/packages/namespace2xml) .NET tool, 3.0 or later — for the **filter** |
| Managed nodes | the same tool, plus a .NET SDK to install it — for the **module** only |

The **filter** evaluates on the controller, where templating happens; a play that uses only the
filter needs neither .NET nor the tool on its target nodes. The **module** is the other way
round. It runs the tool on each node it targets, because its inputs are that node's own files,
so the tool has to be installed there.

```bash
# controller, for the filter
dotnet tool install --global namespace2xml --prerelease
ansible-galaxy collection install stop_cran.namespace2xml
```

```yaml
# each managed node, for the module
- name: Install the transformer on the node
  ansible.builtin.command:
    cmd: dotnet tool install --global namespace2xml --prerelease
    creates: ~/.dotnet/tools/namespace2xml
```

`--prerelease` is not optional today. The 3.0 line is still on `3.0.0-preview`, and without that
flag NuGet resolves the newest *stable* version, which is 2.4.0. That build accepts the same
arguments and the same scheme spellings, so nothing would look wrong — it would simply render
under the previous contract. Both plugins therefore read `--version` and **refuse** any binary
that does not report a `contract-bundle`, rather than proceeding and producing a document whose
rules you did not choose. When 3.0.0 is tagged stable the flag becomes unnecessary and this
paragraph goes away.

Both plugins find the binary at `$NAMESPACE2XML`, then on `PATH`, then in the dotnet global-tools
directory. That last step is not redundant, and it is load-bearing for a different reason on each
side. `dotnet tool install --global` writes to `~/.dotnet/tools` whether or not your login shell
puts it on `PATH`, and a filter runs inside `ansible-playbook`, whose environment was fixed
before the play began — so a filter that only consulted `PATH` would fail on a host where the
tool is installed and working, and fail again when a play installs the tool in one task and
templates with it in a later one. For the module the same lookup is not an edge case but the
ordinary outcome: a module runs in a non-interactive shell that never sourced the profile
`dotnet tool install` asked the operator to re-source.

## The `render` filter

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
| `scheme` | explicit scheme text, for anything the synthesized minimal scheme does not cover. On the module this same name means scheme *files* — see the note under the module's arguments |
| `scheme_text` | the same argument as `scheme`, under the name the module gives inline text. Use it when you want one spelling in both plugins |
| `scheme_yaml` | the same scheme written as a YAML mapping instead of text. Mutually exclusive with `scheme` and `scheme_text` |
| `selector` | the top-level name the data is written under. Default `cfg` |
| `convention` | how mapping keys are read: `escaped` (default) or `xmltodict`, which reads `@`, `Q{…}` and `#` as XML addressing |
| `delimiter` | output delimiter, for the flat formats |
| `tool` | path to the binary. Authoritative — a value given here resolves or the filter fails |
| `memoize` | reuse an identical earlier render in the same worker process. Default `true` |
| `workdir` | parent for the temporary marshalling directory |

Full documentation, including the specification sections each argument corresponds to:

```bash
ansible-doc -t filter stop_cran.namespace2xml.render
```

> **What a render costs.** `ansible-playbook` forks a worker per (host, task) pair, and every
> cache the filter keeps — the memoized render, the resolved binary path, the tool's contract
> identity — lives and dies inside that worker. Five identical renders in one task on one host
> cost one `--version` probe and one tool subprocess; one render in each of three tasks across
> eight hosts costs twenty-four of each. Rendering several documents in one task stays at the
> floor; spreading them over tasks does not. `ansible-doc` has the measurements.

### `root` is required for XML with more than one top-level key

An XML document has exactly one document element. The filter does not guess its name, because
the name is a fact about the target document rather than about your data — `configuration` and
`beans` and `Project` are all correct for the same dictionary.

### Schemes

The filter synthesizes the smallest scheme that expresses your arguments. Pass `scheme` for
anything beyond that — `type`, `substitute`, `merge` and the rest of the
[scheme rules](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md).
The selector the scheme declares must match `selector`, and a rule's pattern must match the
value's full path in the generated profile — `cfg` plus your keys.

```yaml
scheme: |
  cfg.output=ini
  cfg.*.version.type=string
```

### Two spellings of a scheme

A playbook is YAML, so a scheme embedded in one as a block of `a.b=c` lines is a second syntax
inside a file that already has one. Section 15 of the specification selects the scheme parser
from the file extension and accepts a structured document as readily as text, so both plugins
also take the scheme as a mapping — `scheme_yaml` on the filter and on the module:

```yaml
scheme_yaml:
  xmlinputoptions: NormalizeFormattingWhitespace
  cfg:
    output: xml
    root: configuration
    appender:
      "*":
        name:
          type: ignore
```

The two spellings are the same scheme, and the integration suite renders both and compares the
bytes. Choose on how the scheme is produced rather than on taste: a mapping composes with
`combine`, `vars_files` and inventory variables and is checked as you write it, while text stays
copy-pasteable to and from the command line and a `.scheme` file. They are mutually exclusive —
supplying both leaves the render ambiguous, so it is refused.

A few things about the mapping form are worth knowing before you meet them:

- **The nesting is the path.** Section 9 makes a JSON or YAML mapping key *one* name part —
  "only the delimiter and `\u{HEX}` lose their meaning there, because a key is one part rather
  than a path". So `cfg.output: xml` asks for a single name with a dot in it, and both plugins
  refuse it rather than pass it through. Nest the parts. This is refused rather than warned
  about because the failure is otherwise silent: as a *selector* a dotted key draws only
  `WARN009` and the render succeeds with the directive inert.
- **Write `\.` for a name that really does contain a dot.** YAML quoting cannot express this —
  `a.b`, `'a.b'` and `"a.b"` all load to the same string and the quote style is discarded — so
  the escape lives in the text, spelled as Section 8 spells it: `'a\.b': {type: attribute}`
  selects the one name `a.b`. Write it plain or single-quoted; YAML's double-quoted style
  rejects `"a\.b"` as an unknown escape.
- **A dot inside `Q{...}` needs no escape.** Section 11.4 makes those dots part of the URI —
  they "do not split the qualified path" — so `'Q{urn:example.com}name'` and
  `'@Q{urn:example.com}x'` are written as they read. The URI ends at the first unescaped `}`; a
  dot in the local name after it is ambiguous like any other and still wants `\.` or nesting.
- **A multi-valued directive is one comma-separated scalar**, not a sequence: `output: "xml,json"`.
  A filter has one return value, so a scheme that produces several files — several formats in
  one `output`, or several `filename` targets — has no single result to hand back and only works
  through the module.
- **Quote a wildcard and anything number-shaped.** A bare `*` is a YAML alias indicator, and
  YAML reads `3.10` as the number `3.1`.

A scheme key and an input key are not the same language, and the difference is deliberate: a
scheme key is a *selector*, an input key is *data*. `*` already shows this — in a scheme key it
is a wildcard, in an input key it is escaped to a literal `\*` and matches nothing else. A dot
follows the same line. In an **input** mapping, `{'a.b': 'hello'}` is the one name `a.b`, passed
through as data with no ceremony. In a **scheme** mapping the bare form is refused and `'a\.b'`
selects that same name, because here a mistake is silent rather than loud. A backslash before
anything other than a dot is not an escape at all: Section 9.1 says it "contributes itself and
consumes nothing, so a key such as `C:\dir` needs no escaping". Only a leading one is special,
where `\` suppresses a marker — write `\\@x` for a name that begins with a literal `\@`.

### Memoization

Rendering spawns a process, so identical renders are cached within a run. The key covers the
entire marshalled input *and* the tool's version and contract revision, so the cache cannot
survive a tool upgrade or a contract change. Pass `memoize=false` to force every call to spawn
the tool.

## The `render` module

The filter's inputs are play variables. The module's inputs are **files on the node**, which is
the topology the tool was built for: an application ships a pristine template, the play states
the handful of things that differ on this host, and the node renders its own configuration.

```yaml
- name: Raise the logback root level on each node
  stop_cran.namespace2xml.render:
    src:
      - /opt/app/templates/logback.xml
    scheme_text: |
      xmlinputoptions=NormalizeFormattingWhitespace
      configuration.output=xml
      configuration.filename=logback.xml
      configuration.root=configuration
    variables:
      configuration.root.@level: DEBUG
    dest: /opt/app/conf
    mode: "0644"
```

`/opt/app/templates/logback.xml` goes in untouched and `/opt/app/conf/logback.xml` comes out with
`<root level="DEBUG">` and everything else — appenders, encoders, patterns — exactly as it was.

### Arguments

| | |
|---|---|
| `src` | *(required)* ordered input file paths **on the node**, one `-i` each |
| `scheme` | ordered scheme file paths on the node, one `-s` each |
| `scheme_text` | scheme directives written inline in the playbook, applied *after* every `scheme` file |
| `scheme_yaml` | the same inline scheme written as a YAML mapping. Mutually exclusive with `scheme_text`, and composes with `scheme` files the same way |
| `variables` | namespace entries applied after all inputs, one `-v name=value` each. Keys are passed **verbatim** |
| `dest` | *(required)* the output root directory, passed as `-o` |
| `tool` | path to the binary on the node |

> **`scheme` does not mean the same thing on the two plugins.** On the filter it is scheme
> *text*, because a controller-side render has no node filesystem to read from. On the module it
> is a list of scheme *file paths on the node*, and the inline-text spelling is `scheme_text`.
> `scheme_yaml` means the same mapping on both.
>
> Since 2.3.0 the filter also accepts `scheme_text` as a second name for its `scheme`, so inline
> text can be written the same way in both plugins and a scheme moved between them needs no
> rename. `scheme` keeps its filter meaning and is not deprecated — but a play that says
> `scheme` in both places is saying two different things.

Plus the standard file arguments — `mode`, `owner`, `group`, `seuser` and the rest — applied to
every produced file.

Both plugins are called `render`, so name the type when you ask for the documentation:

```bash
ansible-doc -t module stop_cran.namespace2xml.render
```

### Idempotence is measured, not estimated

The module renders into a scratch directory and compares every produced file against `dest`
**byte for byte**, writing only the ones that differ. That is exact rather than heuristic because
[§24](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md) makes the
tool's output deterministic for identical inputs. `check_mode` and `--diff` fall out of the same
comparison, so a dry run reports precisely what a real run would do.

Publication follows [§21.1](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md):
each file is written through a handle-relative, no-follow open and renamed into place, so a
symbolic link planted under `dest` is replaced rather than followed, and a write is never seen
half-finished. The module therefore targets POSIX nodes, and refuses to publish on a platform
that cannot provide those primitives rather than writing without containment.

Files already under `dest` that a render does not produce are left alone. The module never
deletes.

### A source file is never a destination file

The module **refuses** a render whose output would land on one of its own `src` paths. This is a
hard error, not a warning, and the reason is worth stating because the alternative failure is
invisible:
[§16.10](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md) defines
`merge=append` as rebasing each later sequence contribution onto fresh ordering values above the
current high-water mark. A run that reads back its own output therefore appends to what it just
appended — `["STDOUT"]`, then `["STDOUT", "FILE"]`, then `["STDOUT", "FILE", "FILE"]` — growing
by one copy per run and reporting `changed` forever. Keep the input pristine and send the output
somewhere else.

### `variables` reaches XML attributes

`variables` keys are namespace paths handed to the tool exactly as written, with no escaping in
the way. That is what makes `configuration.root.@level` mean the attribute — the same addressing
the filter reaches through
[`convention: xmltodict`](#4-xml-addressing-is-opt-in-and-only-one-convention-at-a-time), but here
with no convention to select because a module argument is already a path rather than a data key.

Values may be strings, numbers or booleans. A mapping or list is refused: synthesizing namespace
paths from nested data is the filter's job, and doing it here would put a key-encoding convention
in the way of an argument whose whole purpose is that there is none.

### Reading and rewriting XML

Round-tripping XML in the same format normally needs `xmlinputoptions=NormalizeFormattingWhitespace`.
Three things about it are easy to get wrong and each fails loudly:

- Without it, indentation between elements is parsed as content and the run is refused with
  `TYPE001`.
- It must be written **unqualified**. Input parsing happens before any output instance exists, so
  a selector-qualified input option such as `configuration.xmlinputoptions=…` is a blocking
  `SCHEME001` under
  [§16.8](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md).
- Enabling it emits `WARN007`, recording that the same-format round-trip guarantee is weakened.
  The module passes that warning through rather than suppressing it.

One more thing has no diagnostic to teach it: **an XML input's top-level namespace name is its
document element**. An overlay for `<configuration>` is rooted at `configuration`, and the scheme
needs `root=configuration` to write the single document element back out.

## Fidelity limits

These are limits of the **filter**, and specifically of the step that turns an Ansible data
structure into profile text. The module has none of them: its inputs are already namespace or
document files, so nothing is being synthesized from a mapping. The first three are inherent to
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

### 4. XML addressing is opt-in, and only one convention at a time

**This is the significant one.** The encoder is *total* — every key has an escaped spelling, so
every key reads back as itself. That guarantee is what costs the addressing: the three name forms
XML needs are exactly the three the encoder escapes away. So by default the filter renders
element-only XML, and a `@id` key produces a **blocking `XML002` error** naming
[§11.2](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md#112-supported-xml-subset)
rather than a silently wrong document.

Pass `convention: xmltodict` to read the markers instead of escaping them
([§11.4](https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md#114-canonical-xml-addressing)).
The spelling is the one the ecosystem already uses — `xmltodict`, `community.general.to_xml` and
the badgerfish style all write attributes this way:

| You want | Key | Renders |
|---|---|---|
| `<bean id="ds">` | `@id` | the attribute |
| `<bean n1:scope="x">` | `@Q{urn:p}scope` | the qualified attribute |
| `<b xmlns="urn:p">` | `Q{urn:p}b` | the qualified element |
| `<bean id="ds">text</bean>` | `#text` beside `@id` | the element's own text |
| `before<b/>after` | `#0`, `#1`, `#2` | mixed content, in that order |

```yaml
- ansible.builtin.copy:
    content: "{{ doc | stop_cran.namespace2xml.render('xml', root='beans', convention='xmltodict') }}"
    dest: /opt/app/beans.xml
  vars:
    doc:
      bean:
        '@id': dataSource
        '#text': jdbc:postgresql://db/app
```

Nothing you wrote before this existed changes: `escaped` is the default and encodes exactly as it
always did. **Both conventions are total** — under `xmltodict` a name that really does begin with a
marker takes a backslash, `\@x` for the name `@x`, and the backslash escapes itself, `\\@x` for the
name `\@x`. A scheme key and an input key have been two different languages since `*`; this follows
that line.

Two things it does not do:

- **`#text` beside child elements is refused.** §11.4 puts an element's own text at the element's
  path only while it has no child elements; once it has one the element is mixed and every content
  node takes an ordered part. A mapping does not record where the text stood, so write `#0` and
  `#1` explicitly rather than have the filter guess and render successfully with the text on the
  wrong side of a child.
- **Only one convention per call.** It applies to the whole input, not per subtree.

When the base document is a file on the node, the [`render` module](#the-render-module) with
`variables` remains the more direct answer — its keys are namespace paths passed through verbatim.

## Versioning

This collection is at 2.3.0 while the tool is at 3.x. They are separate artefacts with separate
compatibility promises: the collection pins no tool version, and the two are released under
different tags — `v3.*` for the tool, `ansible-v*` for this collection.

2.0.0 rather than 1.1.0 because 1.0.0 documented, as a requirement, that target nodes need neither
.NET nor the tool. The module makes that false for any play that uses it, and a promise about what
you must install is the kind a major version exists to revise. Nothing about the filter changed.

2.1.0 added `scheme_yaml` to both plugins and the filter's `convention` argument. Both are
additive: `scheme_text` is untouched and not deprecated, and `convention` defaults to the encoding
every earlier release used, so every 2.0.0 playbook renders identically.

2.2.0 changes no plugin behaviour. It ships the diagnostic code registry inside the collection as
`docs/diagnostics.md`, corrects documentation that had drifted from the code, and raises the
ansible-core floor from 2.14 to 2.15 — the first release that interprets the semantic markup the
plugin documentation is written in rather than printing it literally. That floor is why this is a
minor and not a patch: it narrows the range of Ansible you may run this on. Every 2.1.0 playbook
renders identically.

2.3.0 adds `scheme_text` to the filter as a second name for its `scheme`, so inline scheme text
is spelled the same in both plugins and moving a render between them no longer means renaming an
argument. It is additive — `scheme` is untouched and not deprecated — and it also fixes refusals
that quoted an argument name the caller had not written: a play using `scheme_yaml` or
`scheme_text` was told to fix `scheme`, which is not in the playbook. The remainder of the
release is documentation: the per-(host, task) cost of a render is now stated rather than left to
be discovered, closing [#113](https://github.com/stop-cran/namespace2xml/issues/113). Every 2.2.0
playbook renders identically.

## How this is tested

The unit tests compare the encoder against profile text **authored by hand from the
specification**, never captured from the tool. An expectation captured from the code under test
asserts only that the code still does what it did, which is the one thing that needs no test. The
integration tests then run the real tool over that same hand-authored profile and compare its
output against what the filter produced from the data — so the two sides can only agree if both
match the specification.

The module is held to the claim that matters for a module rather than for a template: that a second
run changes nothing. Its integration target renders, asserts the document, renders again and
requires `not changed` — so a tool that stopped being deterministic, or a comparison that stopped
being a byte comparison, fails there and nowhere else.

## Found a problem?

Good — that is what the preview is for, and the project is built to absorb it.

Every diagnostic these plugins surface carries a **stable code** and a **specification anchor**
naming the clause it enforces, so a disagreement can be reported precisely rather than described.
The codes are listed in `docs/diagnostics.md`, which ships inside this collection and is also
online at
[docs/diagnostics.md](https://github.com/stop-cran/namespace2xml/blob/master/docs/diagnostics.md).
Both plugins pass the tool's diagnostics through unchanged — neither rewrites or summarises them.

Before filing, ask one question: **what would have to change so this never surprises anyone
again?** The answer picks the form:

| Answer | File this |
|---|---|
| A plugin or the tool should have matched the specification | [Bug report](https://github.com/stop-cran/namespace2xml/issues/new?template=bug_report.yml) |
| The specification does not say, or says two things | [Specification ambiguity](https://github.com/stop-cran/namespace2xml/issues/new?template=spec_ambiguity.yml) |
| Both are right; I could not find out how to do this from the docs | [Usage gap](https://github.com/stop-cran/namespace2xml/issues/new?template=usage_gap.yml) |
| Neither a plugin nor the tool can express this at all | [Feature request](https://github.com/stop-cran/namespace2xml/issues/new?template=feature_request.yml) |

Set the **Component** field to `Ansible collection` so it reaches the right place, and say which
plugin was in the loop — they are both called `render`, and the fix lands in different files.

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
   Inline data, not a reference to your inventory. For the module, run it with
   `ansible-playbook -vvv` and attach the failing task's output: it carries the module's full
   result, including `tool_identity`, which names the binary that actually ran on the node.
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
