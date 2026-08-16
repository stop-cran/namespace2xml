# The JSON format

JSON is supported as both input and output. This guide is one of five `docs/format-*.md` guides;
the others cover the namespace profile, INI, YAML and XML. If you have not read the namespace guide,
that is the format the whole tool projects into and out of — this one describes only the JSON
side of that projection.

Every rule below is a citation of `docs/specification.md`, which is the contract. Where the spec
does not settle something, this guide says so rather than guessing.

## When the tool reads JSON

Section 7.1 selects the JSON parser by extension, matched case-insensitively. `.json` and `.JSON`
both feed the file through the JSON reader; a `.json5`, `.jsonc`, or `.txt` file does not, however
JSON-shaped its contents are.

That is a trap worth naming. A JSON file renamed to `.txt` goes through the **namespace-profile**
parser (§7.1), and the record `{"app":{"port":8080}}` — its first non-space scalar is `{`, not `#`
or `!`, and it contains no separating `=` — falls through §8.1 classification to rule 5 and is
`PARSE001` against §8.1. The reader never even reaches §9. Rename before feeding.

## A scheme file may be written in JSON

Section 15 lets a scheme file be authored in any supported input format, chosen by the same
extension rule. A `.json` scheme is read as JSON and then projected to the directive stream a
namespace-profile scheme spells directly: **a mapping that has properties is a path**, and is
recursed into; **anything else is a declaration**, whose key is the directive name and whose scalar
is its value. These two are the same scheme.

```json
{ "app": { "output": "json", "filename": "app.json" } }
```

```
app.output=json
app.filename=app.json
```

A directive's value must be a nonempty scalar after format parsing (§15). A sequence, an empty
mapping, a `null` and an empty string are each a blocking `SCHEME001` naming that declaration's own
line and column — so `"output": ["json", "yaml"]`, the natural JSON spelling of "both", is an error
rather than silence. Declaration order within one scheme is the order the properties appear, which
is what §15.2's later-wins rule ranges over.

## What JSON syntax is accepted

Section 9.1 accepts:

- objects (become ordered mappings);
- arrays (become sequences);
- strings;
- arbitrary-precision JSON numbers;
- Booleans;
- `null`;
- object property order.

"Arbitrary-precision" is normative. A JSON number whose lexical form contains a fraction part or
exponent becomes an arbitrary-precision *decimal*; every other valid JSON number becomes an
arbitrary-precision *integer* (§9.1). Neither passes through a binary floating-point type, so a
value wider than a `double` survives exactly and its written form — not its magnitude — decides
its kind. `1e0` is a decimal that Section 18 renders `1.0`; `1` is an integer that renders `1`.
The `json-scalar-kinds-and-sequence-order` conformance case pins this and lists it as an
intentional break from 2.4.0, which read numbers through a `double`.

Each object-property name becomes **one literal qualified-name part** (§9.1). A JSON key
containing a literal `.` stays one part, not several: `{"a.b": "value"}` overlays as one node
whose part is literally `a.b`, and any output format that spells qualified names — the
namespace profile in particular — has to escape that `.` to keep the part atomic (see §16.4,
§A.6). JSON has no escape for splitting a key across parts, and neither does §9.1.

Backslashes in the decoded property name are preserved literally except at the very start of the
key, where one may escape a marker (§9.1). Within one part, an unescaped `*` or `*[identifier]`
remains a wildcard-template token for compatibility; `\*` is a literal asterisk. A wildcard key is
extracted as a §10.4 wildcard template and expanded in the §12.4 fixed point;
`docs/format-yaml.md` describes that capability in full, including the two shapes beneath a
wildcard key that are declined, and it applies unchanged to JSON because both formats share a
reader. The `a-wildcard-in-a-native-json-key-is-a-template` and
`a-backslash-asterisk-in-a-native-key-is-a-literal-asterisk` conformance cases pin the JSON side.

Marker-shaped keys (`@field`, `#3`, `Q{...}local`) **are** the typed XML components those markers
introduce (§9.1). `{"a": {"@x": "1"}}` contributes the attribute `x` under `a`, exactly as
`<a x="1"/>` does, and §19.3 writes it back as `@x` — so a JSON document this tool produces reads
back into the XML it came from, and a short JSON overlay can override an attribute in a large XML
base. Marker recognition commits: a key that begins like a marker without completing the
production, such as a bare `"@"` or `"#01"`, is blocking `PARSE001` rather than silently becoming
literal text. A leading backslash escapes a following `@`, `#`, `Q`, or `\` and suppresses marker
recognition for the whole key, so the literal key `@key` is written `"\\@key"` in JSON source and
spells `\@key` on the namespace side. Elsewhere in the key a backslash is literal, so `"C:\\dir"`
needs no escaping. The `a-json-key-carries-xml-markers-through-a-round-trip`,
`a-json-override-reaches-an-xml-attribute`, `a-native-key-marker-commits-once-recognized`, and
`xml-typed-components-recognized-and-an-escaped-json-key-stays-literal` conformance cases pin
these rules.

Strings pass through the §A.5 decoded-native-string transducer *after* native JSON escape
processing: `\*` in a resulting string emits a literal `*`, `\${` emits a literal `${`, and every
other backslash in the decoded text emits itself. Emitted text is never rescanned, so `\${host}`
inside a JSON string becomes literal `${host}` with no reference expansion; a bare `${host}`
inside a JSON string does resolve.

## What JSON syntax is refused

Section 9.2 refuses every nonstandard extension unless a future `jsoninputoptions` value enables
it. All are `PARSE001`:

- `//` and `/* */` comments — §9.2 lists comments among unsupported features.
- Trailing commas — same clause.
- Non-finite numbers (`NaN`, `Infinity`) — same clause.
- `True`, `TRUE`, `Null` — JSON's Booleans and null are strictly lowercase, and §9.2 accepts no
  nonstandard extension.
- An unpaired `\uXXXX` surrogate — §9.1 admits strings, and Appendix A.2 excludes surrogates from
  every escape, so the document denotes no text and is refused rather than repaired into U+FFFD.

The `json-strict-parsing-refusals` conformance case authors four JSON files each carrying one of
these conditions, feeds them in one invocation, and pins one `PARSE001` per failing source under
§9.2 (or §9.1 for the surrogate).

### Duplicate keys

Section 9.3 rejects a duplicate key inside one JSON object outright — `standard mode rejects
duplicate keys` — with `PARSE001` against §9.3, no `--jsoninputoptions` flag to loosen it. The
spec is explicit about why: "avoids parser-dependent behavior and accidental hidden overrides".
Two sibling entries `"app": 1, "app": 2` in one object are not a last-wins override, they are a
malformed document.

Duplicate keys **across** two JSON files, by contrast, are ordinary deep-merge input — see the
merge section below. §9.3 is about one object literal.

## The input option

Section 16.8 lists exactly one JSON input option, `jsoninputoptions=Strict`, enabled by default.
There is no permissive mode yet; the option exists to name the setting for future extension. A
selector-qualified form such as `app.jsoninputoptions=Strict` is a blocking `SCHEME001` against
§16.8 because input parsing happens before any output instance exists.

## How JSON becomes the overlay

The JSON value at the root of a file becomes that file's contribution. An object at the root
supplies ordered mapping children under the file's root node; nested objects and arrays continue
in the same way; every leaf JSON scalar (string, number, Boolean, null) becomes a typed scalar
payload with the kind §9.1 chose. Empty objects and empty arrays are retained as explicit
mapping- or sequence-shape contributions (§4.2, §4.4) and travel through to output as `{}` and
`[]`.

Comments inside JSON are the one thing the reader cannot deliver, because JSON has no comment
syntax (§9.2). If you need a JSON *input* to carry annotations, this is not the tool for it.

## Scalar inference and the canonical numeric text

A typed JSON scalar retains its source kind without re-inference (§18). Untyped namespace values
that reach a JSON output are inferred first: exact case-insensitive `null` and `true`/`false`
become the typed null and Boolean, `[+-]?[0-9]+` becomes an integer, a JSON-compatible decimal or
exponent form becomes a decimal, otherwise the value stays a string (§18). No thousands
separators, locale decimal commas, hexadecimal, `NaN`, or infinities are inferred.

When a decimal is rendered back into JSON, §18's canonical text algorithm applies uniformly to
every output format. Working out the algorithm from the spec and then verifying by running the
tool, the following source → canonical mappings hold (the boundaries are the ones §18's rule 5
exists to decide):

| Source | Kind | Canonical text |
|---|---|---|
| `1` | integer | `1` |
| `-0` | integer | `0` |
| `1.0` | decimal | `1.0` |
| `1e0` | decimal | `1.0` |
| `1.50` | decimal | `1.5` |
| `-0.0` | decimal | `-0.0` |
| `1e-6` | decimal | `0.000001` |
| `1e-7` | decimal | `1.0e-7` |
| `1e20` | decimal | `100000000000000000000.0` |
| `1e21` | decimal | `1.0e21` |

The rules that produce them, in order:

- an integer has no negative zero (§18, and the `json-scalar-kinds-and-sequence-order` legacy
  note), so `-0` becomes `0`;
- a decimal `0` is `0.0` or `-0.0` according to its retained sign (§18 rule 2);
- trailing coefficient zeros are removed while the exponent is increased by the same count
  (§18 rule 3), so `1.50` normalises to `1.5`;
- plain notation is used when the adjusted exponent is in `[-6, 20]` inclusive, scientific
  otherwise (§18 rule 5);
- scientific notation uses one coefficient digit before the decimal point, at least one after
  it, lowercase `e`, and no leading `+` or redundant zeros (§18 rule 6);
- when plain notation would otherwise be indistinguishable from an integer, `.0` is appended
  (§18 rule 7), which is why `1.0`, `1e0` and every other decimal equal to a whole number keep
  a trailing `.0`.

Because §18 is invariant of locale and source spelling, a JSON `1.50` and a JSON `15e-1` both
land in the model as decimal `1.5` and render identically. Source spelling is not retained.

## Arrays, and the numeric-map exception

JSON arrays overlay onto the stable ordering-value model in §5.4 and §8.7. A native array's
items get fresh implicit ordering values; two arrays feeding the same path concatenate in CLI
source order. Because that concatenation is a common source of surprise, the tool emits `WARN004`
once per sequence path when it happens without an explicit `merge` directive telling the reader
which behaviour was meant.

Explicit indexing is orthogonal to a native array. In the profile,

```text
app.tags.0=x
app.tags.1=y
```

lands as a two-item sequence at `app.tags` with ordering values `0` and `1`. If a later JSON
file contributes a native array,

```json
{"app": {"tags": ["z"]}}
```

the native `"z"` is implicit and receives a fresh ordering value above the current high-water mark
(§8.7). The result under the default `merge=deep` is three items `x`, `y`, `z` — not a patch — and
running the tool confirms it. To replace rather than append, declare `app.tags.merge=replace`.

Conversely, if two profile files supply explicit `app.tags.0.x=one`, `app.tags.1.x=two` and then
`app.tags.1.x=three`, the second file *patches* the existing item at ordering value `1`
(§8.7, §17.1), producing `[{"x":"one"}, {"x":"three"}]`. A patch is what "later scalar payloads
override earlier scalar payloads" reads as through a sequence: the later ordering value addresses
the same slot.

The **numeric-map exception** is §8.7's most important trap. A JSON *mapping* whose surviving
child names are all canonical nonnegative decimal ordering values is inferred as a sequence at
pipeline step 11. So

```json
{"app": {"items": {"0": "first", "2": "skip", "5": "last"}}}
```

renders as

```json
{
  "items": [
    "first",
    "skip",
    "last"
  ]
}
```

— the JSON *object* is now a JSON *array*, densified in the output, because §8.7 makes canonical
numeric mapping keys sequence ordering values by construction. `WARN010` is meant to warn about
this once per source contribution, canonical mapping path, and output instance (§3.3, §22), and
it does. Every JSON or YAML document that wrote keys into the mapping is named separately, so a
model assembled from several files says which one to edit. A namespace-format contribution to the
same path raises nothing, because a numeric path segment makes no shape claim to contradict.

To keep the mapping as a mapping, declare `type=mapping` on the path:

```text
app.items.type=mapping
```

That re-projects the same input as `{"0":"first","2":"skip","5":"last"}` in the JSON output.
`type=mapping` is the intended escape valve and is what §3.3 points at for a JSON round trip of a
numeric-key mapping.

Leading-zero spellings such as `"01"` and integers wider than `2^63−1` are not canonical decimal
ordering values (§8.7); such keys stay ordinary mapping keys and prevent sequence interpretation.

## Comments

JSON has no comment syntax on either side of the tool.

Reading, the JSON parser rejects a `//` or `/* */` line as `PARSE001` against §9.2. There is no
`--jsoninputoptions=AllowComments` flag; §16.8 explicitly notes "no JSON-comment option exists in
this version".

Writing, JSON output "renders comments nowhere and emits a summarized discard warning when
comments exist" (§19.3). "Summarized" is normative: one `WARN003` per output file per feature
category, not one per comment (§20, §22). §17.1's rule that comments "accumulate and survive
merge whenever their logical path survives" means comments *bound to a path* that reaches a JSON
destination are the ones counted; a comment attached to a path that never reaches this output —
because of `type=ignore`, an unselected subtree, or an ancestor replacement — is silently
omitted, because §17.1 also says a comment "is omitted only when the logical path is absent from
that output".

The `json-discards-comments-and-yaml-keeps-them` fixture pins the count: two comments in the
input, one `WARN003` on the JSON destination, and the sibling YAML destination keeps them.

## Deep merge when two JSON inputs meet

Section 17.3 is short and total: **"JSON and YAML use the general merge rules directly."** There
is no JSON-specific merge dialect. Everything that governs the merge lives in §17.1, §17.2 and
§8.7.

For two JSON contributions at the same path:

- mapping plus mapping: recursively merge matching keys; each surviving key sits at its winning
  contribution's position (§17.1). This is what deep-merges two config profiles that share a
  subtree.
- scalar plus scalar at the same leaf: later payload wins (§17.1). A duplicate key *across* files
  is therefore ordinary override, not a `PARSE001` — that is only for a duplicate key within one
  object literal.
- mapping plus sequence at one node, or scalar plus mapping: the overlay retains both, and JSON
  as an exclusive-shape destination picks the later contribution and warns with `TYPE002` under
  §4.4 and §19.3. The losing shape is omitted from the JSON file, not from the overlay — another
  output can still render the other shape. The `json-and-yaml-render-one-exclusive-shape` fixture
  demonstrates both directions of that contest.
- sequence plus sequence: implicit later items concatenate; an explicit later ordering value
  patches the item already at that value (§17.1, §17.2). "Raw serialized documents are never
  appended byte-for-byte" (§17.3) — the merge always operates on the parsed model.

Override the default with `[path.]merge=deep|replace|append|error` (§16.10). `replace` removes
the earlier visible sequence projection at that node but does **not** lower the allocation
high-water mark (§17.2), so a later automatic index will not reuse a discarded slot.

Two JSON output declarations targeting the same destination file are governed by `filemerge`,
not `merge` (§16.11, §17.5). The default is `deep`, which folds the two destination-level JSON
documents by the same §17 rules; `filemerge=replace` throws away the earlier plan wholesale,
keeping only the destination high-water state (§17.5); `filemerge=error` refuses the second
contribution as `COLLISION001`. A same-format collision emits `WARN005` (§22).

## JSON output

Section 19.3 is the whole spec for JSON output. It:

- preserves ordered mapping order;
- preserves typed scalars;
- renders comments nowhere and emits a summarized discard warning when comments exist;
- serialises logical line breaks as JSON `\n` escapes;
- applies `root`, `key`, and `type` transformations after input `merge`; destination collisions
  use `filemerge`;
- uses indented output by default.

`\n` escaping is the reason a namespace value `app.lyric=one\ntwo` — which after §8.3 escape
decoding contains an internal LF — renders as `"lyric": "one\ntwo"` in the JSON output, and the
JSON reader gives back the same LF-bearing scalar for a lossless round trip.

### Output options (§16.9)

```text
[selector.]jsonoutputoptions=Indent,EscapeNonAscii
```

Three flags, one mutually exclusive pair:

- `Indent` — two ASCII spaces per nesting level. Default.
- `Compact` — no insignificant spaces or line breaks.
- `EscapeNonAscii` — every scalar above U+007F emitted as uppercase `\uXXXX`, in keys as well
  as values; supplementary-plane scalars use the corresponding UTF-16 surrogate pair. Independent
  of `Indent`/`Compact`.

`Indent` and `Compact` in one declaration is a blocking `SCHEME001` against §16.9 (the two forms
of the contradictory-pair rule). Declaring neither reapplies the default `Indent`.

Flag names are case-insensitive and surrounding whitespace is ignored (§16.9). Later `[selector.]`
declarations replace the earlier complete flag set; flags do not accumulate across declarations.

The `json-output-options-and-escaping` fixture pins all three:

```text
indented.output=json
compact.output=json
compact.jsonoutputoptions=Compact
escaped.output=json
escaped.jsonoutputoptions=Compact,EscapeNonAscii
```

reads a profile carrying `café` and `😀` and produces `{"name":"café","emoji":"😀"}` compact,
`{"name":"caf\u00E9","emoji":"\uD83D\uDE00","caf\u00E9":"key"}` escaped, and an indented file for
the default. Note the uppercase hex, the surrogate pair for the emoji, and that the option
escapes non-ASCII in keys as well as values — `EscapeNonAscii` leaves no byte above U+007F in
the file.

## The round-trip guarantee

Section 3.3 says a normalised same-format round trip preserves data structure, scalar type, key
and item order, supported comments, and namespace names. JSON has no comments, so the "supported
comments" clause is vacuous here.

Lexical formatting may change. §3.3 lists indentation, quote style, attribute layout, line
endings, and equivalent scalar spelling as things that need not be preserved. In practice, a JSON
input round-tripped through JSON output will match structurally and typed-scalar-by-typed-scalar,
but will not be byte-identical to the input unless the input already conformed to the writer's
layout: indent two spaces, one entry per line, canonical decimal text (§18), no trailing whitespace,
one trailing LF. `1.50` on input becomes `1.5` on output; `1e21` becomes `1.0e21`; an object
member appears in source order.

§3.3 also carves out one **structural** exception: the numeric-map inference of §8.7. A JSON
mapping with all-canonical-numeric-decimal keys is projected as a sequence unless a `type=mapping`
directive is in scope. That is not a lexical normalisation — it changes the JSON shape from
object to array — and is why §3.3 promises a `WARN010` at output planning to make the change
visible. The warning is emitted; reach for `type=mapping` whenever a JSON numeric mapping is data
rather than an index.

## Traps

**`.json` case-insensitive, everything else namespace-parsed.** `config.JSON` reads as JSON;
`config.json5` reads as a namespace profile and errors on the first `{`. §7.1.

**A JSON key is one part, no matter what is in it.** `"a.b"` stays one part with the literal name
`a.b`. §9.1 is deliberate about this: 2.4.0 split property names on `.` and lost information the
source contained.

**A JSON *number's spelling* decides its kind.** `1` and `1e0` are different types under §9.1,
and render differently (§18). If a downstream consumer treats `1` and `1.0` differently, watch
what your source spells.

**Duplicate keys within one object are `PARSE001`, but duplicate keys across two files are
ordinary deep-merge.** §9.3 governs one JSON object literal; §17 governs contributions to the
overlay. They point in opposite directions, and the diagnostic language reflects it.

**A JSON mapping whose keys happen to be `"0"`, `"1"`, `"2"` becomes a JSON array on output.**
This is §8.7's numeric-map inference, and it is what §3.3's `WARN010` warns about. The warning
names the document that wrote the keys, not just the path, and `type=mapping` both keeps the keys
and silences it for the output instance that carries the directive.

**A shape conflict drops one side of the JSON node.** `a.x=1` and `a.x.z=3` in the
overlay both survive, but JSON as an exclusive-shape destination renders one and warns with
`TYPE002` (§4.4, §19.3). The later contribution wins by position mark; reversing source order
reverses the choice. `json-and-yaml-render-one-exclusive-shape` pins both directions.

**Two JSON files, same array path, no explicit `merge`.** They concatenate, and the tool warns
once per sequence path with `WARN004` (§8.7). If you meant to replace, say so with
`[path.]merge=replace`; if you meant to patch specific slots, use explicit ordering values.

**Comments in a JSON output are counted but not shown.** One `WARN003` per output file per
feature category (§20), not one per comment. If you rely on comments for change review, JSON is
the wrong destination format — YAML preserves them.

**`\${...}` in a JSON string is a literal, not a reference.** The §A.5 transducer runs *after*
JSON decoding: the JSON escape `\\` produces a literal `\`, then `\${` becomes the literal `${`,
and no reference is expanded. Ordinary `${host}` in a JSON string does resolve.

**A number the tool writes exactly may still be read as `Infinity` or `0`.** §18 keeps arbitrary
precision and §19.3 emits the number in full, so `1e400` in a namespace profile is written
`1.0e400` — which is valid JSON, and which both `json.loads` and `JSON.parse` return as infinity.
`1e-400` comes back as `0`, and an integer wider than 2^53 loses its low digits in JavaScript.
Nothing is wrong on this side: RFC 8259 §6 sets no range and warns that implementations vary. But
the magnitude is gone in the consumer, at exit `0`, with no diagnostic anywhere.

Declare `type=string` on the path when a number's exact value matters more than its kind. That
writes `"1.0e400"`, which every parser returns intact; the consumer then chooses how to widen it.
Verified against Python 3 and Node.

**`type=string` restores the kind, not the spelling.** §18 rule 3 infers `[+-]?[0-9]+` as an
integer, and the section closes with "Numeric source spelling is never retained" — so `0755`
becomes `755`, `01234` becomes `1234`, `1.10` becomes `1.1`, and `+15551234` becomes `15551234`.
Adding `type=string` gives `"755"`, not `"0755"`: the inference has already run and the leading
zero is not recoverable from the model.

File modes, zip codes, phone numbers and two-part version numbers are the everyday casualties. If
the spelling carries meaning, write the value so §18's grammar declines it — a file mode as `0o755`
or `u=rwx,go=rx`, a zip code with its country prefix — rather than expecting a directive to undo
the inference. This is §18, not §19.3, so it applies identically to YAML, XML and the flat formats.

## Deliberate omissions

The following are true statements about the JSON side of the tool that this guide does not
enumerate because they are stated in full elsewhere and duplicating them risks drift:

- exit codes, the diagnostic stream format, and the machine-readable diagnostic schema live in
  §6, §6.4.3, and `spec/diagnostic-stream.schema.json`;
- the full canonical-decimal algorithm lives in §18 rather than reproduced here — the boundary
  table above shows what it produces, not how it is computed;
- resource limits (`--max-input-bytes`, `--max-depth`, and the rest) apply uniformly across
  formats and live in §6.2 and §23;
- Appendix A carries the reference and value-escape grammar in full, and §A.5 covers the JSON
  string escape transducer specifically.

If the spec does not settle a question and this guide does not answer it either, that is a real
gap and worth an ambiguity report against §9 or §19.3 — not a licence for a reader to guess.
