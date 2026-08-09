# The namespace profile format

The namespace profile is the tool's native format. It is used both as input and as output, and it is
the model that every other format is projected into or out of. This guide is one of five format
guides in this directory; the others cover INI, JSON, YAML, and XML. Read this one first. If you
are trying to understand what the tool *is*, the answer is here.

Every rule below is a citation of `docs/specification.md`, which is the contract. Where the spec
does not settle something, this guide says so rather than guessing.

## When the tool reads namespace

Namespace-profile parsing is the **default** for any input file. Only four extensions divert to a
different parser, matched case-insensitively: `.json`, `.yaml`, `.yml`, and `.xml` (§7.1). Every
other extension — `.properties`, `.conf`, `.txt`, no extension at all, and notably `.ini` and `.sh`
— feeds the file through the namespace parser. Native INI and shell input are outside this version
by design (§7.1).

That default is a trap worth naming: a file that looks like a Windows-style INI, with `[section]`
headers, is not read as INI. `[section]` is a record that is neither empty, comment, mask, nor an
entry with a separating `=`, and §8.1 rule 5 makes it a blocking `PARSE001`. Rename the file, or
convert it to namespace form, before feeding it in.

## Physical records

A namespace-profile file is a sequence of physical records (§8.1). LF, CRLF, and lone CR terminate
a record. **U+0085, U+2028, and U+2029 are ordinary data and do not terminate a record.** That is a
deliberate choice, not an oversight — YAML 1.1 line-breaks that leak into a namespace file are
carried as scalar text rather than silently splitting a value into two entries.

Every record is classified in exactly this order (§8.1):

1. an empty record or one containing only spaces and tabs is ignored;
2. a record whose first non-space/tab scalar is `#` is a comment;
3. a record whose first non-space/tab scalar is `!` is a permanent ignore mask;
4. any other record must contain a separating `=` and is an ordinary entry;
5. a record that reaches this rule is `PARSE001`.

The final record need not have a terminator. Each `--variables` (`-v`) argument is exactly one
record and accepts entries and masks but not comments (§8.1).

## The entry

An entry is `qualified.name=value`. The first separating `=` divides the name from the value
(§8.1). A separating `=` is an unescaped `=` outside a `Q{...}` URI, so recognising it requires
scanning the name with the Appendix A.2 grammar rather than searching for a character.

**Records are not trimmed.** Every character before the first separating `=` is part of the name,
and every character after it is part of the value, including leading and trailing spaces and tabs
(§8.1). The value

```text
a.note= keep me #not-a-comment  
```

is exactly ` keep me #not-a-comment  `, with a leading space, an embedded number sign, and two
trailing spaces. A `#` that is not the first non-space/tab scalar of a record is never a comment
(§8.5); comment recognition is decided once, at record classification.

Values may be empty.

## Qualified names

An unescaped `.` separates name parts (§8.2). The default input delimiter is fixed at `.`; setting
`delimiter` in a scheme reconfigures *output*, not input (§16.4).

Within a name, the escape set is (§8.2 and Appendix A.2):

- `\.`, `\*`, `\=`, `\#`, `\!`, `\$`, `\@`, `\}` — the literal character;
- `\Q` — a literal `Q`, needed to disambiguate an ordinary part beginning with `Q{` from an XML
  canonical component;
- `\\` — a literal backslash;
- `\u{HEX}` — one Unicode scalar written with one to six hexadecimal digits;
- unescaped `*` — a wildcard capture;
- `*[identifier]` — an explicitly identified wildcard capture.

Every other backslash sequence in a name is a blocking parse error. Values above U+10FFFF, values
in the surrogate range U+D800–U+DFFF, empty digit strings, and malformed forms are `PARSE001`
(§8.2).

At the start of a name part, unescaped `@`, `#` followed by canonical decimal digits, and `Q{`
introduce typed XML canonical components. **Marker recognition commits** (§8.2): once such a marker
is recognised, the part must match the typed production in full. `#1x`, `@`, and `Q{urn:x` are each
`PARSE001`. The ordinary parts carrying that text are written `\#1x`, `\@`, and `\Q{urn:x`. This is
what §19.1 emits for them, so it is what a round trip needs.

An empty name part is a parse error. A qualified name must not begin or end with an unescaped
delimiter or contain consecutive unescaped delimiters (§8.2).

## Value escapes

Namespace values use a smaller escape set than names (§8.3). Within an interpreted value:

- `\\` emits `\`;
- `\*` emits literal `*`;
- `\${` emits literal `${`;
- `\n`, `\r`, `\t` emit LF, CR, tab.

**Every other backslash sequence preserves the backslash and following character** (§8.3). So
`C:\Users\alice` is read as itself — the `\U` and `\a` are unknown value escapes and both scalars
survive. This is a deliberate departure from Java properties files, where an unknown backslash is
dropped. Namespace values also intentionally do not support `\u{HEX}`; non-ASCII text is written
directly as UTF-8 (§8.3, §7.4).

This is not the escape set that JSON, YAML, and XML string values go through. Those have already
been decoded by their native parser, and Appendix A.5 defines a minimal one-pass transducer for
them: only `\*` and `\${` are recognised; every other backslash emits itself and consumes only
itself. Do not read across formats and assume the same escapes apply.

## References

An unescaped reference is `${qualified.name}` (§8.4). The reference name uses the same grammar as a
qualified name, minus wildcards outside the two contexts in §§12.1 and 12.2. The first unescaped
`}` outside a `Q{...}` URI terminates the reference (§8.4, Appendix A.4).

**Every unescaped `${` in an interpreted value must begin a syntactically valid reference.** There
is no "reference-shaped text that isn't a reference." If you need the literal text `${...}`, escape
the opening as `\${` or match the path with a scheme rule that sets `substitute=Key` or
`substitute=None` (§8.4). An unterminated reference produces `REFERENCE001`:

```text
$ echo 'app.x=hello ${incomplete' | ...
profile.txt(1,25): error REFERENCE001 §8.4:
this reference has no closing brace; write '\${' for literal text.
```

References resolve after wildcard generation and ordinary merging (§13.1), and are strictly scalar:
you cannot reference a mapping or a sequence, only the scalar or null payload at one canonical
path (§13.1, §13.3). Missing references, cycles, and ambiguities are blocking errors (§13.1).

References propagate type. If a value consists of exactly one reference and no literal text, it
inherits the referenced scalar's kind and value (§13.2). So with

```text
db.port=5432
svc.port=${db.port}
svc.endpoint=host:${db.port}
```

`svc.port` is an integer and `svc.endpoint` is a string. Under `output=yaml` the guide's tests
render

```yaml
port: 5432
endpoint: host:5432
```

— `port` is an unquoted YAML integer, `endpoint` a string. Any concatenation collapses the result
to a string (§13.2).

Interpolation text is canonical (§13.2): `null`, lowercase `true`/`false`, base-10 integer without
leading zeros except `0`, and canonical decimal per §18. Source spelling is not retained.

## Scalar inference

A namespace-profile scalar starts as an untyped string (§4.3). Scalar inference then classifies it
(§18):

1. exact case-insensitive `null` becomes null;
2. exact case-insensitive `true` or `false` becomes Boolean;
3. `[+-]?[0-9]+` becomes an arbitrary-precision integer;
4. a JSON-compatible decimal or exponent form becomes an arbitrary-precision decimal;
5. otherwise the value remains a string.

Thousands separators, locale decimal commas, hexadecimal, `NaN`, and infinities are not inferred
(§18). The grammar is locale-independent.

Inference happens on ingest, but its effect is only visible where the destination format
distinguishes scalar kinds. Namespace output renders everything as canonical text and is
locale-independent throughout (§18):

```text
# input                # output
a.null=NULL            null=null
a.bool=TRUE            bool=true
a.int=+042             int=42
a.dec=1.50             dec=1.5
a.str=NaN              str=NaN
```

`NULL` and `TRUE` canonicalise to lowercase; `+042` sheds the sign and leading zero; `1.50` sheds
the trailing zero (§18 canonical decimal); `NaN` remains a string. This canonicalisation is applied
by every output format and by interpolation; numeric source spelling is never retained (§18).

An untyped payload containing an unescaped reference is not inferred until reference resolution
(§18); the reference forwards its own type instead.

## Comments

A comment is a physical record whose first non-space/tab scalar is an unescaped `#` (§8.5). Its
text is the remainder of the record after that `#`, with leading and trailing spaces and tabs
removed. The `#` itself is not part of the text.

Consecutive comments accumulate onto the following entry (§8.5). **A blank line, a permanent mask,
and even a `PARSE001` record all leave a comment run open** (§8.5). This is stronger than the
usual adjacency rule and is deliberate: a stray blank line between a comment block and its entry
does not silently discard the comment, and inserting an unrelated `!pattern` does not move the
comment onto a different value.

Trailing comments with no following entry become document-trailing comments (§8.5).

Namespace and quoted-namespace output preserve leading comments only (§20). Every emitted physical
line is prefixed independently with `# `, so a multiline source comment cannot introduce an
executable shell assignment or an uncommented namespace entry through a `#` in its middle (§20).
NUL is rejected in comment text.

Comment association is made against the following entry's logical qualified path before overrides
are evaluated, and it survives replacement of that path (§8.5, §4.5): the winning contribution
inherits the comments bound to its logical path.

## Ignore entries

An ignore entry is `!qualified.name.pattern` (§8.6). The legacy `!pattern=value` form remains
accepted; the value is ignored. Patterns use the wildcard rules of §12.

An ignore entry is a **permanent, run-wide subtree exclusion mask** (§8.6). Every concrete or
generated contribution matching the pattern is suppressed regardless of position in source order,
suppressed paths never become wildcard candidates or reference targets, and — this is the important
part — **a later contribution cannot recreate the path**. The spec's canonical example:

```text
a.x=1
!a.*
a.x=2
```

produces no `a.x` value. A test of

```text
app.a=1
!app.a
app.b=2
app.a=3
```

under `app.output=namespace` emits exactly `b=2`; the later `app.a=3` is still masked.

This is not the same as `type=ignore` in a scheme (§16.6). Both hide data, but they differ
categorically:

- `!pattern` is a namespace-profile directive, applies to the entire run, is order-independent as
  to the paths it masks, and permanently prevents recreation;
- `type=ignore` is a scheme directive, applies to one output view, and hides an item only in that
  output — every other output still sees it.

Reach for `!` when a value must not exist anywhere; reach for `type=ignore` when you want to hide
it from one destination and keep it addressable elsewhere.

Native JSON, YAML, and XML have no tombstone syntax, and a key beginning with `!` in those formats
is an ordinary literal key (§8.6). Use a namespace-profile `!pattern` or `type=ignore` to remove.

## Numeric paths and ordered sequences

This is the corner most users get wrong, and it is worth reading twice.

A nonempty mapping whose surviving concrete child names are **all** canonical nonnegative decimal
ordering values is classified as sequence-inferable and projected as an explicitly indexed
sequence at pipeline step 11 (§8.7). Canonical spelling means `0` or a nonzero digit followed by
decimal digits, with a value not exceeding `9,223,372,036,854,775,807` (§8.7). Leading-zero
spellings like `00` and `01` are ordinary mapping keys and disable the inference. So is a value
canonically spelled above the maximum.

Within an explicit indexed contribution, repeated ordering values are resolved by ordinary later-
entry precedence, gaps and nonzero bases are allowed, and missing values do not create null
placeholders (§8.7). **Explicit indexed contributions patch the sequence at their supplied ordering
values.** So

```text
# base.txt
app.items.0.x=one
app.items.1.x=two

# patch.txt
app.items.1.x=three
```

with `app.output=namespace` renders exactly

```text
items.0.x=one
items.1.x=three
```

The two-item sequence carries `one` and `three`; `two` is overridden at ordering value `1`, and no
third item appears. This is different from what native JSON/YAML arrays do (§8.7): those use
high-water allocation and concatenate. A YAML `[one, two]` followed by `[three]` produces three
items; explicit numeric-path mappings *never* concatenate merely by ordering value.

Under §5.4, each sequence path has a high-water mark that begins at `-1` and only ever rises. An
explicit ordering value greater than the current mark raises it. Automatic allocation is never
retroactive: deleting `a.1` does not free the value for a later item to occupy, and an explicit
later contribution can still intentionally patch a value that is not currently visible unless a
permanent `!` mask suppresses it (§5.4).

**Namespace and INI output show dense zero-based indices** where their projection requires indices
(§8.7). That is a display choice; internally the ordering values are stable across the whole run.
Two agreeing readings of the same corpus can therefore emit byte-identical output while disagreeing
about which ordering value each item carries — a detail that becomes visible only when a
subsequent overlay addresses the value directly.

Numeric-map inference runs once, after wildcard generation and permanent ignores have reached
their fixed point (§8.7). Before that phase, numeric mapping keys are ordinary addressable path
parts, which is what lets templates and ignore masks match them without any provisional guess about
whether they will end up projected as a sequence. A surviving empty mapping remains a mapping.

Each input file is one source contribution. Each `--variables` argument is a separate contribution
in argument order (§8.7).

## Namespace output

Namespace output emits ordered scalar projections in `qualified.name=value` form (§19.1). Mappings
use name parts; sequences use generated zero-based decimal parts. There is no separate section
syntax and no line continuation — every physical output entry is exactly one line, and any LF, CR,
or tab in a value is emitted via `\n`, `\r`, `\t` (§19.1).

By default the tool writes each output instance to `<selector>.properties` in the output root
(§16.2). A root-level selector writes `output.properties`. An explicit `filename` overrides both.

A flat projection visits the selected view depth-first in pre-order (§19.1): a node's own scalar
is emitted before anything beneath it, its mapping children follow in Section 5.2 order, and its
sequence items follow in ascending ordering value. So with

```text
app.x=1
app.y=2
app.*.z=3
```

and `app.output=namespace`, the tool emits

```text
x=1
x.z=3
y=2
y.z=3
```

`x=1` comes before `x.z=3` because pre-order emits the scalar of `x` before descending into its
children (§19.1). The two container facets — mapping children and sequence items — keep their own
orders rather than interleaving, because §5.2 orders mapping children by position mark while §5.4
orders items by ordering value and no comparison between the two is defined (§19.1).

An overlay node can carry a scalar and children at once, and namespace output emits both (§4.2,
§4.4). This is by design: the format is capable of representing a shape that JSON and YAML cannot
express as one node, and does so.

### `root`

A scheme directive of the form

```text
[selector.]root=name[.nested-name...]
```

wraps the selected content (§16.3). The concrete output selector prefix is removed first, then
`root` prefixes what remains. For `app.output=namespace` and `app.root=svc.config`, an input of

```text
app.name=hello
app.port=42
```

emits

```text
svc.config.name=hello
svc.config.port=42
```

The original `app` prefix is gone; the root value provides the new prefix in full. `\.` represents
a literal dot inside one root name part (§16.3).

### `delimiter`

The default namespace output delimiter is `.` (§16.4). A scheme directive of the form

```text
[selector.]delimiter=string
```

replaces it for that output. An input of

```text
app.a.b=x
app.a.c=y
```

under `app.output=namespace` and `app.delimiter=:` emits

```text
a:b=x
a:c=y
```

Namespace *input* syntax always uses `.` in this version; changing the output delimiter is a
consumer-oriented projection, and it is explicitly outside the normalized same-format round-trip
guarantee (§16.4).

An empty delimiter is invalid (§16.4). For namespace output, a delimiter must not contain `=`,
backslash, or any scalar in the forbidden set of §19.1 (Unicode categories `Cc`, `Cf`, `Cs`, plus
U+0085, U+2028, U+2029), and must not consist solely of scalars drawn from `u`, `{`, `}`, and the
hexadecimal digits `0`–`9` and `A`–`F` — the exclusion protects the `\u{HEX}` escape from
colliding with the delimiter. A delimiter violating either restriction is `SCHEME001` (§16.4).

### Escaping on the way out

Namespace name encoding is total and injective (§19.1). In every emitted name part, the writer
escapes: backslash; the configured delimiter string; literal `*`; literal `${`; `=`; `}`; CR, LF,
and tab; a leading `!` or `#` when it could begin a physical record; and leading `@`, `#`, or `Q{`
text when the component is an ordinary name rather than a typed XML component.

The delimiter is a special case worth internalising. **A scalar that begins a configured-delimiter
occurrence is always escaped as `\u{HEX}`**, even where §8.2 defines another lexer form (§19.1,
§16.4). For the default delimiter `.`, that means U+002E emits as `\u{2E}`, not `\.`. So an input
of

```text
app.odd\.name=value1
```

with `app.output=namespace` renders as

```text
odd\u{2E}name=value1
```

The `\u{HEX}` form is written with no leading zeros and uppercase hexadecimal digits (§16.4);
byte-identical output requires exactly one spelling.

Typed XML components emit their canonical unescaped `@`, `#n`, or `Q{...}` notation. Ordinary
components with the same text emit the escaped form (§19.1), which is what preserves identity
across the round trip.

### Values on the way out

Namespace output values are the inverse of the input lexer (§19.1): backslash as `\\`, LF as `\n`,
CR as `\r`, tab as `\t`, literal wildcard as `\*`, literal reference start as `\${`. Multiline
scalar data is represented through escapes, never literal record-breaking line terminators. So

```text
app.text=line1\nline2\nline3
```

reads as the three-line value `line1<LF>line2<LF>line3` (§8.3) and, under `app.output=namespace`,
writes back to exactly `text=line1\nline2\nline3` — same bytes, same three lines represented one
way.

Comments are emitted as normalised `# comment` lines where their association can be represented
(§19.1, §20). Typed values use canonical locale-independent text: `null`, `true`/`false`, base-10
integer, canonical decimal or exponent form (§19.1, §18).

## Quoted namespace

`quotednamespace` is defined as **POSIX shell assignment output without `export`** (§19.2). It is
namespace output projected under shell quoting, not a separate value model. The default output
file is `<selector>.sh` (§16.2), and the default delimiter is `_` (§16.4) rather than `.`, so an
input of

```text
app.SECTION.NAME=val
```

with `app.output=quotednamespace` writes

```text
SECTION_NAME='val'
```

to `app.sh`.

Every key must be a valid shell identifier — `[A-Za-z_][A-Za-z0-9_]*` — after applying `root` and
`delimiter`. An invalid key is `SHELL001` (§19.2). This is stricter than plain namespace output,
which admits arbitrary scalars in a name via escapes.

Values use single-quote shell escaping (§19.2), which is what the `'\''` sequence produces:

```text
app.MSG=can't stop
                       →   MSG='can'\''t stop'
```

Single-quoted shell text has no expansions, so this reproduces spaces, `$`, backticks, double
quotes, backslashes, exclamation marks, and line breaks verbatim (§19.2). NUL is not representable
and is an error.

A null payload emits the text `null`, exactly as in namespace output (§19.2): `NAME='null'`, not
an empty assignment. That is the same information a namespace consumer receives from `name=null`;
emitting an empty assignment would make null indistinguishable from the empty string.

Reach for `quotednamespace` when the consumer is a POSIX shell that will `.` or `source` the file
into its environment. Reach for plain `namespace` when the consumer is another instance of this
tool, or a properties-style reader.

## Round-trip

The normalized same-format round trip through namespace output preserves data structure, scalar
type, key and item order, supported comments, and namespace names (§3.3). It does **not** preserve
lexical formatting: indentation, quote style, and equivalent scalar spelling may change (§3.3).
Numeric source spelling is not retained (§18). Every value goes through canonicalisation before
emission, and every name uses the injective encoding of §19.1.

Two limits are worth stating exactly:

- **The output-side delimiter behaviour is not a round trip.** Namespace input always uses `.`;
  namespace output using a different delimiter is a consumer-oriented projection and is explicitly
  outside the same-format guarantee (§16.4). If you set `delimiter=:` and read the result back, it
  does not parse to the same model.
- **Ordinary namespace text cannot express every YAML or XML concept.** Ordinary namespace output
  is a data projection, not a lossless serialisation of every source-format feature (§3.3). A
  future extended namespace serialisation may expose node-kind metadata; this version does not
  standardise it.

Comments are preserved as normalised leading `#` comments only (§20). Inline and trailing comments
from YAML become leading comments on the following entry (§4.5).

## Traps

**`.ini` and `.sh` are parsed as namespace.** Named already, worth naming again. `.ini` is
particularly nasty because the file will look valid to a human reader and fail on the first
`[section]` header with a bare `PARSE001` (§7.1, §8.1).

**Records are not trimmed.** Trailing spaces in a value survive, and so does a leading space
before the value's first non-whitespace character (§8.1). If a downstream reader cares about
trailing whitespace, so does this tool.

**`#` is only a comment marker at the start of a record.** After leading whitespace, and nowhere
else (§8.5). `a.note=x #not-a-comment` is a value that ends with a number sign.

**Unknown backslash sequences in values are preserved.** `\a`, `\U`, `\z` all keep their backslash
(§8.3). This differs from Java properties files, where an unknown backslash is dropped, and from
the Section A.5 transducer used for JSON/YAML/XML string values.

**A `${` in a value must be a valid reference.** Reference-shaped text that isn't a reference is
`REFERENCE001`. Write `\${` for a literal `${`, or match the path with a scheme rule of
`substitute=Key` or `substitute=None` (§8.4).

**`!pattern` is permanent, and it prevents recreation.** A later contribution at a masked path is
suppressed, not resurrected (§8.6). This is different from `type=ignore`, which only hides in one
output view.

**U+0085, U+2028, and U+2029 are ordinary data.** They do not terminate a record (§8.1). A YAML
line-break character that leaks into a namespace file stays part of a value; it does not split the
entry.

**Numeric mapping keys `00` and `01` are ordinary keys.** Canonical spelling is `0` or a nonzero
digit followed by decimal digits (§8.7). A leading-zero spelling disables sequence inference for
the whole containing mapping, so mixing `01` and `1` in the same parent quietly changes projection
behaviour.

**Marker recognition commits.** An unescaped `@`, `#N`, or `Q{` at the start of a name part is
either a typed XML component in full or a `PARSE001`. There is no fall-back to "ordinary component
that happens to start with `#`" (§8.2). Write the ordinary form as `\@x`, `\#1x`, or `\Q{urn:x`,
which is what §19.1 emits for it.

**On output, the delimiter escapes to `\u{HEX}`, not to its input short form.** For the default
delimiter `.`, U+002E in an ordinary name part emits as `\u{2E}`, not `\.` (§19.1, §16.4). This
holds even where §8.2 defines a shorter lexer form for that scalar. The rule keeps one output
spelling for every delimiter choice, which is what byte-identical output requires.

## Open questions

The following are things the specification does not settle that a user might reasonably ask:

- **The exact spelling of a diagnostic's `rule` member text.** §22 lists `declaration or wildcard
  rule` among the members a diagnostic carries and fixes the JSON layout of §6.4.3, but the text
  of `rule` on a `WILDCARD002` occurrence is an implementation choice.
- **Whether `WARN010` fires on JSON-to-JSON numeric-map inference.** §3.3 says it must, once per
  source contribution, canonical mapping path, and output instance. At the time of writing the
  tool does not emit it; see the report below.
