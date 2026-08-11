# `type=string` forces scalar rendering in the document views

Acceptance items 9 and 18. Sections 16.6, 13.2, 16.3 and 19.4.

## What the directive is asked to do

Section 16.6 `string`:

> Forces scalar rendering as a string in the selected output view. It does not change input scalar
> inference or the typed value forwarded through references.

Both sentences are assertable, and the second one is the reason this fixture carries references at
all. A directive that only ever ran on a literal could satisfy the first sentence while quietly
rewriting every referent, and nothing in a scalar-only case would notice.

## The forcing half

`svc.port=8443`, `svc.enabled=true` and `svc.ratio=1.5` are inferred as integer, Boolean and decimal
by Section 12. `port` and `enabled` carry `type=string`; `ratio` does not.

The rendered string is the Section 13.2 canonical interpolation text of the value inference already
settled on, because Section 16.6 forbids changing that inference:

- integer is "base-10 without leading zeros except `0`", so `8443` is `"8443"`;
- Boolean is "lowercase `true` or `false`", so `"true"`;
- decimal is "exactly the canonical decimal algorithm in Section 18", so `"1.5"`.

`ratio` is the control. It proves the directive is scoped to the paths that carry it rather than
applied to the subtree, and it stays a JSON number.

`svc.label=alpha` is already a string, and `type=string` on it is required to be a no-op rather than
a second round of quoting or escaping. `"alpha"` in JSON and a plain `alpha` in YAML are what a
directive that idempotently forces an existing string produces.

An author who writes `0755` and asks for a string gets `"755"`, not `"0755"`. That follows from the
same clause: the leading zero is gone at Section 12 inference, and re-deriving it at render time
would be exactly the change to inference Section 16.6 rules out. This fixture does not assert it,
because `docs/format-namespace.md` covers source-spelling loss and the case would only restate
Section 13.2.

## The reference half, in both directions

Section 13.2 states the two rules that pull in opposite directions, and this fixture puts one value
under each.

`svc.copy=${svc.port}` has no directive of its own, and `svc.port` has `type=string`. Section 13.2:

> References copy the typed value produced by input parsing and scalar inference, before
> output-specific `type` transformations.

So `copy` renders as the JSON number `8443` even though the node it names renders as `"8443"` two
lines above it. The directive did not travel along the reference.

`svc.echo=${svc.ratio}` carries `type=string` while `svc.ratio` does not. Section 13.2:

> An exact reference later matched by `type=string` is rendered as a string without changing the
> referenced source value.

So `echo` is `"1.5"` and `ratio` is still `1.5`. The directive applied at the referring node and did
not travel backwards either.

Between them these two values fail against any implementation that propagates the directive across a
reference in either direction, and against one that resolves references after applying `type`.

## Why YAML is not a restatement of JSON

Section 19.4 makes the two formats disagree about what a forced string looks like:

> A string whose plain spelling would resolve to a non-string kind under `RestrictedYaml1` is emitted
> single-quoted, with a literal single quote doubled as `''`.

`8443`, `true` and `1.5` all resolve to non-string kinds, so all three are single-quoted; `alpha`
does not, so it is plain. A YAML writer that forced the string but emitted it plain would produce a
document that reads back as the number it was asked to stop being, and the assertion would be
silently void. The quoting is the point, not decoration.

Section 19.4 also "preserves mapping and sequence order", so the six keys appear in Section 5.2
source order rather than sorted.

## Why `api` is in the fixture

`api.root=x.y` wraps the content two levels deep, per Section 16.3: "JSON emits `{"x":{"y":...}}`".
`api.timeout` carries `type=string`, and the directive is written against the scheme path, not
against the wrapped output path.

An implementation that looks the directive up by the path it is rendering rather than by the path the
scheme addressed will search for `x.y.timeout`, find nothing, and emit the number `30`. That is a
real failure mode -- the XML projection already had to solve it -- and no unwrapped case can reach
it. `"30"` under two levels of wrapping is the assertion.

## Not asserted

What `type=string` means for a null payload. Section 16.6 does not say, and Section 19 deliberately
lets each format spell null differently, so this corpus does not invent an answer.

The exit code is 0 and no diagnostic is emitted. Every directive binds to a path that exists and no
shape conflict arises.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 16.6 `string`; Section 13.2 typed reference forwarding; Section 16.3 `root`; Section 19.4 YAML ordering and quoting; Section 26 items 9 and 18.
- Legacy observation: the baseline exits `0` and writes all three files. `svc.json` is byte-identical to the expected output, so 2.4.0 implements the scalar-forcing and both reference directions exactly as specified. `svc.yaml` holds the same six values with the same quoting but sorted alphabetically -- `copy, echo, enabled, label, port, ratio` -- rather than in source order. `api.json` is `{"timeout": "30"}`: the `root=x.y` wrapping is absent entirely.
- Clean behavior: all three files render as specified, with YAML in source order and `api.json` wrapped as `{"x":{"y":{"timeout":"30"}}}`.
- Why the difference is intentional: the two differences are unrelated to `type=string`, which the baseline already gets right, and this fixture exists to keep it that way. Section 19.4 requires YAML to preserve mapping order, and the baseline's alphabetical sort discards the ordering the input established, which no author can recover. Section 16.3 states the `root` rendering for JSON in normative form and the baseline ignores it for this format, so a scheme that is portable across the baseline's own output formats stops being portable at JSON. Recording the agreement on `svc.json` matters as much as the differences: it establishes that a v3 regression here would be a regression against 2.4.0 as well as against the specification.