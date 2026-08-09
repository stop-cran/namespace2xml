# YAML

This tool reads and writes YAML. The reader accepts a normative subset called `RestrictedYaml1`;
the writer emits a normalized form of that subset. Both are defined in `docs/specification.md`, and
the specification wins whenever this guide reads differently. Section anchors below cite the clause
a claim comes from.

YAML is the most permissive of the four structured formats this tool handles. It is also the one
whose ambiguity has bitten the most projects. The rules below exist because a value's kind decided
by a YAML library — YAML 1.1 vs 1.2, this parser vs that — is not a value's kind decided by the
tool that wrote it, and this project cannot allow that difference to change output.

## File extensions

Input paths ending in `.yaml` or `.yml`, matched case-insensitively, select YAML parsing (§7.1).
Nothing else does; a file whose YAML content is named `config.txt` is read as a namespace profile.
On the output side, `.yaml` is the extension the tool appends when a scheme declares
`output=yaml` without an explicit `filename` (§7.1's format matrix; §16.1 and §16.2 for the
appending rule).

## A scheme file may be written in YAML

Section 15 lets a scheme file be authored in any supported input format, chosen by the same
extension rule, so a `.yaml` scheme is read by this reader. It is then projected to the directive
stream a namespace-profile scheme spells directly: **a mapping that has properties is a path** and
is recursed into; **anything else is a declaration**, whose key is the directive name and whose
scalar is its value. These two are the same scheme.

```yaml
app:
  '*':
    output: json
    filename: 'x-*.json'
```

```
app.*.output=json
app.*.filename=x-*.json
```

A `*` key keeps its §10.4 wildcard-template meaning inside a scheme, and its capture substitutes
into `filename` exactly as it does from a profile — the example writes one file per child of `app`.
Quote it: a bare `*` is a YAML alias-adjacent token in some writers, and `'*'` is unambiguous.

A directive's value must be a nonempty scalar after format parsing (§15). A sequence, an empty
mapping, a `null` and an empty string are each a blocking `SCHEME001` naming that declaration's own
line and column — so `output: [json, yaml]`, the natural YAML spelling of "both", is an error rather
than silence. Declaration order within one scheme is document order, which is what §15.2's
later-wins rule ranges over.

## The reader: `RestrictedYaml1`

The reader implements a normative subset of YAML defined in §10.1 and named `RestrictedYaml1`. It
is inspired by the JSON-compatible subset of YAML 1.2, but the exact rules are the ones in §10.1
and not any parser's advertised mode. In particular:

- `true` and `false` are Boolean, resolved by ASCII case-insensitive comparison, so `tRuE` is a
  Boolean and `FALSE` is a Boolean (§10.1);
- `null`, `Null`, `NULL`, `~`, or an empty plain scalar are null (§10.1);
- a JSON-compatible decimal integer is an integer, and a JSON-compatible decimal or exponent form
  is a decimal (§10.1);
- everything else that a YAML library might resolve — `yes`, `no`, `on`, `off`, `y`, `n`,
  timestamps, sexagesimal numbers, hexadecimal, `NaN`, `.inf`, `+1`, `.5`, `1.` — remains a string
  (§10.1);
- duplicate mapping keys are `PARSE001` (§10.1);
- every plain or quoted scalar mapping key is a string with no scalar-tag resolution (§10.1);
- complex mapping keys are `PARSE001` (§10.1);
- construction is safe and never instantiates application-defined types (§10.1).

The differences from the tool's own namespace scalar inference (§18) are deliberate. Plain YAML
`+1`, `.5`, and `1.` remain strings because they are not JSON-compatible numbers; any ASCII case
spelling of `true` or `false`, including `tRuE`, is Boolean (§10.1). §18 rules the untyped
namespace world, and §10.1 rules typed YAML.

`RestrictedYaml1` is intentionally not the full YAML 1.2 Core Schema. A future `yamlinputoptions`
mode may add full Core Schema resolution (§10.1). Until then, if you need `yes` read as `true`,
write `true`.

The conformance fixture `yaml-restricted-schema-and-scalar-kinds` pins each of these rules
against one input document that combines them; its `legacy.md` explains why the shift is
intentional. The point of the shift is that the answer must not depend on which YAML library is
linked.

## What the reader deliberately refuses

§10.2 lists the constructs the reader does not preserve, and titles the section **Deliberately
unsupported features**. Every entry is a refusal by design, not an oversight, and every one has
one reason behind it: `RestrictedYaml1` exists to make one YAML input mean one thing regardless of
who parses it, and each of the listed constructs makes that impossible.

The list is: anchors, aliases, custom tags, directives, explicit document markers such as `---`
and `...`, exact scalar quote style, folded-versus-literal multiline style, exact indentation, and
exact comment spacing.

The first four — anchors, aliases, custom tags, directives — plus every explicit tag token
(`!!str`, verbatim tags, local tags) are blocking `PARSE001` errors in this version (§10.2), even
when the tag would preserve the same basic value. The reason is that `!!str "42"` and `!!int 42`
resolve to different kinds under YAML 1.2 but not under `RestrictedYaml1`, so implicit-tag support
would leak whichever tag set the underlying library chose to honour. Refusing tags outright is the
only way `RestrictedYaml1` can be normative.

Anchors and aliases carry the same objection more sharply: an alias re-uses a value by identity,
which is a graph concept and not a tree concept, and every downstream operation this tool does —
merge, override, comment binding, wildcard match — is defined on the tree. Accepting an alias
would either duplicate the referent's node (in which case anchors add nothing that a copy would
not) or share it (in which case a later contribution to one place would silently update another).
Refusing them is the reading that leaves the rest of the specification consistent.

Merge keys (`<<`) are refused for the same reason `RestrictedYaml1` refuses duplicate keys: a
merge key is a hidden override, and §17.1's deep-merge rules already cover the same job
explicitly. Note that the specification is under-determined here about whether an unsupported
merge key is a `PARSE001` or an ordinary key; this preview refuses plain `<<` and accepts quoted
`"<<"` as an ordinary key. See `KNOWN-LIMITS.md` §1.2.

Quote style, block-scalar style, indentation, and comment spacing are lexical, not semantic.
Nothing downstream can tell whether a value came from a double-quoted or single-quoted scalar, so
preserving that choice would either be a lie (the writer is free to pick either) or an obligation
that the writer would have to break on the first override.

Future input options may define additional safe tag behaviour; no tag is accepted implicitly
(§10.2). If you need one of these constructs today, write it out and pipe it through a
preprocessor, or wait.

The fixture `yaml-restricted-schema-refusals` covers each refusal category with one input
document and one expected `PARSE001` at the offending token's Section 22 line and column. It also
records that when multiple such sources appear in one run, each is reported at its own position in
Section 7.3 command-line order.

## Multiple documents

Multiple YAML documents in one input stream are not supported in this version. Any explicit
document marker — `---` or `...` — is a `PARSE001 §10.3` error. The reader admits one implicit
document per stream (§10.3).

Verified: a stream beginning `---` reports `PARSE001` at line 1 column 1, exits `1`, and produces
no output.

## Wildcard templates supplied as YAML

§10.4 defines a normative capability: a YAML mapping whose keys carry wildcard tokens is a
wildcard template. Each key becomes one literal qualified-name part, so dots and ordinary
backslashes remain literal. Within that part, unescaped `*` and `*[identifier]` tokens use the
wildcard-template grammar of §12.1 and §12.2, and `\*` contributes a literal asterisk.

The spec's worked example is:

```yaml
a:
  '*':
    c: XXX
```

This creates the logical wildcard entry `a.*.c=XXX`. Combined with a second input:

```yaml
a:
  - b: 1
  - b: 2
```

the specified result is:

```yaml
a:
  - b: 1
    c: XXX
  - b: 2
    c: XXX
```

Wildcard template entries are extracted before structural input merging and expanded during the
fixed point in §12.4, before numeric-map sequence inference and output rendering. Extraction is
entry-by-entry: carrier ancestors created only to contain an extracted template do not contribute
mapping-presence marks, and literal sibling entries remain concrete data (§10.4).

**The current preview does not implement §10.4.** Running the tool on the specification's own
worked example produces no output and exits `70` — the non-normative refusal code — with the
message that "wildcard templates in native input (§9.1) is not implemented in this preview: the
key '*' contains an unescaped wildcard token". Namespace2xml 2.4.0 implements §10.4 correctly on
the same input, so a 2.x user gets the specified file today and gets nothing from the preview.
This is recorded in `KNOWN-LIMITS.md` §1.2 as a blocker for 3.0 final rather than a deferred
nicety, and the fixture `yaml-wildcard-template-in-a-native-key-is-declined` pins the refusal so
that a preview that starts guessing at wildcard keys does not do so silently.

The workaround, until §10.4 lands, is to declare the template in a namespace-profile input file
rather than in a YAML input file. `a.*.c=XXX` in a `.txt` input, listed **after** the data input,
against the same `a: [{b: 1}, {b: 2}]` YAML reproduces the specified item shape today:

```text
$ namespace2xml -i data.yaml tpl.txt -s scheme.txt -o out

# out/a.yaml
- b: 1
  c: XXX
- b: 2
  c: XXX
```

Input order is load-bearing here, because §12.4 merges a generated value at its deterministic rule
position: list the template *first* and the generated `c` precedes `b` in every item instead. The
`a:` wrapper is absent because `a.output=yaml` makes `a` the output root, and §16.3 removes the
selector prefix before rendering.

## Comments

YAML supports leading, inline, and trailing comments (§10.1). The reader retains all three
positions in the common model, and the writer emits them in normalized positions after formatting
(§19.4). Exact comment spacing is not preserved (§10.2).

The cross-format binding rules in §20 apply. A comment before the first payload or item is
document-leading, a comment between two payloads or items is a leading comment of the following
one, a comment after the final payload or item is document-trailing, and inline YAML comments
stay attached to their payload. When several source documents merge, each source's document-
leading comments precede its first surviving contribution and its document-trailing comments
follow its final one (§20).

A subtle consequence of §20's document-leading rule: a comment at the very top of a YAML file
binds to the document, not to the first entry. This differs from namespace-profile input, whose
§8.5 rule binds a top-of-file comment to the following entry. A plain round trip does not show
the difference because a document-leading comment and a first-entry leading comment emit in the
same place, but they diverge once the first entry stops being emitted — for example, under an
ignore mask or when the first entry is unselected by the output view. The document comment
survives to the output; the entry-bound one does not. See `KNOWN-LIMITS.md` §1.16.

The fixture `yaml-comment-positions-survive-a-round-trip` pins a YAML→YAML run in which every
supported comment position — document-leading, leading of an entry, inline on an entry, trailing
of an entry, leading of a sequence item, inline on a sequence item, and document-trailing —
survives round trip. The fixture `yaml-document-comments-have-no-value-owner` verifies the
document-position rule by publishing only the second top-level key: the document-leading and
document-trailing comments still appear in the output; comments bound to the unpublished key do
not.

`yamloutputoptions=DiscardComments` (§16.9) turns comment emission off for a chosen output
instance without affecting others.

When YAML is the source and the destination cannot represent comments, one summarized comments-
discarded warning is emitted per destination and feature category (§3.3, §20). JSON is the usual
case — §19.3 renders comments nowhere — and the resulting warning is `WARN003`, once per output
file (§22).

## Sequences and numeric mapping keys

YAML sequences are modelled as ordered items with stable ordering values (§5.4). A native YAML
sequence is a series of *implicit* items, each allocated one ordering value above the sequence
path's high-water mark, in CLI source order and item order. A YAML mapping whose keys are all
canonical nonnegative decimal ordering values in the supported range is *sequence-inferable* and
is projected as an explicitly indexed sequence at pipeline step 11 (§8.7, §3.3).

The two forms interact by design. An explicit numeric mapping key supplies an explicit ordering
value: a later contribution `{'1': three}` addresses the item already at ordering value `1` and
combines the two under §17.1's rules, so it *patches*. An implicit later contribution `[three]`
receives a fresh value above the high-water mark, so it *concatenates*. This is the mechanism
§8.7 gives for cross-file sequence patching. Verified with two YAML sources: with explicit `'1':`
keys, `[one, two]` overridden by `[three]` at index 1 becomes `[one, three]`; with implicit
sequences, the same two contributions become `[one, two, three]` and earn a `WARN004 §8.7`
warning that "implicit items concatenate while explicit ordering values patch".

Where a mapping's keys happen to be numeric but should stay a mapping — an index map, an HTTP
status-code lookup — reach for `type=mapping` on that path (§16.6). It forces the winning
container projection to render as a mapping whose keys are the canonical decimal strings,
gaps and nonzero bases preserved. Verified against `{'2': first, '7': second}` at
`cfg.items.type=mapping`: the output is a two-key mapping, not a two-item sequence.

The step-11 inference should also emit `WARN010` once per source contribution, canonical mapping
path, and output instance (§3.3). This preview does not emit it; see `KNOWN-LIMITS.md` §1.7. Read
the absence of a diagnostic on a numeric JSON or YAML mapping as "not checked" rather than "no
compatibility risk", and reach for `type=mapping` on any numeric mapping whose keys are data.

## Merge

Two YAML contributions at one path deep-merge under §17.1 and §17.3. Mappings recursively merge
matching keys, later scalars win over earlier scalars, sequences merge according to ordering
provenance (§17.1). Raw serialized YAML documents are never appended byte-for-byte (§17.3); the
merge is on the structured overlay, and the writer emits one document from it.

Mapping key order after override follows the position mark of each surviving winning logical path
(§5.2). Overriding a key moves the exact key, together with comments bound to it, to the winning
contribution's position mark. Adding a new child never moves its parent. In the two-YAML deep-
merge example above, `port` is overridden in the later source and moves to the later position;
`name` is only in the earlier source and stays; `db` is a container whose children come from
different sources, so it takes the earliest position at which it was required and keeps it. The
output orders siblings as `name, db, port` for that reason.

`merge=deep` is the default; `merge=append`, `merge=replace`, and `merge=error` change the
strategy per §16.10. `filemerge` (§16.11, §17.5) is the destination-level equivalent that
resolves collisions across output declarations.

## Output

§19.4 is short and worth reading in full. In outline, the writer:

- preserves mapping and sequence order (§19.4);
- preserves supported scalar types (§19.4);
- emits retained comments in normalized positions (§19.4);
- uses literal block scalars for multiline values (§19.4);
- does not emit `---` (§19.4);
- does not preserve original quote style, tag syntax, anchors, aliases, or folded-versus-literal
  source style (§19.4);
- applies structural merge before serialization (§19.4).

Indentation is fixed at two ASCII spaces per mapping or sequence nesting level; literal block-
scalar content is indented two spaces beyond its owning key or sequence indicator; tabs are never
used (§16.9). Document separators and alternate multiline-style preservation are not supported
in this version (§16.9).

### Quoting

§19.4's one explicit quoting rule is that "a string whose plain spelling would resolve to a non-
string kind under `RestrictedYaml1` is emitted single-quoted, with a literal single quote doubled
as `''`". So a string `"true"` is written `'true'`, and a string `"null"` is written `'null'`,
because their plain spellings would resolve to a Boolean and a null on the way back. The
converse is not required: a string `"yes"` is written plain because `RestrictedYaml1` reads
`yes` as a string.

The specification does not fully enumerate the *other* reasons a value needs quoting to preserve
its data. §3.3's normalized same-format round-trip guarantee obliges the writer to preserve
"data structure" and "scalar type", and there are values that would parse differently as a plain
YAML scalar even though `RestrictedYaml1` does not resolve them — a leading `!` that a YAML
reader would read as a tag, a leading `&` or `*` that a YAML reader would read as an anchor or
alias, a value beginning `{` or `[` that would start a flow collection, the strings `...` and
`---` that would be document markers, embedded `: ` that would look like a mapping. The fixture
`yaml-scalars-survive-a-same-format-round-trip` pins concrete cases: `...` is single-quoted,
U+FEFF is escaped as `"\uFEFF"`, U+2028 and U+2029 are escaped as `"\u2028"` and `"\u2029"`, and
a supplementary character is written as itself rather than as two surrogate escapes. Its
`legacy.md` explains that these choices follow from §3.3, not from §19.4's one explicit rule.
`KNOWN-LIMITS.md` §1.15 records the same qualification more generally, filed as a specification
gap in [#52](https://github.com/stop-cran/namespace2xml/issues/52).

Under this preview, verified: a string starting with `!`, `&`, `*`, `{`, `${`, or containing `: `
is single-quoted; `...` and `---` as standalone values are single-quoted. Do not rely on the
exact set — rely on the guarantee that whatever the writer emits, feeding it back through the
reader produces the same string.

### Block scalars

The writer uses a literal block scalar `|` or `|-` for a multi-line string value. The chomping
indicator (`-` strip, none clip, `+` keep) is chosen to make the block's content equal the
string's content. A value ending in exactly one LF selects clip (`|`); a value with no trailing
LF selects strip (`|-`).

A value ending in a blank line — content ending in `\n\n` — cannot be spelled with a keep block
without ending the *file* in two LFs, which §24 forbids ("A text output... ends with exactly one
LF"). Such a value is spelled double-quoted instead, in every position — not only when it happens
to fall last. Making the spelling depend on where it sorts among its siblings would let adding an
unrelated key silently rewrite an untouched value, so the uniform rule wins even at the cost of
losing the block form. The fixture `yaml-indentation-block-scalars-and-quoting` pins this: the
input's `kept: |+` block, whose value ends in a blank line, is written `kept: "solo\n\n"`.
`KNOWN-LIMITS.md` §1.15 records the underlying issue that §19.4's blanket "uses literal block
scalars for multiline values" has never been the whole rule; a block scalar cannot carry a value
containing CR, a control character, lines with trailing whitespace, or a first non-empty line
that is indented, and the writer has always quoted those.

### Shape conflicts

Where a path has both a scalar and a container contribution, §4.4 selects the latest of the two
and warns about the omitted shape. YAML is one of the two output formats that cannot render both,
so a `TYPE002 §19.4` warning is emitted at the omitted node (§22). Verified against §4.4's own
worked input `a.x=1` followed by `a.*.z=3`, which generates `a.x.z=3` at rule position: the YAML
output emits `x` as a mapping containing `z: 3` and warns that the scalar `1` was omitted.
Reversing the source order makes the scalar win instead. The fixture
`json-and-yaml-render-one-exclusive-shape` pins both directions.

## Input options

Root-level input options are declared with `yamlinputoptions=`, comma-separated (§16.8). The only
initial value is:

- `PreserveComments`, enabled by default (§16.8).

There is no `PreserveComments`-off input option; use `yamloutputoptions=DiscardComments` at the
target instance if you want a comment-less output. Selector-qualified input options are blocking
`SCHEME001` errors because input parsing happens before output instances exist (§16.8).

## Output options

`yamloutputoptions=` (§16.9) accepts flags:

- `PreserveComments`;
- `DiscardComments`.

The two are contradictory; naming both in one declaration is `SCHEME001`; naming neither reapplies
the default (§16.9).

Default: `PreserveComments`.

Later complete `yamloutputoptions` directives replace earlier ones (§16.9); flags do not
accumulate across declarations.

## Round-trip guarantees

For supported YAML features, a normalized same-format round trip must preserve data structure,
scalar type, key and item order, supported comments, and namespace names (§3.3). Lexical
formatting — indentation, quote style, line endings, equivalent scalar spelling — need not be
preserved and is not (§3.3).

The one deliberate normalization exception is the numeric-map inference in §8.7: a nonempty
mapping containing only canonical nonnegative decimal keys projects as an explicitly indexed
sequence unless the output view uses `type=mapping`. This is a structural normalization, not a
data loss, and it enables the cross-file sequence patching §8.7 defines. The `WARN010`
compatibility warning it should emit is discussed above.

Cross-format conversion preserves concepts supported by both source and destination formats (§3.3).
YAML → JSON discards comments with one `WARN003` per file; XML → YAML converts XML comments to
YAML comments where representable; namespace → YAML emits values whose kinds were inferred by §18.
A namespace value `true` becomes the Boolean `true` in YAML output; a namespace value `yes`
remains the string `yes`.

The specification does not define a lossless YAML round trip through namespace text — the
extended-namespace serialization mentioned in §3.3 is deferred. Ordinary namespace output is a
data projection, not a lossless serialization of every YAML feature.

## Escapes and references in string values

YAML string scalars, after native YAML decoding, are passed through the §8.3 / Appendix A.5
decoded native-string escape transducer. Its rules are minimal by design:

1. `\*` emits literal `*` (the wildcard-suppression escape);
2. `\${` emits literal `${` (the reference-suppression escape);
3. any other backslash emits itself and consumes only itself;
4. emitted text is never rescanned.

So a YAML value `"hello \n world"` has already been decoded by the YAML parser to
`hello\nworld`, and the tool's escape pass does nothing further. A YAML value `"\\*"` decodes to
`\*` under the YAML parser, and the tool's escape pass emits a literal `*` — as does a single-
quoted YAML value `'\*'`, in which the backslash reaches the transducer verbatim because single
quotes do no escape processing. If you want the literal text `${x}` in a value, write `"\\${x}"`:
the YAML parser turns it into `\${x}`, and the transducer turns *that* into `${x}` literal text.
Verified.

References use the qualified-name grammar of §8.2 and are recognized by §8.4; a YAML string
matched by `substitute=Key` or `substitute=None` is preserved as-is with no transducer decoding
applied (§13.4). A YAML string matched by neither has every reference in it interpreted.

## Traps

**`no`, `yes`, `on`, `off`, `y`, `n` are strings, not Booleans.** §10.1 fixes the Boolean
resolvers as `true` and `false`, case-insensitively, and nothing else. This is different from
YAML 1.1 and from the default schemas of most YAML libraries. If you feed the tool a YAML file
whose value is `enabled: yes` and expect a Boolean, you will see the string `yes` on the way
out. Write `true` if you meant true. `RestrictedYaml1` refuses to guess.

**`1.` and `.5` and `+1` are strings, not numbers.** §10.1 requires JSON-compatible numeric
spellings. `1.` is not one (trailing dot), `.5` is not one (leading dot), and `+1` is not one
(explicit sign). All three round-trip as strings. This differs from the underlying YAML library's
own resolution and from human intuition, and it is the reading that makes the answer
library-independent.

**`nULL` is a string; `null`, `Null`, `NULL`, `~`, and an empty plain scalar are null.** §10.1
enumerates the null spellings by ASCII case, closed. Anything else that looks like an English
"null" is data.

**Timestamps and sexagesimal numbers are strings.** §10.1 lists both and refuses both. A YAML
1.1 parser reads `1:30` as sexagesimal 90; this tool reads it as the string `1:30`. A YAML 1.2
parser reads `2023-01-01` as a timestamp; this tool reads it as the string `2023-01-01`.

**A version-like string in a value is a string, not a number.** `1.0` is a number. `1.0.0` is a
string, because it has too many dots to be a JSON-compatible decimal. If you rely on the type of
a version-like value, decide up front whether it is meant to be a version (string) or a version
number (numeric): a project's `version: 1.0` field becomes the decimal `1.0` on the way out,
which is not what a semver-like intent says.

**`---` and `...` are refused as document markers, not accepted as data.** §10.3 admits one
implicit document per stream and refuses explicit markers. If you meant the literal string
`---`, quote it: `sep: '---'` is fine. If you meant to start a new document, split the file.

**A wildcard `*` in a YAML mapping key is refused today.** §10.4 specifies the behaviour and
the specification's own worked example produces the same output that namespace2xml 2.4.0
produces, but this preview refuses that input with exit `70` and no output. Declare wildcard
templates in a namespace-profile input file until §10.4 lands. See `KNOWN-LIMITS.md` §1.2.

**A comment at the top of a YAML file is document-scoped, not first-entry-scoped.** Under an
ignore mask on the first entry, a top-of-namespace-profile comment is dropped with the entry;
the equivalent top-of-YAML comment survives. §20 and §8.5 give the two different rules. See
`KNOWN-LIMITS.md` §1.16.

**A numeric-key YAML mapping renders as a sequence unless `type=mapping` says otherwise.**
§8.7 makes this a normative deliberate normalization, and it is how cross-file sequence
patching works — but the compatibility warning §3.3 promises for it is not emitted yet
(`KNOWN-LIMITS.md` §1.7). If your intent is a mapping keyed on decimal strings, force it with
`type=mapping`; do not rely on a warning to notice.

**A value ending in a blank line is written double-quoted, not as a block scalar.** §19.4 says
the writer "uses literal block scalars for multiline values", but §24 requires a text output to
end in exactly one LF, and a keep-chomped block ends in two. See `KNOWN-LIMITS.md` §1.15.

**Anchors, aliases, tags, and merge keys are refused, not silently accepted.** §10.2 lists them
all as blocking `PARSE001` errors. A file that used to load under a permissive parser will
refuse to load here; the refusal is at the token, not later.
