# A capture repeats for a shorter name and is ignored when unused

Acceptance item 6. Sections 12.1, 14.1, 16.2, and 16.3.

## What the inputs ask for

`a.*.*` defines two captures, and the single input `a.p.q.x=1` binds them to `p` and `q`. Two
directive values then deliberately mismatch that arity:

```text
a.*.*.filename=*.conf
a.*.*.root=*-*-*
```

One substitution against two captures, and three against two.

## What this asserts

**Fewer substitutions than captures: the extras are ignored.** Section 12.1: "If it contains fewer,
unused captures are ignored." So `*.conf` fills from the first capture alone and the file is
`p.conf`. Consuming the captures from the right instead would give `q.conf`; refusing the arity
mismatch would give no file at all.

**More substitutions than captures: the last one repeats.** Section 12.1: "If a legacy value
contains more wildcard substitutions than the name produced, the last capture is repeated for
compatibility." The three positions in `*-*-*` therefore take `p`, `q`, and `q` again, giving the
root `p-q-q`. Cycling instead would give `p-q-p`, and clamping to the arity would give `p-q`.

Both rules are exercised in one case on purpose: they are the two halves of one sentence pair, and a
build that implemented positional substitution with a hard arity check would fail both at once while
passing every equal-arity case in the corpus.

**The two rules compose without interfering.** The same capture tuple feeds both values, and each
resolves its own arity independently. Sharing one exhausted iterator between directive values would
make the second one start where the first stopped, and `root` would become `q-q-q`.

**Neither `-` nor the repetition creates a name level.** Section 16.3 gives the root value the name
grammar, in which levels are separated by `.`; `p-q-q` contains none, so it is one part and the
default `.` delimiter joins it to `x` as `p-q-q.x`.

## Not asserted

Whether the repeat-last rule applies to the explicit `*[identifier]` form. It does not — Section
12.2 resolves each identifier by name — but that belongs with the explicit-capture cases.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 12.1 positional substitution, repeat-last, and ignore-unused; Section 16.2 explicit `filename`; Section 16.3 `root`; Section 26 item 6.
- Legacy observation: the baseline exits `0` and writes `p.conf`, so it agrees on the ignore-unused rule and on the explicit filename. The file contains `x=1` — the `root` directive produced nothing at all. Its bytes are CRLF-terminated under the Section 24 divergence.
- Clean behavior: `p.conf` containing `p-q-q.x=1`, LF-terminated.
- Why the divergence is the specified one: Section 16.3 makes `root` wrap the selected content unconditionally, and Section 12.1 supplies the text it wraps with. Dropping the directive because its value needed substitution is the silent-discard outcome Section 6.3 rules out; the same scheme with a literal `root` is honoured by 2.4.0, so what the baseline loses is the substitution rather than the directive.
