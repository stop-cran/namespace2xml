# A YAML scheme key carries a wildcard template

Acceptance items 18 and 19. Section 15, Section 10.4, Section 12.1, Section 15.2.

## What the inputs ask for

One namespace profile carries two children under `app`. One scheme, written as YAML, declares
`output` and `filename` under the key `'*'` nested inside `app`, and gives `filename` the value
`x-*.json`.

## What Section 10.4 requires

> YAML mapping keys must be strings. Each key becomes one qualified-name part under the
> Section 9.1 native-key rules: dots and `\u{HEX}` remain literal, the Section 11.4 markers apply
> to a key beginning with an unescaped `@`, `#`, or `Q{`, and a leading backslash escapes one of
> those and suppresses marker recognition. Elsewhere a backslash remains literal. Within that
> part, unescaped `*` and `*[identifier]` tokens use the wildcard-template grammar. `\*`
> contributes a literal asterisk.

The second sentence is what this fixture is for. A YAML key is *one* name part, so `'*'` cannot be
read as two; and within that part it is a wildcard, not the literal asterisk that `\*` would spell.
The scheme therefore declares the selector `app.*`, which Section 15.2 expands at pipeline step 13
against the concrete name graph, producing one output instance per child of `app`.

Section 12.1 supplies the value half:

> A scheme directive's value is decided the same way, from the captures its own pattern defines:
> its selector for the output-instance-scoped directives, its path for the path-scoped ones.

The selector defines one unnamed capture, so the `*` in `x-*.json` is that capture rather than
literal text, and the two instances are named from the two children.

## The discrimination

The two produced file names are the assertion, and they separate four wrong implementations:

- one that read the YAML key as literal text produces a single `x-*.json`, or a percent-escaped
  spelling of it, and no per-child expansion;
- one that expanded the selector but treated the value's `*` as literal produces one file name for
  two instances, which collide;
- one that read the key as a *path* rather than as one part — splitting on a character YAML does not
  give it — reaches no selector at all;
- one that substituted the wrong capture emits the names in the wrong pairing, which the differing
  payloads catch.

The payloads differ between the two children so the pairing is checked and not merely the names: a
transposed substitution would put `example.com` in `x-db.json`.

## Why YAML rather than JSON

Section 9.1 and Section 10.4 state the wildcard sentence separately, once per format, so proving one
does not prove the other. The sibling fixture `a-json-scheme-nests-directive-paths` exercises the
JSON projection; this one exercises YAML, and adds the wildcard clause that fixture leaves alone.
Quoting `'*'` is required by YAML itself — an unquoted `*` begins an alias — and the quotes are
YAML syntax that the parser removes, so the key the projection sees is the one character.

## Not asserted

That a `*` in a directive value other than `filename` is substituted; Section 12.1 requires it and
this version refuses it, which `KNOWN-LIMITS.md` records against issue #71. Nor an explicit
`*[identifier]` capture in a YAML key, which the same sentence admits.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 15, Section 10.4, Section 12.1. Section 24 for the difference that remains.
- Legacy observation: the baseline exits 0 and writes both `x-db.json` and `x-web.json` with the
  correct payload in each — the same two file names, from the same expansion, with the capture
  substituted the same way round. It differs only in Section 24's output bytes: CRLF and no trailing
  newline. **2.4.0 reads YAML scheme files and treats a `*` key as a wildcard template**, which is
  where the compatibility sentence in Section 10.4 comes from.
- Clean behavior: the same expansion and the same substitution, emitted under Section 24.
- The difference is intentional and confined to the byte layout. The value of this fixture is the
  agreement: Section 10.4's "retain their wildcard-template meaning for compatibility" names a
  behavior that exists in the field, and this case is the evidence that 3.0 kept it rather than
  reasoning that it should.
