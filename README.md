# namespace2xml

[![NuGet](https://img.shields.io/nuget/v/namespace2xml.svg)](https://www.nuget.org/packages/namespace2xml)
[![Ansible Galaxy](https://img.shields.io/ansible/collection/v/stop_cran/namespace2xml?label=galaxy)](https://galaxy.ansible.com/ui/repo/published/stop_cran/namespace2xml/)
[![CI](../../actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)

A deterministic configuration transformer. It reads ordered namespace profiles and structured
inputs, applies scheme directives, and renders **many outputs from one overlaid model** — XML, JSON,
YAML, INI, namespace profiles and quoted-namespace files.

Identical inputs always produce byte-identical outputs, on every supported platform, in every
locale, on every run.

---

## Version 3.0 is a rewrite

3.0 replaces the 2.x implementation entirely, against a specification written **before** the code.
The specification is the contract; the implementation is an attempt to satisfy it. Behaviour that
2.4.0 left undefined is now defined, and the intentional differences are listed in
[docs/migration-2.x-to-3.0.md](docs/migration-2.x-to-3.0.md).

`3.0.0-preview.N` is a preview line. It is meant to be used, reported against, and revised. See
[KNOWN-LIMITS.md](KNOWN-LIMITS.md) for what it does not do yet.

---

## Install

```
dotnet tool install --global namespace2xml --prerelease
```

`--prerelease` is required while the 3.0 line is in preview; without it NuGet resolves 2.4.0, which
takes the same arguments under the previous contract.

To use it from Ansible, add the [`stop_cran.namespace2xml`](https://galaxy.ansible.com/ui/repo/published/stop_cran/namespace2xml/)
collection. The filter evaluates on the controller, which is where the tool is needed; target nodes
need neither .NET nor the tool:

```
ansible-galaxy collection install stop_cran.namespace2xml
```

See [ansible/README.md](ansible/README.md) for arguments, memoization and the fidelity limits of the
data-to-profile mapping.

## Basic usage

```
namespace2xml -i <input files> -s <scheme files> [-o <output directory>]
```

Input files carry the data. Scheme files describe what to produce from it: which output formats,
which parts become XML elements or attributes, which keys are hidden, and so on.

### One model, many formats

`test.properties`:

```
a.b.x=1
```

`scheme.properties`:

```
a.output=xml,json,yaml,ini,namespace
```

```
namespace2xml -i test.properties -s scheme.properties
```

produces `a.xml`, `a.json`, `a.yaml`, `a.ini` and `a.properties`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<b>
  <x>1</x>
</b>
```

```json
{
  "b": {
    "x": 1
  }
}
```

```yaml
b:
  x: 1
```

```ini
[b]
x=1
```

```
b.x=1
```

The output selector `a` names what to render, and Section 16.3 removes it before rendering, so
`b` is what each document contains. Add `root` to wrap the result in a name of your choosing.

Scalars become elements in XML unless a scheme directive asks otherwise. Section 16.6 makes
attributes an explicit opt-in, and the choice is scoped to the formats that have attributes:

```
a.output=xml,json
a.b.x.type=attribute
```

renders `<b x="1" />` in XML while JSON keeps `"x": 1` as an ordinary member.

### Overlaying

Inputs are applied in command-line order, and later files win:

```
namespace2xml -i base.properties -i production.properties -i secrets.properties -s scheme.properties
```

Layer by lifetime — base, then environment, then instance, then secrets — not by topic. See
[docs/usage-methodology.md](docs/usage-methodology.md) for why, and for the anti-patterns that
follow from getting it backwards.

### Reading XML that was formatted for humans

Indented XML holds whitespace-only text between element children, and the default
`xmlinputoptions=PreserveWhitespace` keeps every text node. Those become content components, so

```xml
<r>
  <b>1</b>
</r>
```

is the model `r.#0`, `r.#1.b`, `r.#2` — and **not** `r.b`. Nothing warns about this: an override
written `r.b=2` becomes a new node beside `r.#1.b` rather than a replacement of it, and the run
still exits `0`.

When the input was formatted for a human to read, ask for the compatibility mode:

```
xmlinputoptions=NormalizeFormattingWhitespace
```

It discards whitespace-only text between element children, which makes those elements addressable
by name, and warns once per document (`WARN007`) because specification Section 11.7 says discarding
that text weakens the same-format round-trip guarantee. That trade is the reason it is opt-in:
preserving every byte is what makes an unmodified round trip byte-identical, and only you know
whether the file you are reading is data or layout.

[docs/format-xml.md](docs/format-xml.md) covers the whitespace modes in full, and
[docs/usage-methodology.md](docs/usage-methodology.md) works through the override that this defeats.

---

## For automation and AI agents

This tool is designed to be used by programs, and to be **argued with** by them.

- **`--diagnostics-format json`** writes the entire diagnostic stream to standard error as one
  canonical JSON array conforming to [`spec/diagnostic-stream.schema.json`](spec/diagnostic-stream.schema.json).
  Operational log messages are suppressed in that mode, so standard error is pure data. The array is
  written once, at exit, so it is always complete and always well-formed.
- **Every diagnostic carries a stable code and a specification anchor** naming the clause it
  enforces, so a disagreement can be reported precisely rather than described. See
  [docs/diagnostics.md](docs/diagnostics.md).
- **`--version`** prints one `<field>: <value>` line per field, including the `contract-bundle`
  revision that identifies exactly which specification and diagnostic registry the binary implements.
- **Exit codes are contractual.** `0` is success, including success with warnings; `1` is failure.
  Specification Section 6.3 fixes those two and no others. During the `3.0.0-preview` line a third
  code, **`70`**, means *this preview has not implemented the requested work* — the pipeline was
  never entered, no destination was written, and nothing about the input has been judged. An agent
  must treat `70` as "come back later", never as a failure of the configuration it supplied. It
  disappears at `3.0.0`; a released build returns only `0` or `1`.
- **The specification ships inside the package**, so an agent can read the contract offline.
- **Symbols and source link** are published alongside every release, so a stack trace resolves to
  the exact source that produced it.
- **An Ansible collection** wraps all of the above for playbook authors, and keeps the guarantees:
  diagnostics reach the caller unchanged, and a binary that reports no `contract-bundle` is refused
  rather than used. `stop_cran.namespace2xml` ships two plugins — a `render` filter that renders
  play variables on the controller, and a `render` module that renders a managed node's own files
  in place, idempotently. `ansible-doc -t filter` and `-t module` on
  `stop_cran.namespace2xml.render` are their argument references.

Start at [AGENTS.md](AGENTS.md). The machine-readable index is [llms.txt](llms.txt).

---

## Documentation

| File | What it is |
|---|---|
| [docs/specification.md](docs/specification.md) | **The contract.** Normative and self-contained. |
| [docs/diagnostics.md](docs/diagnostics.md) | Every diagnostic code, its meaning and its anchor. |
| [docs/usage-methodology.md](docs/usage-methodology.md) | When to use this tool, how to layer, how to specialize a document you did not write, what not to do. |
| [docs/format-namespace.md](docs/format-namespace.md) | The namespace profile: syntax, escapes, comments, references. |
| [docs/format-json.md](docs/format-json.md) | JSON input and output, scalar kinds, the numeric-map trap. |
| [docs/format-yaml.md](docs/format-yaml.md) | YAML input and output, the `RestrictedYaml1` subset. |
| [docs/format-xml.md](docs/format-xml.md) | XML input and output, typed components, CDATA, comments. |
| [docs/format-ini.md](docs/format-ini.md) | INI output, the two-level projection, `PortableIni1`. |
| [docs/migration-2.x-to-3.0.md](docs/migration-2.x-to-3.0.md) | Every intentional behaviour change from 2.4.0. |
| [CONTRIBUTING.md](CONTRIBUTING.md) | The change protocol and the feedback forms. |
| [KNOWN-LIMITS.md](KNOWN-LIMITS.md) | What is deliberately not covered yet. |
| [ansible/README.md](ansible/README.md) | The `stop_cran.namespace2xml` Ansible collection: the `render` filter and the `render` module, which one to pick, their arguments, and the filter's fidelity limits. |
| [AGENTS.md](AGENTS.md) | Entry point for automated agents. |

---

## Found a problem?

Good — that is what the preview is for, and the project is built to absorb it.

Before filing, ask one question: **what would have to change so this never surprises anyone again?**

| Answer | File this |
|---|---|
| The code should have matched the specification | [Bug report](../../issues/new?template=bug_report.yml) |
| The specification does not say, or says two things | [Specification ambiguity](../../issues/new?template=spec_ambiguity.yml) |
| Both are right; I could not find out how to do this | [Usage gap](../../issues/new?template=usage_gap.yml) |
| The tool cannot express this at all | [Feature request](../../issues/new?template=feature_request.yml) |

Always include the `contract-bundle` revision from `--version`. A report against an unknown contract
revision cannot be acted on. Set the **Component** field to say which surface you were using — the
CLI or the Ansible collection — and for the collection add `ansible --version` and the collection
version.

Full guidance, including the report form and the rules for agent-authored reports, is in
[CONTRIBUTING.md](CONTRIBUTING.md#4-the-feedback-channel-binding).

---

## Building from source

```
dotnet build namespace2xml.slnx
dotnet test  namespace2xml.slnx
```

Requires the .NET 10 SDK. The solution uses the `.slnx` format.

## License

[MIT](LICENSE).
