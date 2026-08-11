# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits `-532462766` (0xE0434352, an unhandled CLR
  exception) and writes nothing. The terminating exception is
  `System.ArgumentException: Requested value 'arrby' was not found.` from `Enum.Parse` in
  `Formatters/Extensions.cs:72`, reached through `SchemeNodeExtensions.WithImplicitArrays`.
- Contract: Section 12.1's exclusion of `type` from capture substitution, Section 16.6's closed
  type-name set, and Section 22's `SCHEME001` cardinality of once per declaration. Section 6.3
  admits only exits 0 and 1, and Section 3.2 lists behaviour "caused by unhandled user-input
  exceptions" among the corrections.
- Legacy observation: 2.4.0 substituted the capture into the `type` value. `arr*y` matched
  `cfg.b`, the capture text `b` was spliced in, and the resulting `arrby` was handed to
  `Enum.Parse` unguarded. The failure is therefore data-dependent, which the following control
  measures: with `cfg.b.x=2` removed so that only `cfg.a.x=1` remains, the same scheme line
  produces the capture text `a`, `arr*y` becomes `array`, and 2.4.0 exits 0 and writes
  `cfg.properties` containing `a.x=1` — silently applying `type=array`.

  | Input profile | 2.4.0 result |
  |---|---|
  | `cfg.a.x=1` and `cfg.b.x=2` | unhandled `ArgumentException`, exit `-532462766`, nothing written |
  | `cfg.a.x=1` alone | exit 0, `cfg.properties` = `a.x=1`, `type=array` silently applied |

  One scheme line is thus either a working directive or a process crash depending on which data
  it matches. This is the accident Section 12.1 names: "a capture could complete either only by
  accident of the matched data."
- Clean behavior: capture recognition is disabled in a `type` value whatever the selector
  defines, so `arr*y` is literal text. It falls to the ordinary Section 16.6 value check, which
  rejects it as `SCHEME001` in the scheme phase at the line the declaration was written on. The
  run exits 1 and publishes nothing, so the `cfg.output=namespace` instance that is perfectly
  well-formed is not written either.
- The difference is intentional: a diagnosis that depends on the data the rule happens to match
  is not a diagnosis. Rejecting the declaration itself makes the same authoring mistake produce
  the same message on every input, which is the property an author can act on.