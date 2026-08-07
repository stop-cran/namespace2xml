# `key` directive precedence follows source order in both directions

Acceptance item 19. Sections 15.2 and 16.5.

## What the inputs ask for

Two sibling mappings and three `key` directives, arranged so that one subtree sees a wildcard rule
before a specific one and the other sees a specific rule before a wildcard one.

```text
cfg.b.key=name
cfg.*.key=id
cfg.a.key=name
```

For `cfg.a`: the wildcard on line 3 matches, then the specific rule on line 4 matches. The later one
wins, so the generated field is `name`.

For `cfg.b`: the specific rule on line 2 matches, then the wildcard on line 3 matches. The later one
wins again, so the generated field is `id`.

Section 15.2:

> All scheme directives follow source order only.
>
> A later matching directive overrides an earlier matching directive for the same effective setting.
>
> Pattern specificity does not alter precedence.

Section 16.5 says the same thing about this directive in particular:

> If several directives match, the later directive wins. A later directive may therefore
> intentionally replace the key-field name chosen by an earlier wildcard rule.

## Why both orders are needed

A rule that scored patterns by specificity would produce `name` for both subtrees, and a rule that
took the first match rather than the last would produce `id` for `a` and `name` for `b`. Only source
order produces `name` for `a` and `id` for `b`. Either single subtree on its own is consistent with
one of the wrong rules; the pair is consistent with source order and nothing else.

This is why the specific rule for `b` is written first, before the wildcard, even though a scheme
author would more naturally write the wildcard first and specialize afterwards. The unnatural order
is the whole point of the case.

## Why the field name is the observable

The generated key field's *name* comes from the directive value, and Section 16.5 puts that field
first in each record, so which directive won is visible as flat key text without needing a format
that distinguishes scalar kinds. The record contents are identical either way, which keeps the
assertion about precedence alone.

## Not asserted

The unbound-directive warning from Section 15.2. All three directives bind: the wildcard matches
both subtrees and each specific rule matches its own, so an override is not an unbinding, and no
`WARN009` is expected. A directive that lost every override it took part in has still bound.

The exit code is 0.
