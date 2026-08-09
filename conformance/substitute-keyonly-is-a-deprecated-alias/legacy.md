# `keyOnly` is a deprecated alias for `substitute=Key`

Acceptance item 18. Section 15.3, Section 16.7, Section 13.4.

## What the inputs ask for

`cfg.raw.substitute=keyOnly` is the legacy spelling of `substitute=Key`, written at one path whose
value is a single reference.

## What Section 15.3 requires

The alias is accepted, and its use is reported once. Appendix B maps a deprecated alias to
`WARN002`, whose cardinality is "once per alias category and scheme". One occurrence produces one
warning, and the run continues: a warning is not blocking, so the exit code is 0 and the destination
is written.

## The discriminations

**The alias is honoured, not merely tolerated.** Section 16.7 gives `Key` a value column of "no", so
`cfg.raw` emits the text `${lit}` rather than the referent `X`. Section 19.1 encodes a literal
reference start as `\${`, so the expected line reads `raw=\${lit}`. An implementation that warned
and then ignored the directive would emit `raw=X`.

**`keyOnly` is a value alias, not a directive-name alias.** The `declaration` member names
`cfg.raw.substitute`, because that is the directive as written; only its value used the deprecated
spelling. This is the same shape Section 15.3's legacy `type` values take, and it is why
`SchemeAlias` distinguishes an alias that names a directive from one that names a value.

**One warning, not one per occurrence.** Only one directive is written here, so the count alone does
not prove the cardinality rule; what this fixture pins is the field set and the phase. The rule
itself is unit-tested with two occurrences in one scheme.

## Why `phase` is `scheme`

The alias is recognized while step 3 compiles the `substitute` directives, before any input value is
lexed at step 6. A `WARN002` reported in phase `input` would mean the alias had been carried
uninterpreted into the input phase, which is exactly what step 3 exists to prevent.

## Not asserted

That an unrecognized mode name is `SCHEME001`, which is unit-tested. Nor the other three Section
15.3 alias categories, which have their own coverage.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 15.3, 16.7, and 19.1; Section 6.4 for the diagnostic stream.
- Legacy observation: the baseline exits 0 and writes `cfg.properties` containing `raw=${lit}`. It
  accepted `keyOnly` and applied it — the reference was left uninterpreted — and said nothing about
  the spelling. 2.4.0 had no diagnostic stream and no deprecation vocabulary, so an author using a
  legacy spelling had no way to learn it was legacy.
- Clean behavior: the alias is still accepted, so no existing scheme breaks, and `WARN002` names the
  spelling and its replacement once per scheme. The value is emitted as `\${lit}` under Section
  19.1's total encoding.
- The difference is intentional in both parts. Section 3.1 keeps "deprecated aliases listed in this
  specification" working, and Section 6.4 exists so that a run can tell an author what it silently
  tolerated. A deprecation nobody is told about cannot be acted on before the release that removes
  it.
