# Usage methodology

How to use namespace2xml well, and when not to use it at all.

This document holds **judgments**, not rules. Rules live in `docs/specification.md`, where they are
enforced. The material here is the part that should stay soft: moving it into the code would freeze
it, and it is exactly the part that should keep learning from use.

> **Status:** partial. Sections 1–3 and 6 are written from practice; the multi-format worked
> pipelines are still thin. See [KNOWN-LIMITS.md](../KNOWN-LIMITS.md) §5. If you worked something out
> yourself that should have been here, that is a
> [usage gap report](https://github.com/stop-cran/namespace2xml/issues/new?template=usage_gap.yml)
> and it is the most valuable kind.

---

## 1. When to reach for this tool

namespace2xml is for **many outputs from one overlaid model**.

Reach for it when:

- the same facts have to appear in several files, in several formats, and drift between them is a
  real and recurring problem;
- configuration is assembled from layers with different lifetimes — a base that rarely changes, an
  environment that changes per deployment target, secrets that change independently;
- you want generated artifacts committed and diffed in review.

Do **not** reach for it when:

- there is one output file, in one format, with no overlays. A template engine will make you happier
  and your successor will thank you.
- the transformation needs real computation — conditionals over data, loops, arithmetic beyond
  simple references. This tool deliberately has no programming language in it. If you find yourself
  wishing it did, that is a signal to generate the input profile with a program and keep this tool
  for the rendering step.
- the output format is not one the tool renders. Adding a format is a specification change, not a
  configuration one.

## 2. Layer by lifetime, not by topic

Inputs are applied in command-line order and later files win. Choose the order by **how often each
layer changes**, not by what it is about:

```
namespace2xml \
  -i base.properties \
  -i env/production.properties \
  -i instance/eu-west-1.properties \
  -i secrets.properties \
  -s scheme.properties
```

Base rarely changes. Environment changes per target. Instance changes per deployment. Secrets change
on their own schedule and come from a different place. Because last-wins is deterministic and
positional, that order makes "which layer set this value" answerable by inspection.

### The anti-pattern

Encoding the environment in the *namespace* instead of in the overlay order:

```
production.service.timeout=30
staging.service.timeout=5
```

This works, right up until you need a third environment that is mostly like production, or a value
that varies along two dimensions at once. Then it is a rewrite rather than a change, because the
distinction you needed was never in the layering where it could be composed — it was baked into the
key.

The rule of thumb: **anything that selects a variant belongs in overlay order; anything that names a
thing belongs in the namespace.**

## 3. Specializing a document you did not write

Section 2 assumes you own every input. The commoner case is that you do not: there is an
`app.xml`, a `pom.xml` or an `application.yaml` that something else generated, or that predates
everyone, and you need two values different per environment. That case is the strongest thing this
tool does — the foreign document is simply another input, and an overlay in *any* format can reach
into it and override a single leaf without reproducing the rest.

```text
namespace2xml -i app.xml -i env/dev.properties -s scheme.properties -o out
```

It also has the sharpest edges, because you are now writing paths into a namespace you did not
design. Three of them are worth knowing before you start, and one habit avoids all three.

### Read the model before you override it

The tool's own namespace output is the best documentation of a foreign document that exists,
because it is the model, not a description of it. Before writing a single override, render the
document to `namespace` and look at what the paths are actually called:

```text
# scheme.properties
server.output=namespace
```

The answer is frequently not what the XML looked like. This one habit prevents the next three
problems, and it costs one run.

### Indentation in the source is data, and it will silently defeat your override

An XML file laid out for humans has whitespace between its elements, and by default that
whitespace is *content*, addressed with `#0`, `#2` and so on (§11.7). Once an element sits among
content, it is nested under a content-token position too. So for

```xml
<server>
  <endpoint domain="example.com" port="8080" />
</server>
```

the endpoint is not at `server.endpoint`. It is at `server.#1.endpoint`, with `#0` and `#2` holding
the two newlines on either side of it.

That is not a cosmetic detail, because an overlay written against the obvious path still parses,
still binds, and still does nothing you wanted:

```text
# dev.properties
server.endpoint.@domain=dev.example.com
```

```text
# what the model actually contains
r.#0=\n  
r.#1.endpoint.@domain=example.com
r.#1.endpoint.@port=8080
r.#2=\n
r.endpoint.@domain=dev.example.com
```

The last line is the override, sitting in a new element of its own beside the real one, which is
untouched. The spelling was right — `@domain`, not `domain` — and it still missed. This is
[#40](https://github.com/stop-cran/namespace2xml/issues/40), and it is the reason the first habit
in this section is to render the model before writing anything.

If the document's layout is not itself meaningful — which, for configuration, it usually is not —
say so once, at the root:

```text
xmlinputoptions=NormalizeFormattingWhitespace
```

The content tokens disappear, `server.endpoint.@domain` becomes the real path, and the override
lands. The tool will tell you what that cost:

```text
warning WARN007 §11.7: whitespace-only text between element children was discarded as
formatting indentation, because 'xmlinputoptions=NormalizeFormattingWhitespace' asked for it.
Section 11.7 weakens the normalized same-format round-trip guarantee for this document.
```

Read that warning as a decision you made rather than noise to suppress. It is accurate: after
discarding layout the tool can no longer promise to give back the bytes it was given. For a file
you are regenerating anyway, that is exactly the trade you want. For a document whose whitespace
carries meaning — mixed content, a `<pre>`, anything where the spaces are the point — it is not,
and you should address the `#n` positions directly instead.

### A dot in an element name is a separator, and the obvious spelling builds a second tree

The trap above is about where an element sits. This one is about what it is called, and it has the
same ending. `.` separates name parts (§8.2), and XML lets an element name contain one, so an
`app.config` reads like this:

```xml
<configuration>
  <system.web>
    <compilation debug="false" />
  </system.web>
</configuration>
```

`system.web` is **one** name part containing a dot, not two parts. The override a person writes
from looking at the file:

```text
configuration.system.web.compilation.@debug=true
```

names four parts, none of which exists. Nothing is malformed, so §17.1 creates them, and the run
exits `0` with an empty diagnostic stream while producing a document that has the original element
untouched and a `<system><web>` subtree beside it. The escape is what expresses the intent:

```text
configuration.system\.web.compilation.@debug=true
```

Two spellings work — `\.` and `\u{2E}` — and they are the same name part, so it does not matter
which you write. It does matter which you *read*: the namespace writer always emits `\u{2E}`
(§16.4), so a rendered model shows `system\u{2E}web` and never `system\.web`. If that spelling
looks like noise, it is the delimiter telling you it is part of a name.

There is no diagnostic for this and none is planned; `docs/format-xml.md` explains why, and what
`WARN011` does and does not cover. This is
[#95](https://github.com/stop-cran/namespace2xml/issues/95), and it is the second reason the first
habit in this section is to render the model before writing anything.

One wrinkle is worth knowing, because it can teach the wrong lesson. Without `root`, the phantom
subtree makes the view multi-rooted and XML refuses it with `TYPE001` — a real signal that
something you did not intend is in the model. The remedy that error names is `root`, the shape
genuinely needs `root`, and adding it makes the signal disappear while the phantom stays. A clean
exit is not evidence that an address bound.

### An XML attribute is `@name`, and the bare name silently means something else

`<endpoint domain="example.com"/>` addresses as `endpoint.@domain`. Writing the override without
the marker does not fail — it creates an ordinary sibling beside the untouched attribute:

```text
# a.b.x=dev.example.com against <a><b x="base.example.com"/></a>
<b x="base.example.com">
  <x>dev.example.com</x>
</b>
```

Exit `0`, and an empty diagnostic stream. Like the indentation trap above it fails without saying
anything, it is what a 2.x profile does when it is moved to 3.0 unchanged — 2.4.0 addressed that
attribute as `a.b.x` and overrode it — and it is tracked as
[#56](https://github.com/stop-cran/namespace2xml/issues/56). Write `@domain`.

The asymmetry worth remembering: a *reference* resolves `${a.x}` to the attribute through the
Section 13.1 alias index, but an *assignment* to `a.x` does not. Reads forgive the bare name and
writes do not, so prefer the canonical spelling everywhere and the question never arises.

### The output selector prefix is removed, so put the document element back

`root` is not decoration. Selecting `server` as an output strips `server` from the paths beneath
it (§16.3), which for XML leaves the endpoint as the document element. If you want the original
shape back, say so:

```text
xmlinputoptions=NormalizeFormattingWhitespace
server.output=xml
server.root=server
server.filename=app.xml
```

```xml
<?xml version="1.0" encoding="utf-8"?>
<server>
  <endpoint port="8080" domain="dev.example.com" />
</server>
```

That is the whole pipeline, and it is worth noticing what is *not* in it: the overlay named one
leaf. Everything else in the document survived without being mentioned.

Note the attribute order — `port` before `domain`, where the source had `domain` first. An
overridden attribute moves to the winning contribution's position. Ordering is deterministic and
specified, but it is not preserved, so a reviewer diffing generated XML should expect it once.

## 4. Scheme files are contracts, not configuration

A scheme file says what shape the world has: which subtrees become which formats, what is an element
and what is an attribute, which key becomes a mapping's identity, and which subtrees are dropped
with `output=ignore`. Downstream systems depend on those answers.

Treat scheme files accordingly. Version them next to the code that consumes their output, review
changes to them, and hold them to the same change protocol as anything else
([CONTRIBUTING.md](../CONTRIBUTING.md) C1). A scheme change with no corresponding review is a silent
interface change.

## 5. Exploit determinism

Byte-identical output is not a nice property to have; it is a technique.

Because the same inputs always produce the same bytes, you can **commit the generated artifacts** and
let code review show you the effect of a configuration change as a diff. Configuration drift stops
being an operational mystery and becomes a pull request comment. A CI job that regenerates and fails
on any difference turns "someone edited the generated file by hand" into a build failure instead of
an outage six weeks later.

This is worth designing a pipeline around, and it is the main reason determinism is a precondition
in this project rather than a feature.

## 6. Pin the behaviour you depend on

Determinism lets you commit generated artifacts. The step past that is to commit a *small* one on
purpose.

This project owns its implementation through an independent corpus: fixtures written from the
specification, compared byte for byte, which fail when behaviour moves whether or not anyone meant
it to. The same technique is available to you at a much smaller scale, and it is worth the twenty
minutes.

Keep a handful of input/expected pairs beside your configuration — one per behaviour you would be
hurt by losing. A CI step that regenerates them and fails on any difference then tells you, at
upgrade time, precisely which of your assumptions this tool has stopped honouring. Without it you
find out at deploy time, in a diff of ten thousand generated lines where the one that changed is
not the one you are looking at.

Two rules make the difference between a fixture that protects you and one that does not:

- **Write the expected output by hand, from what you meant.** A file captured from a tool run
  records what the tool did, which it will keep agreeing with after it starts doing the wrong
  thing. That is the single easiest way to build a suite that cannot fail.
- **Pin the behaviour, not the surroundings.** A fixture over your whole production configuration
  fails on every ordinary change and will be deleted within a month. One that pins "an override in
  `dev.properties` reaches this attribute" survives, because it only fails when that stops being
  true.

Record the `contract-bundle` revision from `--version` next to them. When a pinned expectation
does change, that revision is what turns "something broke" into "the contract moved, here".

## 7. Automate against the machine interface, not the prose

If a program consumes this tool's output, have it consume the *contract*:

- `--diagnostics-format json` for the diagnostic stream, validated against
  `spec/diagnostic-stream.schema.json`;
- the stable diagnostic `code` for control flow, never the message text, which is prose and may be
  reworded;
- the `spec` anchor on each diagnostic when you need to know *why*;
- the `contract-bundle` revision from `--version` recorded in your logs, so a future failure can be
  attributed to a contract change rather than guessed at.

## 8. A candidate, not a principle

There is a general shape visible in this project: a specification owning an implementation through an
independent oracle, with a feedback route that revises the specification rather than the code.

It is filed here as a **candidate**, and it stays a candidate until a second, independent grounding
in different material earns it. One success is a singular; promoting it now would be reaching for a
universal by enumeration. See [CONTRIBUTING.md §10](../CONTRIBUTING.md#10-promotion-restraint).
