# Usage methodology

How to use namespace2xml well, and when not to use it at all.

This document holds **judgments**, not rules. Rules live in `docs/specification.md`, where they are
enforced. The material here is the part that should stay soft: moving it into the code would freeze
it, and it is exactly the part that should keep learning from use.

> **Status:** outline. The layering guidance below is settled; the worked pipelines are not written
> yet. See [KNOWN-LIMITS.md](../KNOWN-LIMITS.md) §5. If you worked something out yourself that should
> have been here, that is a
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

## 3. Scheme files are contracts, not configuration

A scheme file says what shape the world has: which subtrees become which formats, what is an element
and what is an attribute, what is hidden. Downstream systems depend on those answers.

Treat scheme files accordingly. Version them next to the code that consumes their output, review
changes to them, and hold them to the same change protocol as anything else
([CONTRIBUTING.md](../CONTRIBUTING.md) C1). A scheme change with no corresponding review is a silent
interface change.

## 4. Exploit determinism

Byte-identical output is not a nice property to have; it is a technique.

Because the same inputs always produce the same bytes, you can **commit the generated artifacts** and
let code review show you the effect of a configuration change as a diff. Configuration drift stops
being an operational mystery and becomes a pull request comment. A CI job that regenerates and fails
on any difference turns "someone edited the generated file by hand" into a build failure instead of
an outage six weeks later.

This is worth designing a pipeline around, and it is the main reason determinism is a precondition
in this project rather than a feature.

## 5. Automate against the machine interface, not the prose

If a program consumes this tool's output, have it consume the *contract*:

- `--diagnostics-format json` for the diagnostic stream, validated against
  `spec/diagnostic-stream.schema.json`;
- the stable diagnostic `code` for control flow, never the message text, which is prose and may be
  reworded;
- the `spec` anchor on each diagnostic when you need to know *why*;
- the `contract-bundle` revision from `--version` recorded in your logs, so a future failure can be
  attributed to a contract change rather than guessed at.

## 6. A candidate, not a principle

There is a general shape visible in this project: a specification owning an implementation through an
independent oracle, with a feedback route that revises the specification rather than the code.

It is filed here as a **candidate**, and it stays a candidate until a second, independent grounding
in different material earns it. One success is a singular; promoting it now would be reaching for a
universal by enumeration. See [CONTRIBUTING.md §10](../CONTRIBUTING.md#10-promotion-restraint).
