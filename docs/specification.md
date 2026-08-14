# Namespace configuration transformer: clean specification

**Status:** implementation specification for review  
**Compatibility target:** namespace2xml 2.4.0, except behavior classified as undefined, nondeterministic, unsafe, or explicitly changed below  
**Prepared:** 2026-08-04

## 1. Purpose

The product is a deterministic command-line configuration transformer.

It shall:

1. ingest ordered configuration sources;
2. preserve their meaningful typed structure in a common document model;
3. apply ordered overrides, references, wildcard templates, ignores, and scheme transformations;
4. merge contributions deterministically;
5. render one or more configuration files;
6. preserve enough metadata for normalized same-format round trips within the supported feature set;
7. remain compatible with the established namespace2xml profile and scheme contracts unless this specification explicitly changes a legacy behavior.

The supported formats are:

- namespace properties;
- POSIX shell assignments, exposed under the legacy name `quotednamespace`;
- JSON;
- YAML;
- XML;
- INI.

## 2. Normative language

The words **must**, **must not**, **should**, and **may** are normative.

- **Must** describes required behavior.
- **Should** describes the default unless a documented option explicitly changes it.
- **May** describes optional implementation behavior that must not alter the specified result.

### Normative references

This document defines its own dialects — `RestrictedYaml1`, `PortableIni1`, the namespace profile language — but it defines them in terms of external standards, and a standard's edition changes what its own text says. The editions below are the ones this document is written against. A later edition does not apply until this document names it.

| Reference | Edition |
|---|---|
| The Unicode Standard | Version 16.0.0, including UTF-8 and the general category assignments. |
| JSON | RFC 8259 (STD 90). |
| YAML | YAML Ain't Markup Language version 1.2, revision 1.2.2 (2021-10-01). |
| XML | Extensible Markup Language 1.0, Fifth Edition. |
| XML namespaces | Namespaces in XML 1.0, Third Edition. |
| XQuery and XPath Data Model 3.1 | For the `Q{uri}local` notation of Section 11.4 only. |
| ABNF | RFC 5234, as Appendix A states. |

Four consequences are worth stating rather than deriving.

The Unicode edition is load-bearing rather than decorative. Section 16.4 defines the set of scalars a namespace name must escape as the general categories `Cc`, `Cf` and `Cs` plus three named characters, and a general category is a property of an edition: a code point unassigned in one edition may be assigned to `Cf` in the next, at which point the same input escapes differently. Two implementations built against different Unicode editions therefore produce different bytes for a name containing a recently assigned character, which Section 24 forbids. An implementation must confirm that the character data it actually resolves matches the edition named above, rather than assuming the edition its runtime documents; the assignment is observable, so this is a test an implementation can run rather than a promise it has to make.

XML 1.1 is not supported: it permits C1 control characters that XML 1.0 forbids, and Section 11 rejects them. A document declaring `version="1.1"` is refused by Section 11.2 rather than read as the XML 1.0 text it is not.

YAML 1.1 is not supported either, and this matters more than the XML case because the two revisions disagree about *values* rather than about characters: `yes`, `no`, `on`, `off` and sexagesimal numbers are typed in 1.1 and are strings in 1.2. `RestrictedYaml1` follows 1.2 and Section 10 states the resolutions explicitly, so a reader never has to know which revision an implementation's library was built for.

**Every case-insensitive comparison in this document is ASCII case folding.** A character outside U+0041–U+005A and U+0061–U+007A compares equal only to itself. Section 16.5 says this of the scheme language; it holds equally of input file extensions in Section 7.1, option values in Section 6.2, and the `null` literal in Section 18, none of which repeat it. The rule is deliberately not the case-insensitive comparison a general-purpose library offers: U+017F LATIN SMALL LETTER LONG S uppercases to `S` under invariant casing, and U+212A KELVIN SIGN is a `K` to a reader, so a Unicode-aware fold would accept `ſtring` for the type name `string` and `1KiB` written with U+212A. Both are refused. Every keyword vocabulary in this document is ASCII, and admitting a homoglyph into one would make a directive's meaning depend on which casing table an implementation happened to link.

## 3. Compatibility policy

### 3.1 Preserved behavior

The replacement must preserve:

- existing CLI option names;
- namespace profile syntax;
- scheme directive names;
- later-entry override precedence;
- textual wildcard templates;
- value references;
- profile and scheme ignores;
- existing output format names;
- existing default extensions;
- explicit `filename` values as complete paths without automatic extension appending;
- command-line variables as the highest-precedence input source;
- missing-file warning-and-ignore behavior;
- deprecated aliases listed in this specification.

### 3.2 Deliberately corrected behavior

The replacement must not preserve legacy behavior that was:

- dependent on parallel execution order;
- dependent on shared mutable array-index state;
- dependent on runtime culture;
- dependent on dictionary iteration order;
- caused by comparing only the overlapping parts of two qualified names;
- caused by raw appending of independently serialized YAML documents;
- caused by output files being opened before the complete output plan was validated;
- caused by a synthetic internal root leaking into user-visible file names;
- caused by treating the text a resolved scheme reference contributes as scheme-written path, so that a separator held by the referenced directive creates directory hierarchy and can compose a destination another output instance has already claimed;
- caused by discarding a scheme directive whose value could not be resolved, so that an unresolvable or cyclic reference silently selects the default destination instead of failing;
- caused by silent loss of multiline values in JSON or XML;
- caused by unhandled user-input exceptions;
- caused by insecure XML document-type or external-entity processing;
- allowed output paths that differ only by ASCII letter case to coexist on some operating systems but collide on others;
- caused by relying on `merge` to control collisions between output instances; such schemes must use `filemerge`, while `merge` remains recognized with input/common-model scope.

Such cases are governed by the explicit rules below.

### 3.3 Round-trip guarantees

The product defines three different guarantees.

#### Normalized same-format round trip

For supported features, reading and writing the same format must preserve:

- data structure;
- scalar type;
- key and item order;
- supported comments;
- supported XML node kinds;
- significant XML text and whitespace;
- namespace names.

Lexical formatting may change. Indentation, quote style, attribute layout, line endings, and equivalent scalar spelling need not be preserved unless explicitly stated.

The deliberate numeric-map inference in Section 8.7 is a structural normalization exception: a nonempty mapping containing only canonical nonnegative decimal keys projects as an explicitly indexed sequence unless the output view uses `type=mapping`. This exception enables controlled cross-file sequence patching, including explicitly indexed YAML mappings. During output planning, emit exactly one compatibility warning for each source contribution, canonical mapping path, and output instance where a JSON or YAML mapping inferred at step 11 remains projected as a sequence. XML mappings cannot qualify because XML element names cannot be canonical decimal ordering values. An output view using `type=mapping`, an unselected path, and namespace-profile numeric paths do not emit this warning.

#### Cross-format conversion

Cross-format conversion preserves concepts supported by both source and destination formats.

Unsupported source concepts are discarded during rendering and must produce one summarized warning per output file and feature category. For example, rendering YAML comments to JSON produces one comments-discarded warning, not one warning per comment.

#### Extended namespace round trip

Ordinary `name=value` data cannot represent every XML or YAML concept. A future extended namespace serialization may expose node-kind metadata for round trips through namespace text.

This version does not standardize that metadata syntax. Ordinary namespace output is therefore a data projection, not a lossless serialization of every source-format feature.

## 4. Ordered typed document model

The implementation must use an ordered typed model rather than converting every source immediately into untyped namespace strings.

### 4.1 Document

A document contains:

- a root value;
- ordered comments and source annotations;
- source identity and source ordering;
- format-specific metadata within the supported subset.

### 4.2 Overlay nodes

The common model is an ordered tree of **overlay nodes**.

One logical path may simultaneously retain:

- an optional scalar or null payload;
- an explicit mapping-presence contribution, including an empty mapping;
- ordered mapping children;
- an optional ordered sequence of stable ordering values;
- comments and source annotations;
- format-specific node-kind metadata.

This is intentional. Namespace data can legally contain both:

```text
a.x=1
a.x.z=3
```

The model must retain both facts without discarding either during ingestion or wildcard generation.

An output format decides whether that overlay is directly representable:

- namespace output must emit both the payload and descendants;
- quoted-namespace output must emit both the payload and descendants when their flattened keys remain distinct;
- XML must represent a payload as text plus child elements when the effective XML types permit it;
- JSON and YAML cannot represent one node as both a scalar and a container, so source-order conflict projection applies;
- INI behavior follows its declared dialect and otherwise reports or resolves a shape conflict as specified below.

Mapping, sequence, scalar, and null are therefore projections of an overlay node, not mutually exclusive internal node kinds.

### 4.3 Scalar kinds

A scalar records both its value and its kind:

- string;
- Boolean;
- arbitrary-precision integer;
- arbitrary-precision decimal;
- null.

A namespace-profile scalar initially has the kind `untyped string`. Scalar inference converts it according to Section 18 unless reference resolution or an explicit rule determines its kind.

### 4.4 Shape contributions

Each payload, child mapping, or sequence contribution has its own immutable **position mark**, derived from the stable ordering key in Section 4.7.

The effective **mapping shape-mark** is the latest surviving explicit mapping-presence or descendant contribution that requires mapping shape. Empty mappings therefore participate in precedence even though they have no children. Any later deep descendant refreshes the mapping shape-mark of every ancestor required to contain it without changing that ancestor's position mark.

The effective **sequence shape-mark** is the latest surviving sequence contribution. The effective container shape-mark is the later of the mapping and sequence shape-marks.

When a destination requires one exclusive shape:

1. determine the latest scalar/null contribution at the node;
2. determine the latest container contribution at the node;
3. the later of those two contributions determines the rendered shape;
4. the losing shape is omitted from that output and produces one shape-conflict warning;
5. the internal overlay remains unchanged for other outputs.

A destination that discards a category of member — today only the Section 11.5 comment nodes, which
Section 20 keeps in XML and discards everywhere else — applies the discard before step 2, so a
container whose members are all discarded there is not a container contribution at that destination
and the scalar wins step 3 unopposed. No shape-conflict warning arises, because at that destination
no shape lost; the summarized discard warning still reports the comments.

Resolving in the other order loses strictly more. The node would take container shape, step 4 would
omit the scalar, and the members that shape rested on would then be discarded during rendering, so
the output would carry an empty container where the source had a value — both the comment and the
value gone, and the value is the one the author cannot reconstruct from the file. A comment is
metadata about a value, and metadata must not be able to delete what it annotates.

This is not the empty-container case. A container that has no members at all is not discarding
anything, so it still contests under step 2 and an explicitly written empty mapping still wins a
scalar it follows, exactly as stated above.

Example:

```text
a.x=1
a.*.z=3
```

The generated descendant is later than the scalar because the rule occurs later.

- namespace emits both `a.x=1` and `a.x.z=3`;
- JSON and YAML render `x` as an object containing `z`, omit scalar `1`, and warn;
- reversing source order makes the later scalar win in JSON and YAML.

### 4.5 Comments

A non-XML comment records:

- text;
- source order;
- association with a logical qualified path, sequence ordering value, or document position;
- whether it was leading, inline, or trailing where the source format distinguishes those positions.

Exact whitespace surrounding a comment in the *source* need not be preserved: leading and trailing spaces and tabs around the text are not part of it, and the marker never is. What each destination then writes is fixed by that format's output-byte rules in Section 19, not left to the writer, because Section 24 requires two implementations to agree on it.

An XML comment is not a non-XML comment and this does not apply to it. Section 11.5 retains it as an ordered content node whose content is the text between `<!--` and `-->`, and Section 19.5 writes that content back unchanged. Spaces and tabs inside it are part of the content and survive, because every conforming XML parser reports comment content without normalizing them; preserving them is not a stronger promise than the format already makes, it is declining to discard what the parser supplies. Line endings are the exception Section 3.3 already names, for the reason given in Section 19.5.

Normalized association rules are:

- a leading comment binds during parsing to the logical path or sequence item of the immediately following entry;
- an inline comment belongs to the entry or item on the same logical line;
- a trailing comment belongs to the immediately preceding entry or item;
- document-leading and document-trailing comments have no value owner;
- comments contributed to the same surviving logical path accumulate in source order;
- when several YAML inline comments accumulate at one path, the latest remains inline and earlier inline comments become leading comments in source order;
- overriding a payload or container contribution does not detach comments already bound to that logical path;
- the winning contribution determines the final output position of the logical path, and its bound comments move with it;
- permanent ignore masks remove the matching path and all comments bound to it;
- output-view `type=ignore` hides the matching path and its bound comments in that output instance;
- comments on a wildcard template are cloned onto each generated contribution in match order;
- standalone XML comments remain ordered content nodes and are not reassigned to adjacent values.

When a transformation re-addresses a value—such as `key` record construction, mixed-key `type=array`, input or destination `append`, or another rebase—comments bound to the source path or sequence item move with that value to its new address. Document-position comments do not move.

A transformation that collapses several values into one—`multiline` is the only one—moves the comments of every consumed value onto the single result, keeping source order. It needs no placement rule of its own: those comments now accumulate at one path, and the rule above for several inline comments at one path already says which of them stays inline. Nothing is discarded and nothing is reported, because a collapse changes where a note sits rather than whether it survives.

### 4.6 XML nodes

XML content requires these additional ordered node kinds:

- element;
- attribute;
- text;
- CDATA;
- comment.

An XML element records:

- expanded name: namespace URI plus local name;
- preferred prefix, when available;
- ordered attributes;
- ordered content nodes;
- in-scope namespace declarations needed for output.

XML processing instructions, document type declarations, entity declarations, and other node kinds not listed above are unsupported in this version.

### 4.7 Identity and order

Every source item and generated item must have a stable ordering key derived from:

1. CLI source ordinal;
2. item traversal ordinal within that source;
3. transformation declaration ordinal;
4. wildcard match ordinal;
5. deterministic local generation ordinal.

The key is the ordered tuple of those five components. Each is a nonnegative integer, and a component that does not apply to an item is zero. Keys compare lexicographically in the order listed, and "later" everywhere in this specification means greater under that comparison. A plain source entry therefore precedes every item its own transformations generate from the same source position, which is what Section 5.3 requires of generated entries that inherit their rule's precedence position.

The implementation may parse files concurrently, but concurrency must not alter this ordering key or any externally visible result.

## 5. Universal ordering and precedence

The same precedence principle applies to data entries, scheme entries, wildcard-generated entries, output declarations, and file-level collisions:

> Whatever occurs later in the declared source order has higher precedence.

### 5.1 Source order

Source order is:

1. CLI argument order;
2. physical entry order within each file;
3. command-line variables after all input files.

For scheme files, CLI scheme-file order and line order define the same precedence.

There is no implicit "more specific pattern wins" rule. A specific rule overrides a wildcard rule only when the specific rule appears later.

Example:

```text
a*.output=xml
a0.output=ignore
```

suppresses `a0`.

Reversing those lines causes the later wildcard declaration to restore XML output for `a0`.

### 5.2 Mapping order after override

Mapping order follows the position mark of each surviving winning logical path.

Overriding a mapping key moves that exact key, together with comments bound to it, to the winning contribution's position mark. A contribution to a strictly deeper descendant refreshes ancestor shape-marks under Section 4.4 but does not change an ancestor position mark or move an ancestor mapping key. Adding a new child therefore never moves its parent.

A node that no contribution addresses directly — one that exists only because something deeper needed a container to sit in — takes the position mark of the earliest contribution that required it, and keeps it. This follows from the preceding rule rather than adding to it: every contribution that could materialise such a node is a contribution to a strictly deeper descendant, and no such contribution may move it, so only the first one is free to place it. A later source that adds a second child to an existing intermediate node consequently leaves that node exactly where the first source put it. A node that is intermediate in one source and directly addressed in a later one does move to the later direct contribution, because that contribution addresses the node itself and is an override rather than a descendant.

The position mark is the Section 4.7 stable ordering key, so that key is already exhausted once two position marks are equal. When two sibling keys carry equal position marks, their order is decided by the child component itself: first by component kind, in the order ordinary component, qualified element, typed attribute, typed content; then, within one kind, by the component's own text — its literal text, its URI followed by its local text, its attribute name compared by this same rule, or its numeric ordering value — encoded as UTF-8 and compared by unsigned-byte ordinal order, matching the final tie-breaker used in Sections 17.5 and 21.3. A wildcard token sorts after any literal text at the same position, and two wildcard tokens compare by capture identifier with the bare form first. The component kind is compared before the text because a typed attribute and an ordinary component may carry the same text while naming different things, so text alone is not a total order. Mapping order is therefore total, and no output may depend on the order in which an implementation happened to visit siblings.

The same rule applies to comments: refreshing an ancestor's shape-mark through a descendant does not move that ancestor or comments bound to the ancestor path.

### 5.3 Generated-entry order

A wildcard rule is evaluated at the rule's source position.

Generated entries:

- inherit the rule's precedence position;
- are ordered by the source order of their matches;
- use their deterministic generation ordinal to break ties.

### 5.4 Sequence order

Sequence order is semantic and is represented by stable signed 64-bit integer **ordering values** in the range `0` through `9,223,372,036,854,775,807`.

Each sequence path has a high-water mark. It begins at `-1` and records the greatest ordering value ever allocated or explicitly supplied at that path earlier in source order, including values later removed or replaced.

- Native JSON/YAML/XML sequence items receive implicit ordering values one at a time as `high-water + 1`, in CLI source order and item order. A later native sequence therefore concatenates after all earlier allocated sequence items.
- An explicit numeric namespace or JSON/YAML mapping key supplies an explicit ordering value.
- Reusing an explicit ordering value overrides the existing item at that value by ordinary source order.
- Supplying an explicit value greater than the current high-water mark raises the high-water mark to that value.
- Gaps and nonzero bases are retained internally.
- Automatic allocation never shifts, defragments, or reuses an ordering value because an item was removed or replaced. A later explicit contribution may intentionally address an earlier value unless a permanent ignore mask suppresses it.
- Rendering sorts surviving items by ordering value and then emits dense destination indices where the destination requires indices.
- Allocating above the maximum ordering value is a blocking limit error. Gaps never allocate placeholder nodes.

For high-water accounting, any mapping child whose name is canonically spelled as a decimal value within the supported range reserves that ordering value at its own source position during concrete merging, whether or not its containing mapping ultimately qualifies for sequence inference. Final numeric-map inference changes projection only; it never retroactively reallocates native items.

Every sequence item retains ordering provenance:

- **implicit** for native sequence items and items created by structural transformations;
- **explicit** for canonical numeric mapping children.

This provenance survives `root`, `key`, `type`, output-instance construction, and destination planning.

When `append` rebases an explicit item from a later contribution, process items in ascending original ordering value. For each item, first raise the current high-water mark to at least its supplied value, then allocate its new value as `high-water + 1`. The original value is no longer addressable for that rebased item. A first or sole source contribution is not rebased merely because `merge=append` is configured.

During wildcard, ignore, reference, and scheme matching, a sequence exposes its stable ordering values as decimal name parts. These are real logical addresses for the run, not temporary dense ordinals.

## 6. Command-line interface

### 6.1 Invocation

```text
namespace2xml -i <input files> -s <scheme files> [options]
```

The executable may use a new product name, but it must provide a compatibility command or alias using the established invocation.

`--help` and `--version` are immediate informational modes. Argument scanning first checks for `--help`; when present, help is printed and the process exits successfully without validating any other argument. Otherwise, when `--version` is present, version information is printed and the process exits successfully without validating any other argument. Operational options accompanying either informational option are ignored.

Presence is decided by scanning the raw token vector for the option token, in either the bare or the inline form, up to the first `--`. That scan applies no other part of the grammar; in particular it does not work out which tokens are option values, because working that out is validation, and this decision precedes validation. So `namespace2xml --diagnostics-format --version` prints version information and exits `0` rather than reporting a missing value. The alternative would give one token two incompatible readings at once — a value to the informational scan, and an option token to the Section 6.2 rule that a detached value may not be an option token — and an implementation cannot be checked against a rule that contradicts itself. After `--` no token is an option, so `--version` there is an ordinary value and selects no mode.

### 6.2 Options

| Option | Required | Meaning |
|---|---:|---|
| `-i`, `--input` | yes | Ordered input file paths. |
| `-s`, `--scheme` | yes | Ordered scheme file paths. |
| `-o`, `--output` | no | Output root directory. Defaults to the current directory. |
| `-v`, `--variables` | no | Ordered namespace entries applied after all input files. |
| `--verbosity` | no | `trace`, `debug`, `information`, `warning`, `error`, `critical`, or `none`, case-insensitively. Default: `information`. |
| `--diagnostics-format` | no | `text` or `json`, case-insensitively. Selects the encoding of the diagnostic stream on standard error. Default: `text`. See Section 6.4. |
| `--max-input-bytes` | no | Maximum bytes per input file. Default: 256 MiB. |
| `--max-total-input-bytes` | no | Maximum total input bytes. Default: 1 GiB. |
| `--max-depth` | no | Maximum document or qualified-path depth. Default: 512. |
| `--max-nodes` | no | Maximum parsed nodes before generated entries. Default: 10,000,000. |
| `--max-xml-attributes` | no | Maximum attributes on one XML element. Default: 4,096. |
| `--max-comments` | no | Maximum retained comments. Default: 1,000,000. |
| `--max-comment-bytes` | no | Maximum total decoded comment bytes. Default: 256 MiB. |
| `--max-wildcard-rules` | no | Maximum wildcard rules. Default: 100,000. |
| `--max-wildcard-candidates` | no | Maximum `(rule,item)` candidate checks. Default: 100,000,000. |
| `--max-generated` | no | Maximum wildcard-generated nodes. Default: 10,000,000. |
| `--max-wildcard-iterations` | no | Maximum fixed-point iterations. Default: 1,024. |
| `--max-reference-depth` | no | Maximum reference recursion depth. Default: 4,096. |
| `--max-outputs` | no | Maximum planned destination files. Default: 100,000. |
| `--max-total-output-bytes` | no | Maximum total staged output bytes. Default: 4 GiB. |
| `--help` | no | Print help and exit successfully. |
| `--version` | no | Print version and exit successfully. |

Repeated `-i`/`--input`, `-s`/`--scheme`, and `-v`/`--variables` occurrences concatenate their values in exact command-line token order. A `--` token ends option recognition; every following token is consumed only as a value of the immediately preceding list-valued option, and using `--` without such an option is `CLI001`.

Option tokens are recognized by one uniform grammar, which applies to every option in the table above:

- while option recognition is active, a token beginning with `-`, other than the two tokens `-` and `--`, is an option token; an option token naming an option not in the table above is `CLI001`;
- a long option may carry its value inline as `--name=value`. The first `=` separates the name from the value, and the remainder is the value verbatim, including an empty remainder and any further `=`. `--name=value` supplies `value` exactly as though it were the token immediately following `--name`, except that an inline value is always a value and is never the `--` end-of-options marker;
- short options have no inline form, so the whole of a short-option token is its name. `-i=a` therefore names an option that does not exist and is `CLI001`, rather than silently supplying the value `a` or the value `=a`;
- `--help` and `--version` take no value. Section 6.1 decides the informational mode from the presence of the option token in either form, before any argument is validated, so an inline value on either is ignored rather than diagnosed;
- any other token is a value of the option currently accepting values. The token `-` is an ordinary value in this version. A value appearing when no option is accepting values is `CLI001`, as is an option token that reaches the end of the argument vector still requiring a value;
- a list-valued option accepts values until the next option token; every other option accepts exactly one value, and a later occurrence overrides an earlier one.

The inline form is available to every long option rather than to `--diagnostics-format` alone. A grammar with one exception has to be stated twice, tested twice, and explained twice, and the exception would fall on the one option whose parsing already happens twice under Section 6.4.1.

Limit-option values use ASCII decimal syntax:

- count/depth options accept `[1-9][0-9]*`;
- byte options accept `[1-9][0-9]*` optionally followed case-insensitively by `KiB`, `MiB`, or `GiB`, multiplying by 1,024, 1,048,576, or 1,073,741,824 respectively;
- whitespace inside a value, signs, fractional values, decimal SI suffixes, zero, negative values, and values exceeding signed 64-bit range are `CLI001`;
- multiplication overflow or a value exceeding an implementation's documented hard safety ceiling is `CLI001`;
- defaults shown with IEC units are semantic byte counts, not literal command-line spellings.

Verbosity is an output threshold ordered from most to least verbose:

1. `trace`: all diagnostics plus per-file parsing, wildcard candidate/match, generated-node, reference-chain, and publication details;
2. `debug`: all diagnostics plus pipeline-phase progress, merge decisions, expansion counters, and output-plan summaries;
3. `information`: information, warning, error, and critical messages;
4. `warning`: warning, error, and critical messages;
5. `error`: error and critical messages;
6. `critical`: critical host/runtime failures only;
7. `none`: no diagnostic or operational log output.

Normative warning and error conditions still occur and affect processing exactly as specified when hidden by the selected threshold. Verbosity never changes exit codes, output files, resource accounting, or the underlying deterministic diagnostic order. Under `--diagnostics-format json`, `none` still emits the empty array container as specified in Section 6.4.3.

`--help` and `--version` write their requested informational text to standard output. All diagnostics and operational trace/debug/information messages write to standard error. Generated configuration content is written only to planned destination files.

### 6.3 Exit codes

| Code | Meaning |
|---:|---|
| `0` | Success, including success with warnings. |
| `1` | Invalid CLI, invalid input, invalid scheme, reference failure, rendering failure, path violation, or publication failure. |

No user-caused error may escape only as an unhandled exception.

Cancellation or host termination may use a platform-conventional distinct exit code.

### 6.4 Diagnostic stream encoding

`--diagnostics-format` selects the encoding of the diagnostic stream written to standard error. It never changes which diagnostics occur, their fields, their cardinality, their order, processing, generated output files, resource accounting, or exit codes. It is an encoding switch over the stream already specified by Sections 15.4, 22, and 24.

#### 6.4.1 Format pre-scan

The selected encoding must be known before any other argument is validated, so that an invalid command line can itself be reported in the requested encoding. Implementations therefore perform a total pre-scan over the raw argument vector. The pre-scan always terminates, always resolves exactly one encoding, and never emits a diagnostic:

1. Walk tokens left to right. The scan stops at the first token exactly equal to `--`; tokens at or after that position are never interpreted by the pre-scan, because Section 6.2 makes them list-option values.
2. A token exactly equal to `--diagnostics-format` takes the immediately following token, if any, as its value verbatim, including a value that begins with `-`. A trailing `--diagnostics-format` with no following token, or one whose following token is `--`, contributes no value.
3. A token beginning with `--diagnostics-format=` takes the remainder after that first `=` as its value, including an empty remainder.
4. When several occurrences supply a value, the last one wins, matching the later-overrides-earlier rule used elsewhere in this specification.
5. The resolved encoding is `json` when the winning value is `json` under ASCII case-insensitive comparison, and `text` in every other case, including no occurrence, a missing value, an empty value, and an unrecognized value.

Ordinary option parsing then validates the option normally. A missing value, or a value that is neither `text` nor `json` under ASCII case-insensitive comparison, is `CLI001`, reported in the encoding the pre-scan resolved. The pre-scan result is never revised by later parsing.

`--help` and `--version` retain the Section 6.1 precedence. When either is present, the process writes its informational text to standard output, exits with code `0`, and writes no diagnostic stream in either encoding, including when `--diagnostics-format` itself carries an invalid value.

Informational output is never encoded as JSON in this version. `--version` output is nevertheless machine-readable: it contains one `<field>: <value>` line per field, using LF terminators, and includes at least a `version` field and a `contract-bundle` field as defined in Section 22.

#### 6.4.2 `text` encoding

`text` is the default and preserves the established human-readable stream. Diagnostics and enabled operational trace, debug, and information messages are interleaved on standard error. Each phase's buffered diagnostic set is written at that phase boundary in Section 24 order. Prose is localizable and is not part of byte-identical determinism.

#### 6.4.3 `json` encoding

Under `json`:

- standard error carries exactly one JSON array and nothing else, so the whole stream is parseable without framing heuristics;
- operational trace, debug, and information messages that are not diagnostics are suppressed entirely, at every verbosity, because they would break that guarantee. Use `text` to obtain an operational trace;
- the array is buffered across every pipeline phase and written exactly once, immediately before process exit, so that late `PATH002` publication diagnostics appear in the same array as earlier phases. A phase abort under Section 15.4 does not discard the buffer;
- the array container is always written, overriding the Section 6.2 statement that `none` produces no diagnostic output. `--verbosity none`, and any threshold that filters every produced diagnostic, yields exactly the two bytes `[]` followed by one LF;
- `--verbosity` otherwise filters array elements by severity exactly as it filters text lines, and never reorders or renumbers them;
- a failure to write the diagnostic stream itself is not a diagnostic and does not change the exit code.

The byte layout is fixed so that the stream satisfies Section 24 byte-identity:

- UTF-8 without a BOM;
- `[` LF, then each element as one compact object on its own line with no insignificant whitespace, then `,` LF between consecutive elements, then LF `]` LF;
- the empty array is `[]` LF;
- members appear in this order and omitted members are absent, never `null`: `code`, `severity`, `phase`, `source`, `line`, `column`, `path`, `declaration`, `rule`, `destination`, `spec`, `message`;
- strings escape `"` and `\` with a backslash, use `\b`, `\f`, `\n`, `\r`, and `\t` for those five controls, use lowercase `\u00xx` for every other C0 control, and emit every other Unicode scalar literally as UTF-8;
- integers use ASCII decimal with no sign, leading zero, or exponent.

`code`, `severity`, `phase`, `spec`, and `message` are required on every element. The remaining members are present exactly when the condition supplies them, as described in Section 22.

`phase` is a closed enumeration corresponding to the Section 24 emission phases, and is a property of the individual occurrence rather than of the code, because `TYPE001`, `LIMIT001`, and `SERIALIZE001` occur in more than one phase:

| Value | Covers |
|---|---|
| `cli` | Argument scanning and option validation, before Section 15.1 step 1 |
| `scheme` | Section 15.1 steps 1 through 4 |
| `input` | Section 15.1 steps 5 through 12 |
| `planning` | Section 15.1 steps 13 through 19 |
| `publication` | Section 15.1 step 20 |

`spec` is the anchor of the normative clause the diagnostic enforces, spelled `§` followed by the section or appendix number, optionally followed by a `.` and further parts, for example `§13.1` or `§B`. It is required in emitted output. A conformance fixture compares it only when the expected object declares it, so that renumbering the specification does not invalidate the whole corpus.

`message` is human-readable prose. It is required in emitted output, is localizable, and is never compared.

The array is described by the following closed schema, which is normative. `additionalProperties` is `false`, so an implementation may not extend an element without a specification change:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "array",
  "items": {
    "type": "object",
    "additionalProperties": false,
    "required": ["code", "severity", "phase", "spec", "message"],
    "properties": {
      "code": { "type": "string", "pattern": "^[A-Z]+[0-9]{3}$" },
      "severity": { "enum": ["error", "warning"] },
      "phase": { "enum": ["cli", "scheme", "input", "planning", "publication"] },
      "source": { "type": "string", "minLength": 1 },
      "line": { "type": "integer", "minimum": 1 },
      "column": { "type": "integer", "minimum": 1 },
      "path": { "type": "string" },
      "declaration": { "type": "string" },
      "rule": { "type": "array", "items": { "type": "string" }, "minItems": 1 },
      "destination": { "type": "string" },
      "spec": { "type": "string", "pattern": "^§[0-9A-Z]+(\\.[0-9]+)*$" },
      "message": { "type": "string" }
    },
    "dependentRequired": { "column": ["line"], "line": ["source"] }
  }
}
```

`source` is the path token exactly as it was supplied, with every `\` rewritten to `/` and nothing else changed: it is never absolutized, canonicalized, case-folded, or link-resolved, so `../a.txt` is reported as `../a.txt` and an absolute token is reported absolute. A diagnostic member is compared byte for byte by a conformance fixture, and every other choice would make the compared bytes depend on the working directory the run started in, the shape of the filesystem beneath it, or the host's rules for resolving a path — none of which the run's own arguments determine. Prose in `message` may name a resolved path, because `message` is never compared.

`destination` is the canonical destination path of Section 17.5, which is a property of the output plan rather than of the invocation, and so does not vary with `--output`.

`path` is a canonical qualified path under Appendix A: the name parts joined with `.`, each part spelled as Section 19.1 spells a namespace name, with no `root` applied and no output-format projection. A part containing the delimiter carries it escaped by the Section 16.4 `\u{HEX}` rule, so the joined string parses back to the same parts. The path names the node the rule was enforced against, not the component that failed — a report that named only the component would identify what is wrong without identifying where, which in a run over a large model is not actionable.

## 7. Input discovery and reading

### 7.1 File extensions

Input file extensions are matched case-insensitively:

- `.json`;
- `.yaml`;
- `.yml`;
- `.xml`;
- every other extension uses namespace-profile parsing.

Format support matrix:

| Format | Input | Output |
|---|---:|---:|
| namespace profile | yes | yes |
| JSON | yes | yes |
| YAML | yes | yes |
| XML | yes | yes |
| quoted namespace / POSIX shell assignments | no | yes |
| INI | no | yes |

`.ini`, `.sh`, and other unrecognized extensions continue to select namespace-profile input for compatibility. Native INI and shell input are outside this version.

### 7.2 Missing files

A missing input or scheme file:

- emits a warning containing its resolved path;
- contributes no data;
- does not by itself cause failure.

Only a path that does not exist receives this warning-and-ignore behavior. A path that exists but is unreadable, is a directory where a file is required, changes incompatibly while being read, or fails for another I/O reason is a blocking `PARSE001` source error.

Each `-i` or `-s` token that names a path which does not exist emits its own warning, so a path written twice warns twice. Section 4.7 gives every occurrence its own source ordinal and the model treats each as a separate source, which is observable for files that do exist: a JSON input holding `["a"]` supplied twice concatenates to `["a","a"]`. Counting the warning per distinct path rather than per occurrence would report fewer missing sources than the invocation actually named, and where the same path is supplied once as an input and once as a scheme the two occurrences differ in `phase`, which Section 22 makes a property of the individual occurrence. Reporting only one of those would leave the other missing file unmentioned while appearing to have reported it.

One check precedes this one. Section 15 rejects a scheme file named with the `.xml` extension before the file is read, and therefore before its existence is known, so a missing `.xml` scheme file is that blocking `PARSE001` rather than this warning. Naming the extension is an authoring error whichever answer the file system would have given, and reporting it as an ignorable missing file would name the wrong mistake and let the run continue as though the author had supplied no scheme at all.

Another check precedes both. An empty token supplied to `-i`, `-s`, `-v`, or `-o` is a blocking `CLI001` at Section 6.2 option-value validation, before any path is resolved. The empty token names nothing: it cannot be tested for existence, so it is neither the missing file this section forgives nor the I/O failure it blocks on, and it is indistinguishable from an option whose value the caller's own quoting silently dropped — the overwhelmingly common way one arrives. Reporting it against the option that received it names the mistake where it was made. This is a command-line failure rather than a source failure, so it is `CLI001` and not `PARSE001`, and it is diagnosed even for `-o`, which resolves no source at all.

### 7.3 Parsing concurrency

Files may be read and parsed concurrently.

Merging, array concatenation, wildcard evaluation, and precedence assignment must nevertheless use CLI source order.

Concurrent parsers maintain per-source counts only. A parser must not be able to observe any global total: it accumulates its own source's contribution and reports it, and nothing more. This is what makes the outcome independent of how work was scheduled, so an implementation should enforce the separation structurally rather than by convention.

After all independently readable sources finish their parse attempt, global input budgets are evaluated deterministically over one ordered input stream: all scheme files in `-s` order, then all input files in `-i` order, then command-line variables in `-v` token order, matching the consumption order Section 23 gives `--max-total-input-bytes`. The first source whose cumulative contribution would cross a global bound receives `LIMIT001`; that source and every later source in that stream contribute no parsed model, including later sources of a different kind. Per-file byte limits and per-document depth limits are enforced within each source and are never cumulative across sources.

### 7.4 Character encoding

UTF-8 is the default input encoding.

Recognized byte-order marks are:

- UTF-8 BOM: decode as UTF-8;
- UTF-16 little-endian BOM: decode as UTF-16LE;
- UTF-16 big-endian BOM: decode as UTF-16BE.

UTF-32 and unrecognized byte-order marks are errors. Without a recognized BOM, input is strict UTF-8. Invalid byte sequences are errors.

When decoding fails, the reported position is the first character position the byte stream could not produce. The longest prefix of the byte stream that decodes successfully determines it, and line and column locate the position immediately after that prefix. A prohibited byte-order mark therefore reports line 1, column 1, because no prefix of such a stream decodes.

BOM detection tests four-byte signatures before three- or two-byte signatures. Consequently `FF FE 00 00` and `00 00 FE FF` are recognized as prohibited UTF-32 BOMs rather than as UTF-16 prefixes.

A recognized byte-order mark is an encoding signature and not text. Decoding consumes it, and it is not part of the decoded character stream; every line, column, and offset is measured over the decoded stream after it is removed. U+FEFF anywhere else is ordinary data, including immediately after a byte-order mark, so a UTF-8 file beginning `EF BB BF EF BB BF` decodes to a single U+FEFF character.

A byte-order mark is an encoding of U+FEFF, and U+FEFF has exactly five encodings. All five are named above: three are recognized and two are prohibited. The unrecognized case therefore has no members, and a conforming implementation carries no signature table beyond those five. Introducers for other encodings, notably UTF-7's `2B 2F 76` and the UTF-1, UTF-EBCDIC, SCSU, BOCU-1, and GB18030 signatures, are not byte-order marks and receive no special treatment: each is either ordinary UTF-8 text or an invalid byte sequence under the rules already stated. No input is ever decoded as anything other than UTF-8, UTF-16LE, or UTF-16BE.

## 8. Namespace profile language

### 8.1 Entry

```text
qualified.name=value
```

The first separating `=` divides the name from the value. Values may be empty. An ordinary namespace name cannot contain a separating `=`.

A separating `=` is an unescaped `=` that does not fall inside a `Q{...}` URI. Section 11.4 makes `Q{...}` one atomic lexer context whose only escapes are `\}` and `\\`, so an `=` in a namespace URI cannot be escaped and must be recognized by position instead, exactly as Section 8.4 recognizes the first unescaped `}` that terminates a reference. Without this rule a namespace URI containing `=` would be unwritable: XML input could produce such a path and a reference in a value could address it, but no profile entry could define one and no scheme record could select one. Section 16.4 also emits such a URI with a literal `=`, so the tool's own namespace-profile output would not be readable as its input.

A namespace-profile file is a sequence of physical records:

- LF, CRLF, and lone CR terminate a record and are not part of it;
- the final record need not have a terminator;
- U+0085, U+2028, and U+2029 are ordinary data and never terminate a record;
- an empty record or one containing only spaces and tabs contributes nothing;
- every other record is not trimmed: all characters before the first separating `=` are the name and all characters after it are the value, including leading and trailing spaces and tabs.

Records are classified in this order:

1. an empty or space/tab-only record is ignored;
2. if the first non-space/tab character is `#`, the record is a comment and preceding spaces/tabs are not comment text; a record beginning with `\#` is not a comment;
3. if the first non-space/tab character is `!`, the record is a permanent mask and preceding spaces/tabs are not part of its pattern; a record beginning with `\!` is not a mask;
4. otherwise the record must contain a separating `=` and is an ordinary entry;
5. any remaining record without a separating `=` is `PARSE001`.

Each `--variables` argument is exactly one namespace record. It accepts ordinary entries and permanent `!pattern` masks but not comment records. A comment record supplied as a `--variables` argument is `PARSE001`.

A diagnostic reporting a condition inside a command-line variable omits `source`, and therefore also omits `line` and `column`. The Section 6.4.3 `source` member names an input or scheme file, and a variable is neither; a synthetic file name there would be indistinguishable from a real one. The variable is identified in the diagnostic's message by its one-based position in `-v` token order, and its Section 4.7 ordering key still places it after every input file, so the stream order is unaffected.

### 8.2 Qualified names

An unescaped `.` separates name parts.

Within a name:

- `\.` means a literal dot;
- `\*` means a literal asterisk;
- `\=` means a literal equals sign;
- `\#` means a literal number sign;
- `\!` means a literal exclamation mark;
- `\$` means a literal dollar sign;
- `\@` means a literal at sign;
- `\}` means a literal closing brace outside a `Q{...}` URI;
- `\Q` means a literal `Q`, principally to disambiguate an ordinary name part beginning with `Q{` from an XML canonical component;
- `\\` means a literal backslash;
- `\u{HEX}` means the Unicode scalar value written with one to six hexadecimal digits; input accepts either case and normalized output uses uppercase;
- unescaped `*` is a wildcard capture;
- `*[identifier]` is an explicitly identified wildcard capture.

Every other backslash sequence in a name is a blocking parse error.

`\u{HEX}` requires one through six digits and a closing brace. Values above U+10FFFF, values in the surrogate range U+D800 through U+DFFF, empty digit strings, and malformed or unterminated forms are `PARSE001`.

At the start of a name part, unescaped `@`, `#` followed by canonical decimal digits, and `Q{` introduce typed XML canonical components in namespace-profile input, command-line variables, namespace-profile scheme paths, references, and `root` values. Escaped forms such as `\@x`, `\#0`, and `\Q{urn:x}name` are ordinary literal name parts.

Marker recognition commits. Once an unescaped `@`, `#`, or `Q{` is recognized at the start of a name part, that part must match the typed production in full; text that begins like a typed marker without completing one is `PARSE001`, not an ordinary part. `#1x`, `@`, and `Q{urn:x` are therefore each errors, and the ordinary parts carrying that text are written `\#1x`, `\@`, and `\Q{urn:x`, which is exactly what Section 19.1 emits for them. The rule keeps the lexer local and total, since it never has to unread a part, and it makes Section 11.4's blocking errors inside `Q{...}` consistent with the other two markers rather than an exception to them. Under the alternative, a one-character edit would silently change a part's kind: `#1` would be a content token and `#1x` an ordinary name.

JSON and YAML mapping keys are one component that carries the same markers every other name syntax carries, under the native-key rules of Section 9.1: `@x` is an attribute, `#0` is a content component, `Q{uri}x` is a qualified element, and a literal marker-shaped key is escaped `\@x`. Only the delimiter and `\u{HEX}` lose their meaning there, because a key is one part rather than a path. XML input receives typed components from the XML parser rather than by applying this namespace lexer. `filename`, delimiter, option, `key` field-name, and other non-path directive values treat marker-shaped text as ordinary value text.

An identifier consists of ASCII letters, digits, `_`, or `-` and must not be empty.

Numeric identifiers such as `*[0]` are valid.

An empty name part is a blocking parse error. A qualified name must not begin or end with an unescaped delimiter or contain consecutive unescaped delimiters.

### 8.3 Value escapes

Within an interpreted namespace-profile value:

- `\\` emits `\`;
- `\*` emits literal `*`;
- `\${` emits literal `${`;
- `\n` emits LF;
- `\r` emits CR;
- `\t` emits tab;
- other backslash sequences preserve the backslash and following character.

Namespace values intentionally do not support `\u{HEX}`. Non-ASCII characters are written directly as UTF-8, while unknown value escapes remain literal. This differs from strict qualified-name escaping by design.

Within an interpreted JSON, YAML, or XML string that has already been decoded by its native parser:

- scan the decoded string once from left to right by Unicode scalar;
- a backslash immediately followed by `*` emits `*` and consumes both scalars;
- a backslash immediately followed by `${` emits `${` and consumes the backslash, dollar sign, and opening brace;
- every other backslash emits that backslash alone and consumes only it, so the following scalar is processed normally;
- no substring rescanning or second general `\\` decoding pass occurs.

### 8.4 References

An unescaped reference is:

```text
${qualified.name}
```

Every unescaped `${` in an interpreted value must begin a syntactically valid reference. The reference name uses the qualified-name and typed-component grammar of Section 8.2, except that wildcard captures are permitted only as specified in Sections 12.1 and 12.2. The first unescaped `}` outside a `Q{...}` URI terminates the reference. Invalid or unterminated reference-looking text is a blocking `REFERENCE001` error.

Values such as logger templates must escape the sequence:

```text
\${yyyy-MM-dd SEVERITY MESSAGE}
```

or use a matching `substitute=Key` or `substitute=None` scheme rule.

Substitution-mode path patterns are compiled from the raw scheme before structured string values are interpreted. They may contain name wildcards but must not contain references or depend on generated data.

### 8.5 Comments

A namespace comment is a physical line whose first non-whitespace character is an unescaped `#`. A `#` that is not the first non-space/tab scalar of a record never begins a comment; whether it is ordinary text or a Section 8.2 typed marker is decided by Section 8.2, which makes `a.#1` a content token rather than an ordinary name.

The comment's text is the remainder of the record after that `#`, with leading and trailing spaces and tabs removed; Section 4.5 does not require surrounding whitespace to survive. The marker is not part of the text. Section 16.9 selects an output comment marker independently of the input, and Section 20 prefixes every emitted physical line with `# `, so text that kept its input marker would be emitted twice over, and a comment read from `#` would reach an INI destination that selected `;` as `; #...`. Any further `#` scalars belong to the text, because only the first one is a marker.

Consecutive comments are associated with the next entry, with one exception: comments preceding the first entry of a source are document-leading, as Section 20 classifies the first position for every format. Trailing comments with no following entry remain document-trailing comments. A source that forms no entry at all has no contribution for a comment to trail, so its whole run is the opening run and is document-leading.

Binding a source's opening comments to its first entry would be, at the scale of the whole source, the failure the next paragraph exists to prevent: an ignore mask over the first entry, or a later contribution replacing it, would carry off a comment that describes the file rather than that entry. Section 20 therefore governs the first position in every format, so a profile converted between formats keeps its header. The cost is that the first entry of a source cannot be given a leading comment; a comment written there describes the source. That cost is not confined to placement: an opening comment is bound to no path, so Section 5.2 does not move it when the first entry is overridden and Section 16.5 does not carry it into a generated record. A source whose first entry needs a comment of its own must be written with that entry second.

A document-leading comment is bound to no path, so nothing selects which outputs it belongs to, and it is emitted in every output instance the run produces. Where a run writes several files, the comment therefore appears in each of them, and this is not `WARN003`: nothing is discarded. Each output is a standalone document that must be readable on its own, and a header explaining why a file's settings are commented out is worth as much in the second document as in the first. The alternative — choosing one instance and announcing the omission from the others — requires the specification to name a winner among files whose order is a rendering detail, and would leave the remaining documents missing a note whose whole purpose is to be found next to the settings it describes. An `output=ignore` mask does not suppress these comments either, for the same reason: the mask selects paths, and a document-leading comment has none.

Only an entry ends a run of comments. A record ignored under Section 8.1 rule 1, a permanent mask, and a record reported as `PARSE001` all leave the run open, so comments separated from their entry by any of them still bind to it, and comments on either side of such a record bind to the same entry. Requiring adjacency instead would let a blank line silently discard a comment block, and would let inserting an unrelated mask move a comment onto a different value. A wildcard template is an entry, so a run binds to it and Section 4.5 clones the run onto each contribution the template generates.

The association is made with the following entry's logical qualified path before overrides are evaluated. It therefore survives replacement of that path and moves to the winning contribution's output position as specified in Sections 4.5 and 5.2.

### 8.6 Ignore entries

```text
!qualified.name.pattern
```

The legacy form with an ignored value remains accepted:

```text
!qualified.name.pattern=ignored
```

Ignore patterns use the wildcard rules in Section 12.

An ignore entry creates a permanent run-wide subtree exclusion mask:

- every concrete or generated contribution matching the pattern is suppressed, regardless of whether it appears before or after the ignore entry;
- suppressed paths and descendants never become wildcard candidates, reference targets, output-selector matches, or rendered content;
- a later concrete or generated contribution cannot recreate the path;
- multiple ignore masks form a union;
- masked contributions still reserve any canonical ordering value for high-water stability, then are discarded before literal-path merge validation and therefore never trigger `merge=error`;
- comments bound to suppressed paths are suppressed with them;
- a reference to a suppressed value is a missing reference.

Example:

```text
a.x=1
!a.*
a.x=2
```

produces no `a.x` value.

Ignore masks remain active throughout wildcard fixed-point evaluation and are applied to every candidate when it appears. They are never rendered. This permanent exclusion is an explicit exception to universal later-source precedence.

Native JSON, YAML, and XML input have no tombstone syntax in this version. Keys beginning with `!` are ordinary literal keys.

Use a namespace-profile `!pattern` entry or a scheme `type=ignore` directive for removal. This keeps awkward control syntax in the scheme rather than overloading native data formats.

### 8.7 Numeric paths and ordered sequences

A nonempty namespace or structured mapping is classified as sequence-inferable when, after permanent masks and wildcard generation, all its surviving concrete child names are canonical nonnegative decimal ordering values. “Surviving” means not suppressed by a permanent mask. Projection as an explicitly indexed sequence occurs at pipeline step 11.

Within an explicit indexed contribution:

- valid spelling is `0` or a nonzero digit followed by decimal digits and its numeric value must not exceed `9,223,372,036,854,775,807`;
- leading-zero spellings such as `00` and `01` are ordinary mapping keys and prevent sequence interpretation;
- a canonically spelled decimal above the supported maximum is an ordinary mapping key and prevents sequence interpretation;
- repeated ordering values are resolved by ordinary later-entry precedence;
- gaps and nonzero bases are allowed;
- missing values do not create null placeholders.

Explicit indexed contributions patch the sequence at their supplied ordering values.

Native JSON/YAML arrays and homogeneous XML arrays use the high-water allocation rule in Section 5.4. They concatenate in CLI source order.

Example:

First file:

```text
a.0.x=one
a.1.x=two
```

Second file:

```text
a.1.x=three
```

produces a two-item sequence: `one`, `three`.

By contrast, native YAML arrays:

```yaml
a: [one, two]
```

followed by:

```yaml
a: [three]
```

produce `one`, `two`, `three`.

Explicit values followed by a native implicit array concatenate rather than patch:

```text
a.0=x
a.1=y
```

followed by:

```yaml
a: [z]
```

under `merge=deep` produces ordering values `0=x`, `1=y`, and `2=z`. The native item is implicit and therefore receives a fresh value above the high-water mark.

To replace rather than concatenate a sequence, use the scheme merge strategy `replace`.

Each input file is one source contribution. Each command-line variable argument is a separate source contribution in argument order.

Entries within one source contribution are folded by ordinary source-order override before input-path `merge` compares that source with another contribution. Every wildcard-generated `(rule,match)` result is a separate generated contribution for `merge=error` accounting.

Permanent ignore masks suppress matching ordering values for the rest of the run. Output-view `type=ignore` hides an item only in that output instance. Neither operation retargets another item or lowers the allocator high-water mark. Namespace and INI output must display fresh dense indices where their projection requires indices, but matching and precedence continue to use stable ordering values.

Numeric-map inference occurs once, after wildcard generation and permanent ignores reach their fixed point. Before that phase, numeric mapping keys are ordinary addressable path parts, so templates and ignore masks can match them without requiring provisional sequence classification. A surviving empty mapping remains a mapping. Classification does not restart after output-view transformations.

When multiple sources contribute native implicit sequences at one path and no explicit `merge` directive applies, emit one compatibility warning explaining that implicit items concatenate while explicit ordering values patch.

## 9. JSON input

### 9.1 Supported features

JSON input supports:

- objects;
- arrays;
- strings;
- arbitrary-precision JSON numbers;
- Booleans;
- null;
- object property order.

A JSON number whose lexical form contains a fraction part or exponent is an arbitrary-precision decimal. Every other valid JSON number is an arbitrary-precision integer.

Each object-property name becomes one qualified-name part. Dots and `\u{HEX}` sequences in the native property name remain literal characters: a key is one part's worth of text, not a written qualified name, so nothing in it separates parts.

That one part carries the Section 11.4 markers. A key whose text begins with an unescaped `@`, `#`, or `Q{` is the typed component that marker introduces, so an attribute written by this tool reads back as the same attribute and a JSON overlay can name any component an XML input contributed. Marker recognition commits exactly as in Section 8.2: a key that begins like a marker without completing the production is `PARSE001`.

A backslash at the start of the key escapes a following `@`, `#`, `Q`, or `\`, contributing that character literally and suppressing marker recognition for the whole part. A key holding the literal text `@x` is therefore written `\@x`, and one holding `\@x` is written `\\@x`. Elsewhere in the key, and before any other character, a backslash contributes itself and consumes nothing, so a key such as `C:\dir` needs no escaping.

Within that one part, unescaped `*` and `*[identifier]` tokens retain their wildcard-template meaning for compatibility. `\*` suppresses wildcard interpretation and contributes a literal `*`; other backslashes are preserved.

String scalar values use the same strict reference and value-escape lexer as namespace values unless a matching `substitute` directive disables interpretation.

### 9.2 Unsupported features

JSON comments are not supported.

Trailing commas, duplicate object keys, non-finite numbers, and other nonstandard extensions are errors unless a future `jsoninputoptions` value explicitly enables them.

### 9.3 Duplicate keys

Standard mode rejects duplicate keys within one JSON object.

This avoids parser-dependent behavior and accidental hidden overrides.

## 10. YAML input

### 10.1 Supported features

YAML input supports:

- ordered mappings;
- sequences;
- strings;
- integers;
- decimals;
- Booleans;
- null;
- leading, inline, and trailing comments.

Every conforming YAML parser must expose these supported comment positions. Comments are retained in the common model and emitted by YAML output after normalized formatting.

String scalar values use the same strict reference and value-escape lexer as namespace values unless a matching `substitute` directive disables interpretation.

YAML parsing uses the product-defined restricted schema `RestrictedYaml1`, inspired by the JSON-compatible subset of YAML 1.2:

- `true` and `false`, case-insensitively, are Boolean;
- `null`, `Null`, `NULL`, `~`, or an empty plain scalar are null;
- JSON-compatible decimal integers and floating-point values are numeric;
- values such as `yes`, `no`, `on`, `off`, timestamps, and sexagesimal numbers remain strings;
- duplicate mapping keys are errors;
- an unquoted merge key (`<<`) is an error; quoting it makes it an ordinary string key;
- every plain or quoted scalar mapping key is treated as a string without scalar tag resolution;
- complex mapping keys are errors;
- construction is safe and never instantiates application-defined types.

The merge key is an error rather than an ordinary key because the alternative fails silently. `RestrictedYaml1` performs no merge, so an accepted `<<` would place the referenced mapping one level too deep, under a member literally named `<<`. Nothing would appear to be missing — the data is all present, at the wrong path — which is the failure mode Section 9.3 exists to prevent. An error names the clause instead, and the quoted spelling `"<<"` remains available for a document that means the two characters as data.

A component whose text is `<<` is therefore written quoted on YAML output, as Section 19.4's spelling rule already requires of any scalar whose plain form would not read back as itself. Emitting it plain would produce a document this tool refuses to read.

`RestrictedYaml1` is intentionally not the complete YAML 1.2 Core Schema. A future `yamlinputoptions` mode may add full Core Schema resolution.

These rules, rather than an underlying library's advertised YAML 1.1 or 1.2 mode, are normative. Any parser may be used only if it is configured or wrapped to produce exactly `RestrictedYaml1` behavior.

The differences from namespace scalar inference are intentional: plain YAML `+1`, `.5`, and `1.` remain strings because they are not JSON-compatible numbers, while any ASCII case spelling of `true` or `false`, including `tRuE`, is Boolean.

### 10.2 Deliberately unsupported features

This version does not preserve:

- anchors;
- aliases;
- custom tags;
- directives;
- explicit document markers such as `---` and `...`;
- exact scalar quote style;
- folded-versus-literal multiline style;
- exact indentation;
- exact comment spacing.

Anchors, aliases, and every explicit tag token—including standard `!!` tags, verbatim tags, and local tags—are blocking input errors in this version, regardless of whether the tag would preserve the same basic value.

Future input options may define additional safe tag behavior; no tag is accepted implicitly.

### 10.3 Multiple documents

Multiple YAML documents in one input stream are not supported in this version.

Encountering any explicit document marker is an error.

A stream holding *no* document node is the symmetric case and is also an error: a YAML source is exactly one value, and a stream that is empty, or holds only comments and blank lines, supplies none. It is a `PARSE001` source error under Section 10.1 rather than an empty contribution, because the two readings are not distinguishable afterwards — a source that contributes nothing and a source that was never valid produce the same merged model, and only one of them is a mistake worth naming. A source deliberately reduced to nothing is expressed by omitting it from `-i`, or by an empty mapping `{}`, which *is* one document node.

### 10.4 Wildcard templates supplied as YAML

YAML mapping keys must be strings. Each key becomes one qualified-name part under the Section 9.1 native-key rules: dots and `\u{HEX}` remain literal, the Section 11.4 markers apply to a key beginning with an unescaped `@`, `#`, or `Q{`, and a leading backslash escapes one of those and suppresses marker recognition. Elsewhere a backslash remains literal.

Within that part, unescaped `*` and `*[identifier]` tokens use the wildcard-template grammar. `\*` contributes a literal asterisk.

Example template file, given the data file below on the command line **first**:

```yaml
a:
  '*':
    c: XXX
```

creates the logical wildcard entry:

```text
a.*.c=XXX
```

Wildcard template entries are extracted before structural input merging and expanded during the fixed point in Section 12.4, before numeric-map sequence inference and output rendering.

Extraction is entry-by-entry. Carrier ancestors created only to contain an extracted template do not contribute mapping-presence marks. Literal sibling entries remain concrete data and do contribute their ordinary ancestor mapping marks.

A sequence beneath a wildcard key is extracted through its items' ordering values, which Section 5.4 exposes as decimal name parts. A template written

```yaml
a:
  '*':
    b:
      - x
      - y
```

therefore creates the logical wildcard entries `a.*.b.0=x` and `a.*.b.1=y`, and is the same rule as those two entries written in namespace form. The items become canonical numeric mapping children, so Section 5.4 gives them explicit ordering provenance even though the source spelled a native sequence; extraction flattens native shape into namespace-shaped entries here exactly as it does for the mapping ancestors above.

That provenance is observable only against a destination that already holds items, because it decides whether the template's values address the destination's ordering values or extend past them. Given the template

```yaml
a:
  '*':
    tags:
      - red
      - blue
```

supplied after the input

```yaml
a:
  x:
    tags:
      - green
```

the destination's `green` holds the implicit ordering value `0`, and the template supplies the explicit values `0` and `1`. Section 5.4 makes reuse of an explicit ordering value override the existing item at that value by ordinary source order, so the result is

```yaml
a:
  x:
    tags:
      - red
      - blue
```

and not `[green, red, blue]`. A template written this way replaces the items it addresses rather than appending to them. To append instead, configure `merge=append` at the target path under Section 16.10.

A wildcard key whose value is an **empty mapping or an empty sequence** has no entries for entry-by-entry extraction to find, so the template would contribute nothing at every path it matched. That is `PARSE001` against this section, once per failing source, reported at step 7.

Given the input

```yaml
a:
  - b: 1
  - b: 2
```

supplied ahead of the template above, the result is:

```yaml
a:
  - b: 1
    c: XXX
  - b: 2
    c: XXX
```

The argument order matters to this example and the example does not settle it. Mapping-sibling order is governed by Section 5.3 and the Section 4.7 ordering key alone: a generated entry inherits its rule's precedence position, so it sorts against a concrete sibling by the position of the **rule**, not by the moment of generation. Supplying the template first therefore renders `c` ahead of `b`. This example prints the data file first so that its printed result reads naturally; nothing in it overrides Section 5.3.

## 11. XML input

### 11.1 Secure parser configuration

XML parsing must:

- prohibit document type declarations;
- prohibit external entity resolution;
- prohibit network retrieval;
- prohibit external schema retrieval;
- accept predefined XML entities and numeric character references.

A document containing a DTD is rejected rather than partially processed.

Resource bounds for XML input are the general bounds of Section 23 plus exactly one XML-specific bound. This specification fixes the accounting rather than leaving it to the implementation:

| Aspect | Governing bound |
|---|---|
| Document size | `--max-input-bytes` per file and `--max-total-input-bytes` cumulatively, measured on encoded input bytes before decoding |
| Nesting depth | `--max-depth`, counting the document element as depth 1 |
| Node count | `--max-nodes`, which every element, attribute, text, comment, and CDATA overlay node consumes |
| Attributes per element | `--max-xml-attributes`, default 4,096 |
| Entity and character-reference expansion | no separate bound; see below |

Entity expansion needs no budget of its own, and an implementation must not impose one. Because document type declarations and custom entities are prohibited, the only recognized references are the five predefined entities and numeric character references, and each expands to exactly one Unicode scalar value. Decoded length therefore never exceeds encoded length, so unbounded-expansion attacks are structurally impossible rather than merely bounded. Where a host XML reader exposes an entity-expansion knob, it is configured so it can never be the effective limit. Decoded character data still consumes `--max-nodes`, `--max-comments`, and `--max-comment-bytes` exactly as other formats do.

`--max-xml-attributes` counts every attribute on one element, including namespace declarations, before any overlay node is materialized. Crossing it is `LIMIT001`. Like `--max-depth`, it is a per-source bound and is never cumulative across sources, so a source that crosses it contributes no partial overlay under Section 15.4.

`LIMIT001` remains once per invocation. When more than one per-source or global bound is crossed in the parse phase, the reported occurrence is the earliest under CLI source order as defined in Section 7.3, then document order within that source, then element order, then the bound name compared as unsigned UTF-8 bytes. Attribution is therefore independent of parser worker scheduling.

### 11.2 Supported XML subset

XML input supports:

- one document element;
- namespace declarations;
- namespaced elements and attributes;
- attributes;
- element children;
- text;
- mixed content;
- CDATA;
- comments;
- empty elements;
- `xml:space`.

The XML declaration is not retained as a data node.

An element or attribute name emitted as XML must match the `NCName` production of Namespaces in XML 1.0, Third Edition. A name component holds arbitrary text — a JSON or YAML key need not be an XML name at all — so a component that does not match is `XML002` at the point the name would be written, and only there. The same component reaches a JSON, YAML, namespace, quoted-namespace or INI destination unchanged, because nothing in those formats constrains it; a run that writes no XML never consults this rule.

`NCName` rather than `Name`, which admits a colon. A component written `a:b` would be emitted as `<a:b>` and read back as the local name `b` in whatever namespace the prefix `a` was bound to — a different component from the one written, in a namespace the model never mentioned. Refusing the colon is what keeps writing and reading inverse to each other; every other `Name` character is admitted.

A namespace URI is not validated. `Q{...}` carries its text to the emitted declaration unchanged, and an empty URI means "no namespace" and is spelled `Q{}`. Namespaces in XML 1.0 recommends but does not require that a namespace name be an IRI, and a conforming XML parser therefore accepts one that is not; validating on the way out would refuse to write a document this tool can read, and a round trip that fails on a document neither standard rejects is a worse outcome than an unchecked URI.

Input decoding is controlled exclusively by Section 7.4. If an XML declaration contains an encoding name, it must agree with the encoding selected by the BOM or strict UTF-8 default; disagreement is a blocking `PARSE002` error, not an `XML0xx` error. The condition is a decoding failure that an XML declaration happens to reveal, so it is reported once per failing source at line 1, column 1 with the rest of Section 7.4's encoding errors, and it is diagnosed before the document is parsed. An XML declaration that is malformed in any other way is `XML002`.

Processing instructions are discarded with a summarized warning.

### 11.3 Mixed content

Element content is retained as an ordered sequence.

Example:

```xml
<a>text1<b x="1"/>text2</a>
```

is represented internally as:

1. text node `text1`;
2. element `b` with attribute `x="1"`;
3. text node `text2`.

The canonical conceptual projection is:

```text
a.#0=text1
a.#1.b.@x=1
a.#2=text2
```

but that ordinary projection alone is not sufficient to reconstruct node kinds. Same-format XML processing uses the typed model directly.

### 11.4 Canonical XML addressing

Scheme selectors and references use this XML path projection:

- canonical paths contain typed components; an ordinary mapping-name component whose text resembles XML syntax is not identical to an XML element, attribute, or content-token component;
- an unqualified element normally uses its local name; `Q{}local-name` is the explicit canonical spelling of that same component, used when a path must be distinguished from a *format-agnostic alias* in the sense of Sections 13.1 and 15.2;
- a namespace-qualified name is `Q{namespace-uri}local-name`;
- dots inside `Q{...}` are part of the URI and do not split the qualified path;
- the first unescaped `}` closes the URI; a literal `}` inside the URI is written as `\}`;
- an attribute is prefixed with `@`, for example `@x` or `@Q{urn:p}x`;
- element-only children use ordinary element-name paths;
- repeated same-name element children form a sequence and use stable ordering-value parts;
- when an element has mixed content, every content node uses an ordered part `#0`, `#1`, and so on;
- a mixed-content child element is addressed as `#n.element-name`;
- a mixed-content text or CDATA node is addressed as `#n`;
- comments may be selected for ignore and conversion through `#n`, but cannot be value-reference targets.

`Q{...}` is one atomic lexer context. Inside the URI, delimiter, wildcard, reference, and ordinary name-escape recognition is suspended; `\}` encodes a literal closing brace and `\\` encodes a literal backslash. The first unescaped `}` ends the URI. The following local name uses ordinary name escaping.

A `Q{}local` component and an unmarked `local` component are the same component and address the same overlay node; an XML element and a mapping key of that name are one node, which is what makes cross-format overlay possible at all. The marker does not narrow the component — it narrows the *addressing*. An unmarked component resolves through the simple alias index of Sections 13.1 and 15.2, where an attribute `@x` and a content token `#n` may alias to the same simple path and make it ambiguous; a marked component bypasses that index and names one canonical component outright. That is the whole of the distinction the second bullet draws: `${a.x}` is ambiguous when `a` has both an attribute and a child element named `x`, `${a.@x}` selects the attribute, and `${a.Q{}x}` selects the child element. Where no such alias competes, `a.b` and `a.Q{}b` name the same thing and behave identically.

Inside `Q{...}`, a backslash followed by any character other than `}` or backslash is a blocking parse error.

Every XML parent assigns stable content-token ordering values across all child elements, text, CDATA, and comments, including element-only parents. Element-only children retain ordinary element-name addressing while also carrying their content-token ordering value for deterministic placement. A comment in `<a><b/><!--c--><d/></a>` is therefore addressed as `a.#1`.

One content node stands outside that addressing. An element with no child elements and exactly one non-comment text or CDATA node exposes that run as the scalar at the element path rather than as a content node, under the scalarization rule below, so the run is not addressable as `#n` and the scalar the overlay carries at the element path holds no ordering value of its own. The index the run would have occupied is consumed rather than reassigned: in `<a><!--c-->1<!--d--></a>` the comments are `a.#0` and `a.#2`, while `a.#1` matches nothing and a directive written against it emits `WARN009`. Section 19.5 states what the absent ordering value costs — such a comment cannot be placed relative to the value and is written after it — and `KNOWN-LIMITS.md` records that limit. The exception is stated here because the general rule above is otherwise read as promising that the position survives, which for this one shape it does not.

Example:

```xml
<a xmlns:p="urn:p">text<p:b x="1"/><b>two</b></a>
```

projects addressable paths conceptually as:

```text
a.#0
a.#1.Q{urn:p}b.@x
a.#2.b
```

Attribute and child-element names therefore never collide.

Because they never collide, a later contribution that writes an unmarked component where an earlier contribution already placed an XML component of the same simple alias adds a second, ordinary component; it does not override the existing one. Exactly two kinds of XML component can stand in that relation: an attribute, `@x`, and an element in a namespace, `Q{uri}x`, whose simple aliases are both `x`. A no-namespace element is not one of them — `Q{}x` and `x` are the same component by the rule above, so an unmarked contribution meeting one overrides it in the ordinary way and there is nothing to report. Neither is a content token, because Section 13.1 removes that part rather than renaming it, so it aliases to its owning element's path and never competes for a name at this node. That is the typed model working as specified, and it is also the shape Sections 13.1 and 15.2 call ambiguous. Each such component emits `WARN011` naming the canonical component already present, because an override that silently became a sibling is indistinguishable in the merged model from a sibling that was intended. The warning reports and never changes that model: writing the contribution canonically — `@x` for the attribute, `Q{}x` for the element — is what expresses the override. Components arriving together in one contribution never warn, since a single XML document may legitimately carry an attribute and a child element of the same name.

For element-only repeated children:

```xml
<a><b/><b/></a>
```

the canonical child paths are:

```text
a.b.0
a.b.1
```

using the `a.b` sequence path's high-water allocator.

Mixedness and repeated-child classification are properties of the merged common-model element and are evaluated at concrete merge time across all input contributions to that element. Repeated same-name children use `parent.child.<ordering-value>`. If the merged element is mixed, every content node uses its `#n` wrapper even when it originated in an element-only source document.

Stable ordering values are assigned while concrete XML contributions merge, using Section 5.4. A generic scalar payload converted from a non-XML source into XML content receives a fresh implicit content-token ordering value at its source position. Addresses are exposed before wildcard evaluation and never recomputed for an output view. Only final XML serialization densifies document order.

Repeated same-name children form a sequence at `parent.child` and use that child path's own high-water allocator independently of the parent's content-token allocator. Content-token values determine placement in the parent's serialized stream only.

A sequence rendered as repeated sibling elements emits its items in canonical index order. One document's counter is monotone, so a contribution's own tokens already agree with that order and a single-document stream is unaffected. Two contributions each number from their own zero, and an occurrence promoted into a sequence by a repetition in another contribution therefore keeps a value that may fall among or before the items of the other: where content tokens and canonical index order disagree, canonical index order governs, and the item takes the position of the latest item preceding it. Content that is not an item of that sequence keeps the position its own token gives it, and an item that ties with such content follows it. This is the only place the two orders can disagree — Section 17.4 keeps mixed-content child elements from deep-merging across contributions, so converting an element-only contribution to mixed content allocates fresh values and raises no conflict.

The rule is what makes `parent.child.<ordering-value>` mean the same thing in both views. A namespace destination reporting `a.b.0`, `a.b.1`, `a.b.2` and an XML destination emitting those three elements in a different relative order would be two contradictory descriptions of one model, and the address, being the thing a scheme directive and a reference are written against, is the one that has to hold.

Singleton-to-sequence promotion changes the canonical child address. A singleton `<b>` is addressed as `a.b`; after the merged model contains repeated `<b>` children, their canonical paths are `a.b.<ordering-value>` and the former singleton path no longer names a scalar or element. Scheme directives that no longer bind emit `WARN009`; reachable references to the former singleton scalar fail under the ordinary missing- or non-scalar-reference rule. Implementations must not silently retarget `a.b` to the first repeated child.

XML scalarization for references and scalar transformations is:

- an attribute owns its string scalar at its attribute path;
- text and CDATA own string scalars at their content-token paths;
- an element with no child elements and exactly one non-comment text or CDATA node also exposes that scalar at the element path;
- every other element has no scalar payload at its element path.

The element-path scalar and its sole text/CDATA content-token scalar are two canonical addresses for one scalar identity, not two candidates in the simple-alias ambiguity index.

When converting a non-XML mapping to XML, the document element does not come from the selector. Section 14.1 removes the concrete selector prefix unconditionally and for all six formats, so what remains beneath it is the selected view, and XML requires that view to hold exactly one top-level member: that member becomes the document element. A view holding none, or more than one, names no element, and `root` must supply one — which is why a root-level selector, whose view is usually the whole model, almost always specifies `root`. Section 16.3 governs how `root` wraps the view, and Section 19.5 raises `TYPE001` when neither route yields an element.

### 11.5 XML comments

XML comments are retained as ordered comment nodes.

They are not forced into a "leading comment for the next value" representation because a comment may occur between mixed-content nodes or after the final child.

### 11.6 CDATA

CDATA is retained as a distinct XML node kind.

XML output must preserve imported CDATA as CDATA unless an output option requests conversion to ordinary text.

If CDATA content contains `]]>`, the writer must split it into a valid sequence of CDATA and/or text nodes without changing the logical text.

On input, adjacent CDATA segments created solely by safe output splitting are coalesced into one logical CDATA run. Adjacent ordinary text is coalesced separately. CDATA and ordinary text are not coalesced with each other.

### 11.7 Whitespace

The default XML input mode is `PreserveWhitespace`.

`NormalizeFormattingWhitespace` is an explicit opt-in compatibility mode.

In this mode:

- non-whitespace text is preserved;
- whitespace in mixed content is preserved;
- whitespace under `xml:space="preserve"` is preserved;
- whitespace-only text between element children must be discarded as formatting indentation.

The option `PreserveWhitespace` retains every text node.

Because XML has no universal test for insignificant whitespace without a schema or DTD, enabling `NormalizeFormattingWhitespace` weakens the normalized same-format round-trip guarantee and emits one warning per input document when whitespace is discarded.

### 11.8 Unsupported XML features

The following are outside this version's preservation contract:

- DTDs;
- custom entities;
- processing instructions;
- schema type annotations;
- entity-reference boundaries;
- exact namespace declaration placement;
- exact prefix choice when several prefixes identify the same namespace URI;
- exact empty-element spelling;
- exact attribute quote style.

## 12. Wildcard templates

### 12.1 Legacy captures

An unescaped `*` matches zero or more characters within one qualified-name part.

When one name part contains several captures, matching is anchored to the complete part. Captures are assigned left to right, each taking the shortest text that still permits the remaining pattern to match. Implementations must produce this partition independently of regular-expression greediness.

Legacy unnamed captures are substituted positionally.

If a legacy value contains more wildcard substitutions than the name produced, the last capture is repeated for compatibility.

If it contains fewer, unused captures are ignored.

An asterisk in a value is a legacy capture substitution only when the same rule's name defines at least one unnamed `*` capture and value substitution is enabled by the effective `substitute` mode. Explicit `*[identifier]` captures are not compatible with a bare value `*`. In an ordinary entry whose name defines no unnamed captures, `*` is literal text, so values such as `pattern=*.txt` require no escape. `\*` remains the explicit literal spelling in a template value.

Whether a value contains wildcard tokens at all is therefore decided before the value is lexed, from the owning name's captures and the effective `substitute` mode, and the decision covers the bracketed form too. In an entry whose name defines no captures, `*[identifier]` in the value is literal text along with its brackets: `path=/opt/x*[0-9]/y` is a glob, not an undefined capture. Where the name defines explicit captures, a bare `*` in the value is likewise literal text, because the name defines no unnamed capture for it to substitute; that is what incompatibility means here, and it is not a mixed-capture error. Only where recognition is enabled is an unterminated `*[` a `WILDCARD001`.

Exactly one capture form is recognized in any one value, so where the name defines unnamed captures the bare form is the recognized one and it consumes the asterisk alone: in `x*[0-9]y` the asterisk is an unnamed capture substitution and `[0-9]` is literal text. The bracketed form is not recognized there, so its brackets never terminate a token.

A scheme directive's value is decided the same way, from the captures its own pattern defines: its selector for the output-instance-scoped directives, its path for the path-scoped ones, as Section 15.2 separates them. The `substitute` directive does not apply to scheme declarations, so that pattern alone decides. Where it defines captures they are substituted into the directive's value once per binding — per concrete output instance for a selector, per matched path for a path-scoped directive — so one wildcard declaration can give each instance or each match a different value; `jobs.*.root=*` wraps each instance's content in an element named after its own capture, and `reg.*.key=*` names the generated field after each match's. Where it defines none, `*` in the value is literal text: in a scheme whose selector contains no wildcard, `*` in a `filename`, `root`, or `delimiter` value needs no escape. Those three are named as the common cases and not as a closed list; the rule reaches every directive except the two the next paragraph excludes.

A `type` value and an `output` value are excluded from capture substitution. Section 16.6 closes the type names and their legal combinations, and Section 16.1 closes the output formats, so a capture could complete either only by accident of the matched data. Capture recognition is therefore disabled in both values whatever the selector defines, and an unescaped `*` in a `type` or `output` value is literal text. It falls to the ordinary Section 16.1 or Section 16.6 value check, which rejects it as `SCHEME001` in the scheme phase, at the line the declaration was written on. The exclusion belongs to the directive and not to the declaration, so `cfg.*.output=*` and `cfg.output=*` are the same error.

A legacy unnamed capture inside a `${...}` reference is not supported and is `REFERENCE001`. References from templates must use explicit named or numbered captures.

### 12.2 Explicit captures

An explicit capture is:

```text
*[identifier]
```

Example:

```text
a.*[0].b.*[1].val=text1*[1]-repeat-*[1]
```

Rules:

- capture scope is one profile or scheme entry;
- the same identifier reused in the name must match the same text;
- `*[identifier]` in a value substitutes that capture;
- `*[identifier]` may appear inside a reference name;
- an undefined capture outside a reference is an error;
- a capture inside a reference is governed by Section 13.3, which requires it to be bound by the owning template;
- inconsistent repeated captures are nonmatches;
- a single rule must not mix explicit and legacy unnamed captures.

Capture scope, value substitution, an undefined capture outside a reference, and mixed capture styles are properties of the rule as written and are decidable before any item is examined, which is what `WILDCARD001`'s "once per rule" cardinality presupposes. Consistency of a repeated capture is a property of a (rule, item) pair, and the same rule may be consistent against one item and inconsistent against the next; it is therefore a nonmatch and never a diagnostic. Inconsistent repetition is how `*[identifier]` *selects* — writing the identifier twice means "only where these two parts agree" — and reporting it would leave that construct with no use.

An unbound capture *inside* a reference is the one capture condition that is not decidable per rule, which is why Section 13.3 governs it and why Appendix B scopes `WILDCARD001` to a capture "outside a reference". Section 14.4 suppresses a reference error in an owning value unreachable from every concrete output instance, so whether the condition is reported at all depends on which of the rule's generated values are reachable; a once-per-rule diagnostic could not express "these three of five hundred". The two conditions also arise in different phases — a capture the rule does not define is refused while the entry is read, and a reference is only resolved once the Section 14.4 closure is known — so a single code covering both would carry two different phases and two different cardinalities.

Capture text inserted into a generated name is literal text inside one name part. It is never re-lexed as delimiter, wildcard, reference, or escape syntax.

### 12.3 Matching scope

A wildcard matches only within one name part and never crosses a namespace delimiter.

A template name matches existing concrete names through its last wildcard-containing name part. Literal suffix parts are appended to generated names.

Example:

```text
a.x=1
a.y=2
a.*.z=3
```

generates:

```text
a.x.z=3
a.y.z=3
```

The generated descendants are retained alongside the scalar payloads already present at `a.x` and `a.y`. Output shape projection follows Section 4.4.

Mappings expose their keys as name parts.

Sequences expose their stable ordering values as decimal name parts:

```text
a[0] -> a.0
a[1] -> a.1
```

When a generated suffix targets a sequence item, it is deep-merged into that item's overlay node.

Template-bearing JSON or YAML branches are extracted entry-by-entry. Only entries whose qualified path contains a wildcard token are removed from the concrete contribution. Literal siblings remain ordinary concrete data.

### 12.4 Fixed-point evaluation

Wildcard evaluation occurs after all concrete input contributions have been merged and all sequences have been concatenated.

All templates from all sources participate in one deterministic worklist ordered by source order.

Every template must be matched against every eligible concrete or generated entry present in the current fixed-point evaluation, regardless of whether the matched entry originated before or after the template. Source order controls precedence, not visibility.

Concrete step-8 ordering allocations are frozen. A generated contribution reserves or allocates ordering values, in the Section 5.4 sense of a sequence's signed 64-bit ordering value under a high-water mark, only when it is generated, and never retroactively moves a concrete item, even when the rule's source mark is earlier. This governs sequence-item numbering only; the mapping-sibling order of a generated entry follows Section 5.3, which gives it the rule's precedence position. The rule mark still controls conflict precedence.

Evaluation proceeds in deterministic breadth waves. One wildcard iteration is one wave that evaluates eligible `(rule,item)` pairs against the items present at the start of that wave; items generated during the wave become eligible in the next wave.

Each generative `(rule, matched logical item)` pair is applied at most once. Permanent ignore masks are predicates rather than one-shot worklist items and suppress every matching candidate whenever it appears. Evaluation continues until no new match pair or generated contribution exists.

Every generated `(rule,match)` result remains a separate contribution for every merge strategy and is merged at its deterministic rule/match position using the effective input-path strategy of its target: `deep`, `replace`, `append`, or `error`.

Consequently, `merge=error` can intentionally make a wildcard-generated contribution fail when another contribution already exists at its target path.

A wildcard candidate check is counted once for a `(rule,item)` pair for generative templates, permanent wildcard ignore masks, and wildcard scheme selectors when:

1. the item has at least the number of parts required through the rule's last wildcard-containing part;
2. every literal name part before that point equals the corresponding item part;
3. the pair has not previously been considered.

Full capture matching may then succeed or fail without another candidate charge.

For candidate accounting, an item is a distinct logical path node. If the rule's last wildcard-containing part is at depth `k`, eligible items are the distinct depth-`k` prefixes of existing paths, not every deeper descendant.

Eligible items are enumerated in the first-appearance order of those depth-`k` nodes in the model being evaluated, which is the mapping order Section 5.2 preserves. That enumeration order is the *match order* referred to by Sections 5 and 17.5, and it applies identically to generative templates, permanent wildcard ignore masks, and wildcard scheme selectors, so the term names one order everywhere it is used.

Breadth-wave iteration counts apply only to generative templates. Every wildcard rule category consumes the shared candidate-check limit once per eligible pair.

Ordering values are never recomputed during the fixed point. Deleting `a.1` does not cause another item to become logical address `a.1`.

The implementation must:

- detect nonterminating or excessively expanding rule sets;
- enforce configurable generated-entry and iteration limits;
- report the rules responsible for the limit;
- never depend on hash-map iteration order.

### 12.5 Duplicate generated names

Generated entries participate in normal source-order precedence.

If several rules produce the same name, the later rule wins. If one rule produces the same name more than once, the later deterministic match ordinal wins.

## 13. References

### 13.1 Resolution

References resolve after wildcard generation and ordinary data merging.

References may be recursive.

Missing references and cycles are blocking errors with a complete source chain.

References are strictly scalar:

- after canonical or simple-alias resolution chooses one path, a reference resolves only the scalar or null payload stored at that exact canonical path;
- descendants of the referenced path are never copied;
- referencing a path that has descendants but no scalar/null payload is a missing-reference error;
- a reference never materializes new mapping keys, sequence items, XML nodes, output selectors, or wildcard candidates.

This preserves the established scalar-payload-only reference contract and intentionally excludes hierarchical subtree references.

A reference has either canonical or format-agnostic addressing:

- a reference containing an unescaped XML `Q{...}`, `@`, or `#n` typed address component is canonical and resolves one exact canonical path;
- every other reference is format-agnostic and resolves through the simple scalar alias index;
- ordinary JSON, YAML, and namespace paths alias to themselves;
- an XML simple alias replaces every `Q{uri}local` or `@Q{uri}local` part with `local`, replaces every `@local` part with `local`, removes a `#n` wrapper before a child element, and removes a terminal text/CDATA `#n` so that the scalar aliases to its owning element path;
- XML comment content-token paths never enter the simple alias index; comments have no scalar payload and are invisible to format-agnostic reference resolution;
- repeated-element stable ordering values remain ordinary decimal alias parts;
- one matching scalar/null payload resolves successfully;
- no match is a missing-reference error;
- more than one canonical scalar having the same simple alias is a blocking ambiguous-reference error that lists the canonical candidates.

Escaped marker text and marker text inserted from a wildcard capture are ordinary literal name components and never become typed XML components.

For example, an XML attribute and unqualified child element both named `x` make `${a.x}` ambiguous; `${a.@x}` selects the attribute and `${a.Q{}x}` selects the child element.

A canonical reference directly addressing an XML comment path fails as a non-scalar reference.

### 13.2 Type forwarding

If a value consists of exactly one reference and no literal text, it inherits the referenced scalar kind and value.

Example:

```text
port=${database.port}
```

remains numeric when `database.port` is numeric.

If a value contains any concatenation, its result is a string:

```text
endpoint=https://${host}:${port}
```

References copy the typed value produced by input parsing and scalar inference, before output-specific `type` transformations. Scalar inference is not applied to an untyped payload containing an unescaped reference before resolution. After recursive resolution, a single-reference payload adopts the referent's kind transitively; any concatenation becomes a string.

Canonical interpolation text is:

- string: unchanged text;
- null: `null`;
- Boolean: lowercase `true` or `false`;
- integer: base-10 without leading zeros except `0`;
- decimal: exactly the canonical decimal algorithm in Section 18.

An exact reference later matched by `type=string` is rendered as a string without changing the referenced source value.

### 13.3 Non-scalar references

Mapping, sequence, XML element, comment, and other structured-node references are unsupported and are blocking reference errors.

Free wildcard references such as `${a.*}` are blocking errors. A reference inside a wildcard template may contain only explicit captures already bound by that same template. After capture substitution, the resulting reference must contain no wildcard and resolves as one canonical or format-agnostic scalar reference.

A capture the owning template does not bind is a free capture, and this section governs it rather than Section 12.2: the code is `REFERENCE001`, reported once per reachable owning value in the planning phase, and Section 14.4 suppresses it where no concrete output instance reaches that value. Appendix B states the same division by scoping `WILDCARD001` to a capture outside a reference.

### 13.4 Disabled substitution

`substitute=Key` disables reference and wildcard interpretation in values while retaining wildcard interpretation in names.

`substitute=None` disables interpretation in both names and values.

Namespace-profile lexical escapes are decoded regardless of substitution mode because they are part of the profile syntax.

Native JSON, YAML, and XML strings matched by `Key` or `None` are preserved exactly after native format decoding; no transformer escape decoding is applied.

## 14. Output filtering

### 14.1 Concrete output instances

An `output` selector containing no wildcards creates exactly one concrete output instance even when no data path currently matches its literal prefix.

An `output` selector containing wildcards is first expanded into one concrete output instance per concrete selector match.

Example:

```text
a.*.output=json
```

with data under `a.x` and `a.y` creates two output instances, selected by literal prefixes `a.x` and `a.y`.

The default files are `a.x.json` and `a.y.json`.

Expansion stops at the last wildcard-containing selector part. Descendants below the captured part do not create deeper output instances.

For data containing only:

```text
a.x.y=1
```

the declaration:

```text
a.*.output=json
```

creates exactly one `a.x` instance. It never creates `a.x.y`.

There is exactly one instance per unique capture tuple and literalized selector, regardless of how many descendants matched beneath it.

A wildcard `filename` may use the same captures to choose another destination. If several concrete instances resolve to one destination, Section 17.5 governs the collision.

An output instance selects its complete literal-prefix subtree. Nested output declarations intentionally may select overlapping data and create duplicate content in separate files.

The concrete selector prefix is unconditionally removed from the selected output model before `type`, `key`, `root`, rendering, and `filemerge`. `root` then wraps the remaining selected value when configured.

A concrete output instance created by a literal declaration or wildcard expansion remains a planned output even when its selected view contains no surviving payload, explicit container presence, descendants, or comments:

- JSON and YAML emit an empty mapping unless `root` wraps that mapping;
- namespace, quoted namespace, and INI emit their normalized empty text file, which under Section 24 is zero bytes, because the Section 24 termination rule applies only to output that has content;
- XML requires `root` to provide a document element and otherwise raises `TYPE001`.

A wildcard output declaration that produces no concrete selector instance emits `WARN009` and creates no file. Explicit empty mapping or sequence presence is not a zero-entry selection.

A concrete output instance whose selected view contains nothing also emits `WARN009`, and still produces its file as described above. The instance is a planned output either way, so this is a warning rather than an error and the exit code is unaffected; what it removes is the silence. A literal selector that matches no data and a wildcard selector that matches no data are the same authoring mistake, and Section 14.1 otherwise reports only the second: the first produces a well-formed, deployable, empty document and no diagnostic, so a mistyped selector is indistinguishable from a deliberately empty one at every later stage. An intentionally empty output is still expressible and still exits `0`; it now says so in the stream. Explicit empty mapping or sequence presence is content for this purpose, exactly as it is for the wildcard rule above, so a deliberately declared empty container does not warn.

If the empty root selector selects a bare scalar, JSON and YAML may emit a scalar document. XML, namespace, quoted namespace, and INI require an explicit `root`; otherwise rendering is a blocking type error because no key or element identity exists.

The rule above is about the selected view, and it turns on the selector being empty. A non-empty selector whose view is a bare scalar does *not* behave identically, because it leaves a concrete name behind. JSON and YAML emit a scalar document, as before. XML still requires an explicit `root`, because an element identity cannot be invented. Namespace, quoted namespace, and INI retain the final concrete selector part as the emitted key under Sections 19.1, 19.2, and 19.6, and `root` then prefixes that key rather than replacing it under Section 16.3.

This is not a format keeping a prefix that Section 14.1 removed. The removal above is unconditional and applies to all six formats. What the three flat formats then do is *supply* a key for a value that would otherwise have none, and the name they supply is the one the author last wrote. They can do this and the other three cannot because a flat format's output is a set of qualified names, so a key it supplies is ordinary content — Section 16.3 says so where it explains that `root` prefixes such a key rather than replacing it. So `cfg=5` published as JSON under the `cfg` selector is the document `5`, not `{"cfg": 5}`, while the same selection published as INI is the global key `cfg=5` and as namespace is the entry `cfg=5`.

### 14.2 Strict prefix semantics

An output selector pattern `P` selects a concrete name `E` only when:

1. `E` has at least as many parts as `P`;
2. every part of `P` matches the corresponding leading part of `E`.

Examples:

- `a.b` selects `a.b` and `a.b.c`;
- `a.b.c` does not select `a.b`;
- `a.*` selects `a.x` and `a.x.y`;
- the empty root selector selects everything.

### 14.3 Templates

A template entry is retained when it can produce at least one concrete name under a selected prefix.

### 14.4 Reference closure

After concrete output instances are known, direct output filtering is applied to the already generated name graph. All entries reached transitively through references from selected entries are retained for evaluation.

Referenced support entries outside the selected subtree are not emitted unless independently selected.

Missing, cyclic, ambiguous, free-wildcard, and non-scalar references in entries unreachable from every concrete output instance do not fail the run.

Selected entries and their transitive reference closure are resolved strictly.

A selector whose winning declaration is `output=ignore` plans no output instance and no reference-reachability root. References reachable only from that suppressed selector are therefore not resolved and cannot fail the run.

"Plans no output instance" is about the output *plan*, and Section 15.2 uses the word `exists` in the other sense: the instance remains a configuration binding target, because Section 16.1 keeps it so that a later declaration can restore it. A directive naming a suppressed instance has therefore bound and does not emit `WARN009`, while the instance still contributes no file, no destination, and no reachability root here. Both are true of the same instance, and a reader who takes either sentence for the whole answer will get the diagnostic stream wrong in one direction or the other.

## 15. Scheme language

Scheme files may use the case-insensitive `.json`, `.yaml`, and `.yml` extensions that Section 7.1 gives input files, and every other extension, including none at all, uses namespace-profile parsing. Their parsed content must project to qualified directive paths and scalar directive values.

A directive value that is a sequence is `SCHEME001`, not a set of indexed directives. Section 5.4's rule that a sequence exposes its ordering values as decimal name parts governs *matching* — wildcards, ignore, references, and scheme selectors — and so applies to the path side of a scheme entry. It does not apply to the value side, where Section 15 requires a nonempty scalar. Reading `cfg.output: [json, yaml]` as `cfg.output.0` and `cfg.output.1` would report two unknown-directive errors naming paths the author never wrote, and would quietly make a JSON scheme file mean something different from the namespace file `cfg.output=json,yaml` that expresses the same intent. The comma-separated scalar is the spelling for a multi-valued directive in every format.

Namespace-profile scheme files are the canonical and recommended representation.

JSON and YAML scheme files use secure default parsing because input-option directives cannot affect the parsing of the scheme file that defines them.

The `.xml` extension is excluded, and a scheme file named with it is `PARSE001` against this section, reported once per failing source in the scheme phase before the file is read. XML is an input format under Section 7.1, and this section defines no projection from an XML document to a qualified directive path. Three questions have no answer here and none of them has an obviously correct one: an element and an attribute are distinct component kinds under Section 11.4 and only one of them can be an ordinary name part, the single root element occupies a position that no JSON or YAML top-level key does, and Section 12.2 spells a capture `*`, which is not a legal XML name, so a selector containing a wildcard cannot be written at all. Reading the file as a namespace profile instead would report Section 8.1 against a document its author never wrote to that contract, naming the wrong rule; accepting it silently would discharge none of the directives it appears to carry. A scheme extension this section does not define is therefore rejected by naming this section. Because the check reads the name rather than the file, it precedes Section 7.2's existence test: a `.xml` scheme path that does not exist is this error and not that section's warning-and-ignore.

The final qualified-name part identifies a directive.

Directive names are matched under ASCII case-insensitive comparison, as is every other name and value in the scheme language: formats in Section 16.1, type names in Section 16.6, substitute modes in Section 16.7, output-option flags in Section 16.9, and merge strategies in Sections 16.10 and 16.11. The Section 15.3 deprecated aliases are matched the same way. Comparison is ASCII-only, so no directive name changes meaning with the host locale.

Recognized compatibility directives are:

- `output`;
- `filename`;
- `root`;
- `delimiter`;
- `namespacedelimiter`;
- `key`;
- `type`;
- `substitute`;
- `xmloptions`;
- `xmlinputoptions`;
- `xmloutputoptions`;
- `jsoninputoptions`;
- `jsonoutputoptions`;
- `yamlinputoptions`;
- `yamloutputoptions`;
- `inioutputoptions`;
- `merge`;
- `filemerge`.

Unknown directives are blocking errors.

A namespace-profile scheme file contains only entries and comments. A Section 8.6 mask record projects to neither a qualified directive path nor a scalar directive value, so it is `SCHEME001` rather than a run-wide exclusion: a mask applies to input data, and a scheme file supplies none.

Every recognized directive requires a nonempty scalar value after format parsing. An empty value, null, container value, unknown directive value, or illegal option/type combination is `SCHEME001`.

### 15.1 Scheme loading phases

The normative processing pipeline is:

1. Parse scheme syntax using secure format defaults and resolve references among scheme entries. Scheme references cannot target input data. A scheme reference resolves against the directive entries of the scheme set and nothing else: its target is another scheme entry's value, addressed by that entry's qualified directive path, so `cfg.filename=${cfg.output}` is legal and yields that directive's resolved text. A reference naming a path that is not a scheme entry is `REFERENCE002` in the `scheme` phase, reported as an unrecognized directive at the named path rather than as input data that happens to be absent, because at step 1 no input has been read and the two cannot be told apart. A reference chain that returns to its own start is `REFERENCE003` in the `scheme` phase, anchored at Section 13.1, and names the whole cycle so that no directive in it is reported as merely unresolvable. These two are reported per owning directive rather than under the Section 13 "once per reachable owning value" rule, because reachability is a Section 14.4 property computed at step 13 and does not yet exist here.
2. Compile root-level input options.
3. Compile `substitute` path patterns. These patterns may contain name wildcards but no references.
4. Compile literal-path input `merge` directives. Input `merge` paths must not contain wildcards or references. `filemerge` is not consulted during input processing.
5. Parse input documents into typed overlays without applying namespace escape decoding to native structured strings.
6. Lex and validate reference and value-wildcard syntax in strings according to the precompiled `substitute` patterns. A `substitute` pattern matches an entry's declared pre-expansion path; a generated entry inherits the mode resolved for its template. Reference targets are not resolved until step 15.
7. Extract wildcard template entries from concrete JSON, YAML, and namespace contributions, and permanent namespace `!` exclusion masks from namespace contributions. XML input cannot define a wildcard template because XML names are parsed as XML names rather than namespace wildcard syntax.
8. Fold entries within each source contribution, then merge source contributions one at a time in source order using literal-path merge rules. Allocate native-sequence ordering values and reserve every in-range canonically numeric mapping child at its source position for high-water accounting. A nonempty all-in-range-canonical-numeric mapping is sequence-eligible at this phase for `append`, `replace`, and `error`: when a strictly earlier surviving sequence-eligible contribution exists, `append` consumes and rebases the later mapping as a sequence contribution and leaves no mapping projection for later inference; the earliest or sole contribution retains its supplied ordering values.
9. Expose native-sequence ordering values and ordinary numeric mapping keys as path parts.
10. Evaluate templates under the permanent exclusion masks to a deterministic fixed point.
11. Process remaining inferable mappings in source order and project each as an explicit indexed sequence. Inference replaces that contribution's mapping projection and does not create a second contribution for `merge=error` accounting. Under `deep`, explicit values patch; under `append`, an inferred contribution is rebased only when a strictly earlier surviving sequence-eligible contribution exists at the path; under `replace`, the later source contribution has already replaced the earlier visible value without lowering high-water; under `error`, step 8 has already rejected a distinct second source contribution. A later projection never causes an earlier contribution to rebase. Expose the final stable ordering-value graph without reallocating earlier native items.
12. Infer locale-independent scalar kinds for remaining untyped namespace payloads that do not contain unresolved references.
13. Expand wildcard scheme selectors and output declarations against the resulting concrete name graph.
14. Build concrete output instances and apply strict prefix filtering.
15. Compute each output instance's transitive reference closure and resolve references within selected closures.
16. Apply path-scoped transformations to each selected output view in this order: `type=ignore`, explicit scalar/XML types, `type=array` or `type=mapping`, `multiline`, `key`, then `root`.
17. Group fully transformed output contributions by canonical destination path.
18. Fold same-format destination collisions using `filemerge`, and resolve cross-format overrides.
19. Serialize all planned destinations into immutable in-memory byte buffers.
20. Publish destinations directly in deterministic order.

Unless explicitly stated otherwise, scheme paths address the stable pre-transformation paths produced at step 11. `substitute` and literal input `merge` are the explicit earlier-phase exceptions. A transformation does not cause scheme matching to restart against newly created paths.

Within step 16 that addressing holds across the passes: every pass applies its directives at the step-11 addresses, not at the addresses an earlier pass in the same step has left behind. A pass that reshapes a node re-addresses the surviving descendants of that node — `type=array` discards mapping keys in favor of ordering values, `type=mapping` names sequence items by their ordering values, and `key` places each mapping child in a record — and a directive bound beneath such a node is re-addressed together with the value it bound to, exactly as Section 17.5 re-addresses the per-path high-water map. The re-addressing reaches every later reader of a step-16 directive, including the explicit scalar and XML node kinds of Section 16.6, which select a rendering at serialization rather than reshaping the view.

This is not a restart of scheme matching. No directive acquires a path that step 16 created: a directive spelling an address that exists only after a reshaping matches nothing at step 11, binds nowhere, and emits the Section 15.2 warning. The one reshaping whose descendants are not re-addressed is `type=ignore`, which removes them rather than moving them, and Section 16.6 states that case: a directive matching only a descendant of an ignored path is inert and emits the same warning.

At step 9, a mapping child whose name is an in-range canonical ordering value and the sequence item with that value at the same path are one structural overlay node for merging, comments, references, selectors, generation, and wildcard candidacy, whether or not step-11 inference ultimately applies. When both are present, step 9 forms that node by merging them in source order under the effective input `merge` strategy at their shared path, because they are one literal path that step 8 could not yet recognize as one; under the default `deep` the later contribution therefore patches the earlier item at its ordering value, as Section 8.7 requires of an explicit indexed contribution. The combined item keeps the ordering provenance the sequence item already had, because the value was acquired when that item was placed and step 9 supplies no new value. Its matching position is the latest surviving contribution mark at that path. A `(rule, logical path)` pair therefore generates at most once.

This phase order is acyclic. Data-dependent references or wildcards are prohibited in directives required before the phase that supplies their matching data.

Within one step-16 pass, where directives of the same kind bind at a node and at one of its descendants, the descendant is applied first: the pass proceeds deepest-first. Every directive therefore sees the subtree its own address named at step 11, and no directive is handed a shape another directive built in the same pass. The alternative strands work silently — a `key` at a node turns its children into sequence items, so an outermost-first pass would delete the very path a descendant directive bound to, and an author who wrote both would get neither the descendant's reshaping nor a warning, because the directive did match something at step 11. Deepest-first composes instead: `a.type=array` together with `a.x.type=array` converts `x` and then converts `a`, which is what writing both plainly asks for. Order between directives of the same kind at the *same* path remains the Section 16.5 tie-break, and order between kinds remains the pass order above.

### 15.2 Directive precedence

All scheme directives follow source order only.

A later matching directive overrides an earlier matching directive for the same effective setting.

Pattern specificity does not alter precedence.

Scheme paths use the same typed component model as canonical data paths. An explicitly marked `Q{}`, `@`, or `#n` component selects only that XML component. An unmarked component uses the simple alias index for compatibility and convenience; if ordinary and XML components make that alias ambiguous at a matched location, selector expansion at pipeline step 13 emits blocking `SCHEME002` in the transformation/output-planning diagnostic phase and lists the canonical alternatives. Escaped marker text selects an ordinary literal component.

Scheme configuration is output-instance-scoped:

- `output`, `filename`, `root`, `delimiter`, output options, and `filemerge` bind to the concrete output selector instance produced from their own selector;
- exact and wildcard declarations that literalize to the same concrete selector participate in one source-ordered override stream;
- a directive for selector `a` does not implicitly configure an independently created `a.x` output instance;
- `type`, `key`, and output-view ignores are evaluated independently against absolute stable pre-transformation paths in every output instance containing the path;
- `output=ignore` suppresses only its concrete output instance and never removes data from another output instance.

A selector-qualified `filename`, `root`, `delimiter`, output-options, `filemerge`, or output-view transformation that binds to no concrete output instance emits one scheme warning and is otherwise inert.

The test is existence, not effect. An output instance suppressed by `output=ignore` still exists — Section 16.1 keeps it precisely so that a later declaration can restore it — so a directive naming that instance has bound, and does not warn. Configuring an output fully and then disabling it with one line is the workflow `output=ignore` exists to serve, and a warning on every directive of a disabled output would make that line noisy in proportion to how completely the output was configured. The instance is also one declaration away from being live, so the warning would be describing a configuration that is about to be correct.

A directive stranded *beneath* an ignore is a different case and does warn. `type=ignore` at `cfg.a` removes `cfg.a` and its descendants from the view, so `cfg.a.p.type=array` names a path that no longer exists and emits `WARN009`. The distinction is what the directive configures: `a.filename` and `a.output=ignore` configure one output instance between them and are read together, whereas `cfg.a.p.type` and `cfg.a.type=ignore` are separate declarations about separate paths, the second of which silently voided the first. A directive warns when the thing it configures is absent, not when that thing is present and producing nothing.

Use `type=ignore` for output-view removal or a namespace `!pattern` permanent mask for run-wide exclusion.

Ignore mechanisms are intentionally distinct:

| Mechanism | Scope | Later restoration | Removes content from other outputs | Typical use |
|---|---|---|---:|---|
| namespace `!pattern` | entire invocation and every output | never | yes | permanently exclude sensitive or obsolete input paths |
| `type=ignore` | one output instance's selected view | later non-ignore `type` at the same path | no | omit a section from one rendered view |
| `output=ignore` | one concrete output instance | later non-ignore `output` declaration | no | suppress a wildcard-generated or otherwise unwanted file |

### 15.3 Deprecated aliases

The following aliases remain accepted with one warning per scheme:

- `namespacedelimiter` for `delimiter`;
- `keyOnly` for substitute mode `Key`;
- `xmloptions` for `xmloutputoptions`;
- legacy type values `xmlns` and `xmlnssuffix`, treated as no-ops.

### 15.4 Blocking-error recovery

Blocking diagnostics are collected within the pipeline phase in which they are detected, subject to the registry cardinalities in Section 22. A phase completes every independent check that does not depend on a failed result, buffers its deterministic diagnostic set, and then aborts before the next phase when any blocking diagnostic exists.

A source that fails parsing, decoding, or a per-source limit contributes no partial overlay. Other independent sources in the same phase may still be parsed so their source-scoped diagnostics can be collected. A failed scheme contributes no partial directives. Transformation and planning errors never produce a partial output instance for a later phase.

Publication is the exception because external side effects have begun: `PATH002` stops publication immediately as specified in Section 21.3.

## 16. Scheme directives

### 16.1 `output`

```text
[selector.]output=format[,format...]
```

Formats:

- `namespace`;
- `quotednamespace`;
- `json`;
- `yaml`;
- `xml`;
- `ini`;
- `ignore`.

Names are case-insensitive. Whitespace around comma-separated values is ignored.

Formats in one comma-separated declaration have a left-to-right declaration ordinal. A later format in the same list has later file-collision precedence than an earlier format.

A later `output` declaration replaces the complete earlier output-format set for the matched concrete selector.

`ignore` is a negative output declaration. When it is the winning declaration, no output is produced for that concrete selector.

`ignore` must appear alone in its declaration. It participates in ordinary source-order override: a later non-ignore declaration can restore output, and a later `ignore` declaration can suppress it again.

### 16.2 `filename`

```text
[selector.]filename=relative-path
```

The path must be relative to the configured output root.

Absolute paths and paths resolving outside the output root are errors.

An explicit `filename` value is the complete relative destination path and is used verbatim after the portable path processing below. A format extension is never appended to it. The default extensions in this section apply only when no effective `filename` directive exists.

Literal `/` or `\` separators in `filename` intentionally create subdirectories. Wildcard captures may be substituted into path segments. Scheme references are resolved before capture substitution, but their resulting text is opaque segment data: `/` or `\` supplied by a reference is encoded and never creates a directory. The `substitute` directive applies only to input/common-model values and does not transform scheme declarations.

Every substituted capture and every selector-derived default filename part uses this ordered portable segment algorithm exactly once:

1. split the scheme-written path only at literally written `/` and `\`;
2. substitute captures and selector-derived parts as decoded opaque text inside the segment;
3. reject an empty assembled segment;
4. record whether the decoded segment equals `.` or `..`, or whether its portion before the first dot case-insensitively equals one of `CON`, `PRN`, `AUX`, `NUL`, `COM1` through `COM9`, or `LPT1` through `LPT9`;
5. retain ASCII letters, digits, `-`, `_`, and `.`, and encode every other UTF-8 byte—including `%`—as `%HH` using uppercase hexadecimal;
6. percent-encode every trailing dot as `%2E` and every trailing space as `%20`;
7. prefix the result with `%5F` when step 4 recorded a dot-segment or reserved-device condition.

Generated `%HH` tokens are atomic and are never encoded again.

The reserved-name list is ASCII-only; `COM0`, superscript-digit variants, `CONIN$`, and `CONOUT$` are not included.

The complete portable segment algorithm, including Windows reserved-device handling, is applied identically on every operating system so identical inputs produce identical relative paths.

Only separators written literally in the scheme create directory hierarchy; separators originating inside captured data are encoded.

Statically written `.` and `..` segments are prohibited. The composed-segment safety rules apply to every output segment after substitution, including wholly literal segments. Reserved device names are deterministically renamed with the prefix rather than rejected. Captured data cannot create traversal because it is encoded.

Default file names:

| Format | Non-root selector | Root selector |
|---|---|---|
| namespace | `<selector>.properties` | `output.properties` |
| quotednamespace | `<selector>.sh` | `output.sh` |
| JSON | `<selector>.json` | `output.json` |
| YAML | `<selector>.yaml` | `output.yaml` |
| XML | `<selector>.xml` | `output.xml` |
| INI | `<selector>.ini` | `output.ini` |

`<selector>` means the dot-joined concrete selector after encoding each selector part with the portable rules and additionally encoding literal `.` as `%2E` inside a part. It is always one filename segment, and different selector-part sequences cannot collapse merely because a part contained a dot.

### 16.3 `root`

```text
[selector.]root=name[.nested-name...]
```

The root path wraps the selected content uniformly.

The concrete output selector prefix is removed first. `root` then wraps the selector's remaining content. The original selector name is not retained unless it is also present in the `root` value.

For `root=x.y`:

- namespace output prefixes keys with `x.y`;
- JSON emits `{"x":{"y":...}}`;
- YAML emits `x: { y: ... }` in normalized YAML form;
- XML emits `<x><y>...</y></x>`;
- INI prefixes the section/key path with `x` and `y`.

`root` wraps; it never renames. The sentence above is about the *selector prefix*, which names a position in the model rather than content: once it is removed, `root` prefixes whatever content keys remain, and every case in the list above is a wrapping. Where a flat format supplies a key for a bare scalar under Sections 19.1, 19.2, and 19.6, that key is content by the time `root` applies, so `root` prefixes it as well rather than replacing it. With selector `k`, a bare scalar payload, and `root=x.y`, namespace emits `x.y.k`, quoted namespace emits the same parts joined by its delimiter, and INI emits key `k` inside section `[x:y]`.

`\.` represents a literal dot inside one root name part.

Wildcard captures may be substituted into root name parts, per output instance, under the Section 12.1 rule that governs every scheme directive value. `jobs.*.root=*` therefore wraps each concrete instance's content in an element named after that instance's own capture. A capture matches within one qualified-name part, so substituted text cannot introduce a further nesting level.

### 16.4 `delimiter`

```text
[selector.]delimiter=string
```

Default:

- namespace: `.`;
- quoted namespace: `_`;
- INI nested section path: `:`.

When explicitly supplied, the delimiter consistently joins path parts for namespace, quoted-namespace, and INI output. Only plain namespace output uses the `\u{HEX}` delimiter-disambiguation escape below. Quoted-namespace and INI output instead apply their own validation and escaping after joining.

An empty delimiter is invalid.

For namespace output, a delimiter must not contain `=`, backslash, or any scalar in the Section 19.1 forbidden set, and must not consist solely of scalars drawn from `u`, `{`, `}`, and the hexadecimal digits `0` through `9` and `A` through `F`. The first restriction covers NUL, CR, LF, tab, and every other `Cc`, `Cf`, or `Cs` scalar, along with U+0085, U+2028, and U+2029: a delimiter is emitted literally between parts, so admitting a scalar that Section 19.1 escapes *inside* a part would break the same one-line guarantee the escape protects. The second restriction excludes `E`, `2E`, and `u{`, each of which occurs inside a `\u{HEX}` escape this section can emit; a consumer splitting the joined path on such a delimiter would split inside an escape, which is the ambiguity the escape exists to prevent. A delimiter violating either restriction is `SCHEME001`.

Scan each decoded ordinary name part and the local-name portion of a typed component once from left to right. When an occurrence of the delimiter begins at the current scalar, encode that first scalar with `\u{HEX}` and continue at the next input scalar. Overlapping occurrences are therefore each escaped: with delimiter `::`, the part `a:::b` emits `a\u{3A}\u{3A}:b`. Escaping only nonoverlapping occurrences would leave an unescaped delimiter in the output and break injectivity. Emitted escape text is atomic and is never rescanned.

A `\u{HEX}` escape is emitted with no leading zeros and upper-case hexadecimal digits, so U+002E is `\u{2E}` and U+000A is `\u{A}`. Appendix A.2 accepts other spellings on input; output uses only this one, because byte-identical output requires exactly one.

Inside `Q{...}`, only closing brace and backslash use the XML canonical escapes from Section 11.4. Delimiter occurrences and all other URI characters are emitted literally.

Namespace input syntax always uses `.` as its qualified-name delimiter in this version. Namespace output using another delimiter is a consumer-oriented projection and is outside the normalized same-format round-trip guarantee.

Delimiter occurrences always use `\u{HEX}` rather than an input-specific short escape because the delimiter is configurable. One output rule therefore remains unambiguous for every delimiter choice.

Before publication, every flat output must detect collisions after root application, delimiter joining, segment escaping, and identifier normalization. Two distinct logical paths must never silently become one namespace, shell, or INI key. A collision is blocking `FLAT001`.

Namespace, quoted-namespace, and INI output are destinations requiring one container shape, so a node holding both a mapping and a sequence projection emits only the later container contribution and warns, under Section 17.1. Both projections supply name parts rather than a distinguishing syntax, so emitting both would give two keys to the single node Section 15.1 step 9 shares between a numeric mapping child and the sequence item at its ordering value, and would then report one logical path as a `FLAT001` collision between distinct ones.

Here, identifier normalization means only shell-identifier validation under Section 19.2 and INI name validation under Section 19.6. It performs no case folding or character replacement.

### 16.5 `key`

```text
[path.]key=field-name
```

`key` is an output-neutral transformation from an ordered mapping to a sequence of records.

Input:

```text
a.b.x=1
a.c.x=2
```

Scheme:

```text
a.key=name
```

Logical transformed value:

```text
[
  { name: "b", x: 1 },
  { name: "c", x: 2 }
]
```

Namespace projection:

```text
a.0.name=b
a.0.x=1
a.1.name=c
a.1.x=2
```

INI projection:

```ini
[a:0]
name=b
x=1
[a:1]
name=c
x=2
```

Wildcard-qualified `key` directives are supported.

The captures a wildcard-qualified `key` binds are substituted into its field-name value, per matched path, under the Section 12.1 rule that governs every scheme directive value. `reg.*.key=*` therefore names each generated field after the capture of the path it was matched against, so one declaration can name the field differently in each record set it produces. Section 12.1's rule is stated for the captures a directive's own pattern defines; for `key` that pattern is the path of Section 15.2's path-scoped family, not an output selector, and the field name is resolved once per match rather than once per output instance.

If several directives match, the later directive wins. A later directive may therefore intentionally replace the key-field name chosen by an earlier wildcard rule.

The target must be an ordered mapping.

`key` operates on the mapping projection of an overlay and leaves any independent scalar payload available to formats that can represent both. If no mapping projection exists, it is a blocking type error.

The transformed mapping becomes a sequence contribution carrying the mapping projection's source mark. A child already carrying ordering-value provenance retains that value and provenance through record construction. A child without ordering provenance receives a fresh implicit ordering value in mapping order. If an independent sequence projection already exists at the same node, the transformed contribution combines with it as the later contribution under the Section 17.1 sequence rules: a record that received a fresh implicit ordering value concatenates above the node's Section 5.4 high-water mark, and a record that retained an explicit ordering value addresses the item already at that value. A `merge` directive does not configure this fold, because Section 16.10 confines `merge` to pipeline steps 8 through 11 and this is step 16. The rendered order is therefore the Section 5.4 ascending order of the combined set, which may place an item the node already held between two generated records.

For each mapping entry:

- a child with a mapping projection becomes a record containing those mapping fields;
- the generated key field is inserted first as a string scalar containing the decoded mapping-key text; scalar inference is never applied to this generated field;
- comments bound to the original child path move with the complete generated record, while the generated key field itself receives no separate comment;
- if the child already contains that field name, processing is blocking `TYPE001`;
- every independent scalar, null, or sequence projection of that child is placed under a field named `value`;
- a child without a mapping projection becomes a record containing the generated key field followed by `value` holding the complete child overlay;
- if `value` would collide, processing is blocking `TYPE001`.

Applying `key` to a sequence-only or scalar-only target is `TYPE001`.

An effective `type=array` and `key` at the same path is therefore an illegal option combination and raises `SCHEME001`; implementations must not silently choose an order or reinterpret the resulting sequence as a mapping.

### 16.6 `type`

```text
[path.]type=type[,type...]
```

Recognized values:

- `default`;
- `ignore`;
- `array`;
- `mapping`;
- `string`;
- `element`;
- `attribute`;
- `text`;
- `cdata`;
- `multiline`.

Names are case-insensitive and surrounding whitespace is ignored.

`default` clears all earlier effective `type` values at the matched path and restores format-default projection.

The later complete `type` directive replaces the earlier complete type set; values from separate matching directives do not accumulate.

Legal multi-value combinations are:

- `multiline,string`;
- `multiline,text`;
- `multiline,cdata`;
- `array,multiline`;
- `array,multiline,string`;
- `array,multiline,text`;
- `array,multiline,cdata`;
- `string,attribute`;
- `string,element`;
- `string,text`;
- `string,cdata`.

`ignore`, `default`, and `mapping` must appear alone. `array` may appear only alone or in the listed multiline combinations. `attribute`, `element`, `text`, and `cdata` are mutually exclusive. Every other combination is a blocking scheme error.

#### `ignore`

Removes the complete matched overlay subtree, including payload, all container projections, descendants, and comments, from the selected output instance only. It does not mutate the common model. A later complete `type=default` directive for the same effective path can restore default projection.

Applying `type=ignore` to the concrete selector root is a blocking type error because it would leave an output instance without a root value. Use `output=ignore` to suppress that instance.

An effective ignore at path `P` removes every descendant regardless of directives matching those descendants. Only a later complete non-ignore type set matching `P` itself restores `P`. A directive matching only a descendant of an ignored path is inert and emits the unbound-directive warning from Section 15.2.

#### `array`

Forces the matching value to be treated as a sequence.

`array` operates on the winning container projection under Section 4.4:

- if the winning container is already a sequence, retain it;
- if the winning container is a mapping, convert that mapping;
- an independent scalar payload remains available to formats that can represent both;
- if no container projection exists, it is a blocking type error.

Only the winning container projection is converted. A losing mapping or sequence projection remains omitted under Section 4.4 and produces the ordinary shape-conflict warning; it is never merged into the converted result.

This omission uses diagnostic `TYPE002`; no additional array-specific warning is emitted.

Mapping children are converted to sequence items according to the following ordering rule.

More precisely:

- if every surviving key is an in-range canonical ordering value, items are ordered numerically and retain explicit ordering provenance;
- otherwise every key is discarded and every child—including in-range numeric and out-of-range decimal children—receives a fresh implicit ordering value above the node's current high-water mark in current mapping order;
- duplicate canonical numeric indices have already been resolved by source-order precedence.

#### `mapping`

Forces the winning container projection to render as an ordered mapping.

- an existing mapping projection is retained;
- a sequence projection becomes a mapping whose keys are the stable ordering values rendered as canonical decimal strings;
- gaps and nonzero bases are preserved as mapping keys;
- an independent scalar payload remains available to formats that can represent both;
- if no container projection exists, it is a blocking type error.

This directive is the explicit escape hatch for preserving numeric keys as mapping keys rather than projecting them as an array.

#### `string`

Forces scalar rendering as a string in the selected output view. It does not change input scalar inference or the typed value forwarded through references.

It applies to the node's scalar payload. At a node that supplies none — one whose only projection is a mapping or a sequence — it has no effect, in every output format including XML, and it does not distribute over the node's descendants. Section 15.2 evaluates `type` against absolute paths, so a directive at `db` governs `db` and not `db.port`; distributing would make `type` the only directive whose effect escapes the path it names.

Having no effect is deliberate rather than an omitted error case. A wildcard form such as `a.*.type=string` is the ordinary way to force string rendering across a group, and such a group will ordinarily contain interior nodes, so making those an error would leave the form unusable. Nor is this the unbound directive of Section 15.2: that warning reports a directive that reached no path at all, and this one reached its path and found nothing there to render.

#### `element`

For XML output, renders a scalar as an element.

#### `attribute`

For XML output, renders a scalar as an attribute. It is invalid where no containing element exists.

#### `text`

For XML output, renders a scalar as ordinary text content.

#### `cdata`

For XML output, renders a scalar as CDATA.

#### `multiline`

Combines the matching sequence of scalar lines using logical LF.

- a lone scalar is a one-line value and is unchanged;
- an empty sequence becomes the empty string;
- a nonempty sequence must contain only scalar or null payloads; null contributes an empty line;
- a mapping with no sequence projection, a node with no scalar or sequence projection, or a sequence item having only container shape is `TYPE001`.

Comments bound to the joined items move to the resulting scalar, as Section 4.5 requires of a collapse. Comments already bound to the node itself keep their placement, and the items' keep theirs, so an inline comment on the last joined item is the one Section 4.5 leaves inline on the result.

Where the node supplies both a sequence projection and a scalar payload, the sequence is the operand. This directive exists to join a sequence, so a scalar contribution at the same path does not disable it, and the Section 4.4 payload contest does not choose between the two here. The scalar payload is then omitted from this output instance and reports `TYPE002` once for that path and destination, carrying both `path` and `destination`. Section 15.1 resolves destinations at step 17, after this pass, but the stream is emitted once at the end of the run, so the destination of the instance is known by the time the record is written. Where two instances fold into one file, Section 24 governs: the two occurrences agree on phase, ordering key, destination, code and path, and are therefore one record. The destination is required rather than optional, because Section 24 orders a per-output-instance diagnostic in group 2 by its destination, and because a record carrying no destination would tell a consumer neither which file lost a contribution nor, where several did, how many. The `path` member is the projected output key of Section 22 rather than the overlay node, for the same reason: the scalar is still present in the merged model and is missing only from this projection.

Without the directive the same two contributions resolve the other way and the scalar wins. That reversal is the whole effect of asking for a multiline value, and it is why the omission must be reported: the contribution that loses is one the author wrote, and it is discarded by a directive that names neither it nor the shape contest.

Wildcard capture substitution occurs before scalar inference. Consequently, generated text such as an unquoted captured `1` may be inferred as numeric at pipeline step 12. A value still containing a reference defers inference until reference resolution under Section 13.2. `type=string` may force string rendering in an output view.

Rendering:

- JSON emits a JSON string whose line breaks serialize as `\n`;
- YAML emits a literal block scalar;
- XML emits ordinary text unless `cdata` is also selected;
- namespace emits a value using its configured escape rules;
- quoted namespace emits one shell-quoted value;
- INI uses the configured multiline strategy or reports an unsupported-value error.

#### A type that names a format

`element`, `attribute`, `text`, and `cdata` name XML node kinds. In an output instance whose format is not XML they have no effect: no diagnostic, no change to the emitted bytes, and no effect on whether the directive bound. Their XML cases, including the error cases, are unchanged and are stated in Section 19.5.

This is the ordinary case rather than a mistake. `output=xml,json` at one selector renders one subtree twice, Section 15.2 evaluates `type` independently in each output instance, and a scheme naming an XML rendering below such a selector is correct as written. Reporting it would put a diagnostic in the stream of every correct multi-format scheme, which is the shape this tool exists to serve.

It is specifically not `TYPE001`. Section 21.2 gates publication on the whole run, so an error raised in the JSON instance would suppress the XML file the directive was written for — the one output where the directive is meaningful and correct. It is also not `WARN003`, which Section 3.3 gives to a concept discarded from a *source*; nothing was discarded here, because the directive selected a rendering for a format that is not being rendered.

`string` names no format and is governed by its own clause above.

### 16.7 `substitute`

```text
[path.]substitute=All|Key|Value|None
```

| Mode | Names interpreted | Values interpreted |
|---|---:|---:|
| `All` | yes | yes |
| `Key` | yes | no |
| `Value` | no | yes |
| `None` | no | no |

Values are case-insensitive.

Default: `All`. Any other default would stop interpreting references in profiles that never name this directive, which the value references Section 3.1 preserves do not admit.

A `substitute` directive governs only the node it matches; descendants use their independently effective mode, defaulting to `All`. The scope is the node rather than the subtree because the table's columns are absolute rather than relative: a subtree reading would have to give `All` at a descendant the meaning "interpret again", which the table does not define, and would leave `substitute` the one path-scoped directive that cannot be narrowed again beneath itself.

The pathless form matches every node. It is therefore not a statement about the root alone, and it competes with a path-scoped form under Section 15.2 like any other pair of directives: the later declaration in source order wins, and being pathless neither strengthens nor weakens it.

Section 15.1 step 6 matches this directive's pattern against "an entry's declared pre-expansion path", and that path may itself be a Section 12 template. It is matched as it was written, one name part at a time, so `a.*.b.substitute=None` governs the entry declared `a.*.b`, and a concrete path such as `a.x.b` does not reach it. Matching a template structurally instead would leave the table's name column unreachable, because the only entries whose *names* have anything to interpret are exactly the entries whose declared paths are not literal text; `Key` and `None` would then differ from `All` and `Value` nowhere, and an implementation could omit half the directive and satisfy every other sentence here.

A Section 8.6 exclusion mask is not an entry and is not governed by this directive. Its pattern is interpreted as written whatever mode is effective at the paths it names, because a mask carries no value and step 6 speaks only of an entry's declared path.

### 16.8 Format input options

Root-level input options use comma-separated names:

```text
xmlinputoptions=PreserveWhitespace
yamlinputoptions=PreserveComments
jsoninputoptions=Strict
```

Supported initial values:

#### XML

- `NormalizeFormattingWhitespace`;
- `PreserveWhitespace`.

These values are mutually exclusive. The later complete directive wins.

Default: `PreserveWhitespace`.

#### YAML

- `PreserveComments`, enabled by default.

#### JSON

- `Strict`, enabled by default.

No JSON-comment option exists in this version.

Selector-qualified input-option directives are blocking scheme errors because input parsing occurs before output instances exist.

### 16.9 Format output options

For every output-options directive, the later complete directive replaces the earlier complete flag set. Flags from separate declarations do not accumulate. When a replacement omits every flag from a mutually exclusive mode group, that group's documented default is reapplied.

Flag names are case-insensitive and surrounding whitespace is ignored, as for the comma-separated values of Section 16.1. Naming both flags of a contradictory pair in one declaration is `SCHEME001`; naming neither selects that group's default rather than leaving the group unset, because every flag group governs a decision the serializer has to make.

#### XML

```text
[selector.]xmloutputoptions=Indent,NewLineOnAttributes,PreserveCData
```

Supported flags:

- `Indent`;
- `NoIndent`;
- `NewLineOnAttributes`;
- `PreserveCData`;
- `CDataAsText`;
- `Declaration`;
- `NoDeclaration`.

`Indent` and `NoIndent`, `NoIndent` and `NewLineOnAttributes`, `PreserveCData` and `CDataAsText`, and `Declaration` and `NoDeclaration` are contradictory pairs.

Default: `Indent,PreserveCData,Declaration`.

XML `Indent` uses two ASCII spaces per element nesting level outside mixed content. `NewLineOnAttributes` places every attribute on its own line, including the first, indented two spaces beyond the owning start tag; a start tag that carries attributes therefore ends after the element name and its `>` follows the last attribute. `NoIndent` inserts no formatting whitespace.

`NoIndent` and `NewLineOnAttributes` are therefore a contradictory pair as well, and declaring both
is `SCHEME001`. The line break and the two spaces `NewLineOnAttributes` requires are formatting
whitespace, so on any element carrying an attribute the two flags demand different bytes. Refusing
the combination is what makes the conflict visible: the alternative readings each silently discard
a flag the author wrote down, and a scheme author cannot see a flag that did nothing.

#### JSON

```text
[selector.]jsonoutputoptions=Indent
```

Supported flags:

- `Indent`;
- `Compact`;
- `EscapeNonAscii`.

`Indent` and `Compact` are contradictory.

Default: `Indent`.

`EscapeNonAscii` applies to every JSON mapping key and string scalar value. Each Unicode scalar above U+007F is emitted as uppercase hexadecimal JSON `\uXXXX`; a scalar above U+FFFF is emitted as the corresponding UTF-16 surrogate pair `\uXXXX\uXXXX`. ASCII controls continue to use valid JSON escapes. Without this flag, non-ASCII text is emitted as literal UTF-8 wherever UTF-8 admits it, and a UTF-16 code unit that UTF-8 cannot encode — an unpaired surrogate — is emitted as a `\uXXXX` escape regardless of the flag. The flag is independent of `Indent` and `Compact`.

The escape is not an exception to the flag but the only way to keep the text. UTF-8 has no encoding for an unpaired surrogate, so a writer obeying the literal rule absolutely would have to substitute U+FFFD, which discards the code unit while reporting success. An implementation is not required to make such text reachable; this states what it emits if it does.

`Indent` uses two ASCII spaces per nesting level. `Compact` emits no insignificant spaces or line breaks.

#### YAML

```text
[selector.]yamloutputoptions=PreserveComments
```

Supported flags:

- `PreserveComments`;
- `DiscardComments`.

`PreserveComments` and `DiscardComments` are contradictory.

Default: `PreserveComments`.

YAML formatting uses fixed two-space indentation for each mapping or sequence nesting level. Literal block-scalar content is indented two spaces beyond its owning key or sequence indicator. Tabs are never used for indentation.

YAML document separators and alternate multiline-style preservation are not supported in this version.

#### INI

```text
[selector.]inioutputoptions=...
```

The initial portable options are:

- `SemicolonComments`;
- `HashComments`;
- `RejectMultiline`;
- `EscapeMultiline`;
- `QuoteValues`;
- `GlobalSection`.

`SemicolonComments` and `HashComments` each enable comment emission and select its marker; they are mutually exclusive. `RejectMultiline` and `EscapeMultiline` are mutually exclusive.

Default: `RejectMultiline`. Comments are discarded unless `SemicolonComments` or `HashComments` is selected, and global keys are emitted in a preamble unless `GlobalSection` is selected.

`GlobalSection` belongs to no exclusive pair. Its absence is not a choice between two spellings of one decision but the dialect's stated default, in the same way as `QuoteValues`.

### 16.10 `merge`

```text
[path.]merge=deep|replace|append|error
```

Values are case-insensitive.

- `deep`: ordered mappings merge recursively, implicit sequence items concatenate, an explicit ordering value addresses and recursively merges the sequence item already at that value under Section 17.1, later scalar payloads override earlier scalar payloads, and scalar/container shape contributions coexist in the overlay until output projection.
- `replace`: the later complete value replaces the earlier value. "Value" here means payload, container presence, children, and sequence projection; two things at the path are deliberately not replaced. Comments bound to the path survive, because Section 17.1 keeps a comment "whenever their logical path survives" and omits it only on "replacement of an ancestor that removes the path" — replacement *at* the path does not remove the path. The sequence allocation high-water mark also survives, by Section 17.2.
- `append`: every item in the later sequence contribution, including explicitly indexed items, is rebased in ascending original ordering value onto fresh implicit ordering values above the current high-water mark; a source contribution that is a nonempty all-canonical-numeric mapping is sequence-eligible for this purpose; other non-sequence use is an error.
- `error`: after entries inside each source contribution have been folded, any distinct second source or generated contribution at the path is an error. Numeric-map inference is a projection of its existing contribution and does not count again.

Default: `deep`.

Non-sequence use of `append` is a blocking `TYPE001` in the input phase, anchored at Section 16.10, and it is raised only when a second contribution actually has to be appended. A sole contribution at the path is not appended to anything, so the strategy never engages and the value publishes unchanged; `append` is a rule for combining contributions, not an assertion that the path holds a sequence. Making a lone contribution fail would break the common case of declaring one strategy across a wildcard whose matches do not all collide.

A contribution is **at path `P`** when it contributes a payload, explicit container presence, sequence projection, or any descendant under `P`. A `merge` directive governs only the node it matches; descendants use their independently effective strategy, defaulting to `deep`.

`merge` applies only to common-model input and wildcard-generated contributions at pipeline steps 8 through 11. It never configures output-destination collisions, and it does not configure the Section 16.5 fold of `key`-generated records onto an independent sequence, which follows Section 17.1 directly.

Input `merge` directives required at pipeline step 4 must use literal paths and must not contain wildcards or references.

### 16.11 `filemerge`

```text
[selector.]filemerge=deep|replace|append|error
```

`filemerge` applies only when two or more fully transformed output contributions resolve to the same canonical destination path at pipeline step 18.

- `deep`: structurally merge same-format document models using Section 17;
- `replace`: the later same-format output contribution replaces the complete earlier visible document model while retaining destination high-water state as specified in Section 17.5;
- `append`: append/rebase sequence contributions; a non-sequence contribution is a blocking `TYPE001` in the planning phase, anchored at Section 17.5, carrying the `destination` and no `path`, because the fold is a property of the destination rather than of either contribution;
- `error`: any second contribution to that destination is a blocking collision error.

Default: `deep`.

Wildcard-qualified `filemerge` selectors are supported and are expanded with their output instance. The effective value is taken from the later colliding output declaration. `filemerge` does not affect input data merging, wildcard generation, or shape precedence. Cross-format collisions always use the deterministic complete-plan replacement rule in Section 17.5 rather than `filemerge`.

`filemerge=error` is not sticky. A later matching declaration may replace it with another complete `filemerge` value under universal source-order precedence; omitting `filemerge` on that later declaration uses the default `deep`.

## 17. Merge semantics

### 17.1 General deep merge

For two contributions `earlier` and `later`:

- mapping plus mapping: recursively merge matching keys; place each surviving key at its winning contribution position;
- sequence plus sequence: merge according to ordering-value provenance—implicit later items concatenate, while an explicit later ordering value addresses the item already at that value and the two items are then combined by the rules of this section, recursively. Provenance decides *which item* the later contribution meets, not *how* the two combine; a later contribution therefore never removes a sibling key it does not name;
- scalar or null payload plus scalar or null payload: later payload wins;
- scalar/null payload plus mapping or sequence contribution: retain both in the overlay with independent source marks;
- mapping plus sequence contribution at one node: retain both container projections in the overlay; a destination requiring one container shape uses the later container contribution and warns;
- non-XML comments bind to logical paths rather than individual scalar or shape contributions; they accumulate and survive merge whenever their logical path survives. Shape-conflict projection attaches them to the surviving projection. They are omitted only when the logical path is absent from that output through permanent masking, non-selection, `type=ignore`, or replacement of an ancestor that removes the path.

### 17.2 Arrays

Arrays use the stable ordering-value model from Sections 5.4 and 8.7.

Separate native source arrays concatenate according to CLI file order and within-file order because their items receive fresh implicit ordering values.

Explicit canonical numeric mapping keys are run-global ordering values at their sequence path under `deep` and address matching values, merging into the item already there under Section 17.1. They are not rebased unless `merge=append` explicitly requests rebasing.

`merge=replace` removes the earlier visible sequence projection but does not lower the path's allocation high-water mark. Later automatic allocation therefore never reuses removed values; later explicit contributions may intentionally address a prior value unless permanently suppressed.

### 17.3 JSON and YAML

JSON and YAML use the general merge rules directly.

Raw serialized documents are never appended byte-for-byte.

### 17.4 XML

XML uses the same principles adapted to XML node kinds.

When XML contributions ultimately target the same destination and have the same expanded root name, classify the complete destination-level contribution set before folding:

- attributes form an ordered mapping by expanded attribute name and merge recursively as scalars;
- later duplicate attributes override earlier values and move to the winning contribution position;
- each parent is classified once across all destination-level contributions after output-view transformations for serialization and fold behavior only;
- the presence of any text or CDATA node makes the parent mixed-content;
- comments alone do not make a parent mixed-content;
- for a mixed-content parent, every text, CDATA, comment, and child-element node is a sequence item and complete content streams concatenate in source order; child elements in mixed content do not deep-merge with elements from another contribution;
- for an element-only parent, children are classified by expanded name as follows;
- for one expanded child name, if every source contribution contains at most one occurrence, those singleton children deep-merge in source order;
- if any source contribution contains more than one occurrence, every occurrence of that expanded name forms one sequence and all occurrences concatenate in source order;
- the classification is computed before folding, so grouping is associative and does not change after an intermediate merge;
- canonical addresses established before output transformations are never reassigned by destination-level classification; items are ordered or rebased only through the destination-fold ordering rules in Section 17.5.

This clause governs element merging inside the model, where two contributions supply elements of the same expanded name at one path. It is not a destination-fold rule, and the destination fold has no incompatible-root case to decide: an XML input's document element is an ordinary leading name part, so two inputs with different document elements occupy different paths and never collide, and two output contributions folding to one file merge as views *before* any document element is chosen. The document element of the written file is then selected from the merged view under Section 14.1, so a fold that leaves two top-level members is `TYPE001` asking for an explicit `root` rather than a contest between two names.

When XML destination-fold intent is ambiguous, `filemerge=replace` provides deterministic whole-document override.

When the effective destination `filemerge` strategy is `replace`, the later element's complete value—attributes, content tokens, comments, and children—replaces the earlier element. Singleton/sequence classification and recursive child merging are not applied to the replaced earlier element.

### 17.5 File-level collisions

A canonical destination path is the portable-encoded relative path with `/` separators, no `.` or `..` segments, and no redundant separators. All output contributions are grouped by byte-identical canonical destination path before rendering.

For cross-platform collision detection, also compute a portability key by uppercasing ASCII letters in the canonical path. Because portable segment encoding makes every non-ASCII byte an uppercase `%HH` sequence, this comparison is platform-independent. Two nonidentical canonical paths with the same portability key are a blocking `PATH001` collision rather than a merge. Byte-identical canonical paths continue through the deterministic collision fold below.

Every sequence item retains explicit or implicit provenance into this fold. Each sequence path in a destination accumulator has its own high-water mark:

- an implicit item from a later output contribution is rebased onto the next fresh destination ordering value;
- an explicit item retains its supplied ordering value and patches an existing item at that value under `deep`;
- `append` rebases every later item regardless of provenance;
- `replace` discards the visible accumulated projection without lowering the destination high-water mark.

Every output contribution carries its complete per-path high-water map, including marks raised by items hidden by output projection. Selector-prefix removal, `root`, `key`, and `type` transformations re-address this map together with the associated value. A destination accumulator absorbs the incoming high-water mark for a path before allocating or patching incoming items at that path.

Ordering values from different original selector paths therefore interact only after they have been transformed to the same destination path, using these destination-fold rules.

A contribution's Section 15.2 transform table travels into the fold in the same way as its high-water map. Where several contributions fold to one destination, that destination's table is the union of theirs, so a `type` bound in a contribution that is not the last one still applies: Section 15.2 evaluates `type` in every output instance containing the path, and a contribution's own instance is one of them. Every table addresses the destination frame once selector-prefix removal has been applied, so their keys compare directly. Where two of them bind the same destination path, the later contribution's binding is the effective one, on the Section 16.11 rule for a collision between output declarations. A transform table addresses paths and not values, so a binding is not extinguished by the loss of the particular value it was written against; a binding whose destination path holds no value after the fold simply has nothing to apply to.

The §17.5 fold key of an output contribution is the tuple already used below: output-declaration source order, format ordinal, wildcard match order, and concrete-selector UTF-8 byte order. Same-format `replace` preserves the earliest prior publication key even when no prior sequence high-water state exists; only cross-format replacement resets it.

Contributions are folded strictly left to right by:

1. output-declaration source order;
2. format ordinal within one comma-separated `output` value;
3. wildcard match order, as defined in Section 12.4;
4. concrete selector encoded as UTF-8 and compared by unsigned-byte ordinal order as the final deterministic tie-breaker.

The selector is spelled by the Section 19.1 namespace name encoding before those bytes are taken. That encoding is total and injective, which is what makes this component a tie-breaker rather than another way to tie: a spelling that merely joined part texts with the delimiter would map the one-part selector `a\.b` and the two-part selector `a.b` to the same bytes, leaving two distinct contributions to fold in arrival order.

A cross-format replacement discards the complete accumulated plan for that destination, including document data, comments, renderer state, sequence provenance, and every destination high-water mark. Later contributions then fold onto the replacement from a fresh destination state. Implementations must not group by format before folding.

For every destination collision, emit a warning identifying:

- destination path;
- earlier declaration;
- later declaration;
- merge or replacement decision.

If contributions have the same output format, merge their document models using the effective `filemerge` strategy.

File-level merge operates on fully transformed contribution models after `type`, `key`, and `root` have been applied.

The merge can therefore create a shape conflict that exists in no contribution: one output instance may supply only a scalar at a path and another only a mapping, each internally consistent, and the destination accumulator then holds both. Such a conflict is resolved exactly as Section 4.4 resolves any other, and reports `TYPE002` once for the destination and the projected path, carrying `destination` and `path` and naming no output instance. No instance is named because none is distinguished: the conflict belongs to the accumulator, and attributing it to the earlier or the later contribution would report a fault against a model that does not contain one. This is why the cardinality unit is the destination rather than the output instance, which also makes the count of `TYPE002` records a property of the files written rather than of how many declarations happened to write them.

Duplicate formats within one `output` declaration, such as `output=json,json`, are blocking scheme errors rather than self-collisions.

The effective `filemerge` strategy and renderer option set are taken from the later colliding output declaration. A later output-options directive replaces the earlier complete option set.

If contributions have different output formats, the later contribution replaces the complete earlier file plan. This is a deterministic file-level override.

Cross-format collision is not a blocking error.

## 18. Scalar inference

Scalar inference for untyped namespace values is locale-independent.

The grammar is:

1. exact case-insensitive `null` becomes null;
2. exact case-insensitive `true` or `false` becomes Boolean;
3. `[+-]?[0-9]+` becomes an arbitrary-precision integer;
4. a JSON-compatible decimal or exponent form becomes an arbitrary-precision decimal;
5. otherwise the value remains a string.

Thousands separators, locale decimal commas, hexadecimal, `NaN`, and infinities are not inferred.

Typed JSON and YAML input scalars retain their source kind without re-inference.

An untyped payload containing an unescaped reference is not inferred until reference resolution as specified in Section 13.2.

Canonical decimal text is produced as follows:

1. represent the finite decimal as a sign, a nonnegative integer coefficient, and a base-10 exponent;
2. zero is `0.0` or `-0.0` according to its retained sign;
3. for nonzero values, remove trailing coefficient zeros while increasing the exponent by the same count, and remove leading coefficient zeros;
4. the adjusted exponent is `exponent + coefficient-digit-count - 1`;
5. use plain notation when the adjusted exponent is from `-6` through `20`, inclusive, and scientific notation otherwise;
6. scientific notation uses one coefficient digit before the decimal point, at least one digit after it—even when that digit is `0`—lowercase `e`, and an exponent without a leading `+` or redundant zeros;
7. when plain notation would otherwise be indistinguishable from an integer, append `.0`.

Thus decimal `1.0` remains `1.0`, decimal negative zero becomes `-0.0`, and the algorithm is independent of locale and source spelling.

Canonical decimal text and base-10 integer text are used by every output format and by interpolation. Numeric source spelling is never retained.

## 19. Output format rules

### 19.1 Namespace

Namespace output emits ordered scalar projections:

```text
qualified.name=value
```

Mappings use name parts. Sequences use generated zero-based decimal parts after all concatenation and merging.

A flat projection visits the selected view depth first in pre-order: a node's own scalar is emitted before anything beneath it, its mapping children follow in their Section 5.2 order, and its sequence items follow in ascending ordering value. Pre-order is what places `a.x=1` before `a.x.z=3` in Section 4.4. The two container facets keep their own orders rather than interleaving, because Section 5.2 orders mapping children by position mark while Section 5.4 orders items by ordering value and no comparison between the two is defined; a node emits only one of them in any case, under Section 16.4.

The configured root and delimiter apply.

Namespace name encoding is total and injective. In every name part, escape:

- backslash;
- the configured delimiter string;
- literal `*`;
- literal `${`;
- `=`;
- `}`;
- CR, LF, and tab;
- a leading `!` or `#` when it could begin a physical record.
- leading `@`, `#`, or `Q{` text when the component is an ordinary name rather than a typed XML component.

A leading `#` is escaped whether or not digits follow it, because Section 8.2 makes marker recognition commit: an unescaped `#` beginning a component must complete a content token, so `#x` would not read back as an ordinary name.

`}` is escaped although Appendix A.2 admits it unescaped in a name, because Appendix A.4 ends a reference at the first unescaped `}`. Were it left bare, a value of one reference to the name `a}b` would encode as `${a}b}` and read back as a reference to `a` followed by the literal text `b}`. Escaping is by construction rather than by context, so the same name has one spelling wherever it occurs.

A scalar that begins a configured-delimiter occurrence is always escaped as `\u{HEX}` under Section 16.4, even when Section 8.2 defines another lexer form for that scalar. The default delimiter therefore emits `\u{2E}`, not `\.`. Every other character requiring escape uses its Section 8.2 lexer form where one exists and `\u{HEX}` otherwise. The forbidden set is Unicode categories `Cc`, `Cf`, and `Cs`, plus U+0085, U+2028, and U+2029. Emitted escapes are atomic and never rescanned. Escaping is unconditional rather than dependent on whether a wildcard happens to be active.

Every scalar in the forbidden set is escaped as `\u{HEX}` wherever an escape is admissible. Two positions admit none. An unpaired surrogate has no `\u{HEX}` spelling, because Appendix A.2 excludes surrogates. Section 11.4 admits only `\}` and `\\` inside a `Q{...}` URI, so a URI containing a forbidden scalar cannot be escaped there either. Serializing a name containing either to namespace output is blocking `SERIALIZE001`.

Typed XML components emit their canonical unescaped `@`, `#n`, or `Q{...}` notation. Ordinary components with the same text emit the escaped forms, preserving identity.

A typed `#n` component cannot be the first emitted namespace path part because it would be parsed as a comment record. Such a selected view requires `root` to place an ordinary part before it; otherwise serialization is blocking `SERIALIZE001`.

For record-leading escaping, "`!` or `#` could begin a physical record" means all emitted text preceding it in that record consists only of spaces and tabs.

Values use the inverse of the namespace value lexer:

- backslash as `\\`;
- LF as `\n`;
- CR as `\r`;
- tab as `\t`;
- literal wildcard as `\*`;
- literal reference start as `\${`.

Physical output entries are always one line. Multiline scalar data is represented through escapes, never literal record-breaking line terminators.

Comments are emitted as normalized `# comment` lines where their association can be represented. Where it cannot, the comment is converted to the nearest position the format does represent rather than discarded: an inline comment becomes a full-line comment immediately before the key it was attached to, and a document-trailing comment is emitted at end of file. This is the same normalization Section 20 states for INI, and for the same reason — the flat formats carry only full-line comments, so conversion is the only way a comment survives at all, and a comment that moves a line is a smaller loss than one that disappears. Nothing here is `WARN003`, because no concept is discarded.

A comment bound to a container-only path is converted the same way. These formats emit no key for such a path, so the comment is emitted immediately before the first key the projection emits for that path or for any path beneath it. Where the projection emits no key for that path or anything beneath it, the comment is not emitted and nothing is reported: it annotates content that did not survive selection, and a comment must not outlive what it describes — the same reason an `ignore` mask takes the comments bound to the entries it removes.

Re-reading such an output binds the comment to that first key rather than to the container, because these formats have no key for a container-only path to carry the association. This is a normalization and not a discard, so it is not `WARN003`; the alternative is losing the comment outright, which is the larger loss this rule exists to prevent.

Typed values use canonical locale-independent text:

- null: `null`;
- Boolean: `true` or `false`;
- integer: base-10;
- decimal: canonical nonlocalized decimal or exponent form;
- string: escaped text.

XML node kinds such as ordinary text versus CDATA are not represented in ordinary namespace values, but XML addresses are emitted using the canonical typed components above. An extended namespace mode may preserve additional node-kind metadata in the future.

When the selected output root is a bare scalar, namespace output retains the final concrete selector part as the emitted key. `root` prefixes that key rather than replacing it, as in Section 16.3.

### 19.2 Quoted namespace

`quotednamespace` is defined as POSIX shell assignment output without `export`.

Keys must be valid shell identifiers after applying root and delimiter:

```text
[A-Za-z_][A-Za-z0-9_]*
```

Invalid keys are `SHELL001`.

Values use single-quote shell escaping:

```sh
NAME='value'
NAME='can'\''t'
```

This preserves spaces, `$`, backticks, double quotes, backslashes, exclamation marks, and line breaks without expansion.

NUL is not representable and is an error.

A null payload emits the text `null`, as in Section 19.1. Quoted namespace is namespace output under shell quoting rather than a different value model, so a consumer reading `NAME='null'` learns what a namespace consumer reading `name=null` learns. Emitting an empty assignment instead would make null indistinguishable from the empty string, which is a different payload.

When the selected output root is a bare scalar, quoted namespace retains the final concrete selector part as the assignment name. `root` prefixes that name rather than replacing it, as in Section 16.3, and the parts are joined by the delimiter.

### 19.3 JSON

JSON output:

- preserves ordered mapping order;
- preserves typed scalars;
- renders comments nowhere and emits a summarized discard warning when comments exist;
- serializes logical line breaks as JSON `\n` escapes;
- applies `root`, `key`, and `type` transformations after input `merge`; destination collisions use `filemerge`;
- uses indented output by default.

At an overlay containing both payload and container contributions, Section 4.4 selects exactly one JSON shape and warns about the omitted shape.

#### JSON output bytes

Section 24 requires two conforming implementations to produce identical bytes, so the escaping and
layout below are normative rather than a description of one writer's habits. `Indent` and `Compact`
are Section 16.9 flags and `EscapeNonAscii` is stated there.

Within a string — both mapping keys and string scalars — exactly these characters are escaped:

| Source | Emitted |
|---|---|
| `"` U+0022 | `\"` |
| `\` U+005C | `\\` |
| U+0008 | `\b` |
| U+0009 | `\t` |
| U+000A | `\n` |
| U+000C | `\f` |
| U+000D | `\r` |
| any other U+0000–U+001F | `\u00XX`, uppercase hexadecimal |

No other character is escaped. In particular `/` is emitted as itself rather than as `\/`; `<`, `>`,
and `&` are emitted as themselves, because JSON is not consumed as HTML and escaping them would make
the output depend on a downstream context the tool cannot see; and U+007F, U+0085, U+00A0, U+2028,
and U+2029 are emitted as literal UTF-8. RFC 8259 requires none of these, and a writer that escapes
more still produces valid JSON — which is exactly why the set has to be fixed here rather than left
to the writer. `EscapeNonAscii` and the unpaired-surrogate rule of Section 16.9 are the only
additions to this set, and the only place uppercase `\uXXXX` applies above U+007F.

Under `Indent`:

- a nonempty mapping is `{`, then for each member a line break, that member's indentation, its key
  as a string, `:`, one space, and its value, with `,` closing every member but the last; then a
  line break, the mapping's own indentation, and `}`;
- a nonempty sequence uses the same shape with `[`, `]`, and no keys, so each item is alone on its
  line;
- indentation is two ASCII spaces per nesting level, and the document node is at level zero;
- an empty mapping is exactly `{}` and an empty sequence exactly `[]`, with no space or line break
  between the brackets, at any depth.

Under `Compact` no insignificant character appears at all: no line breaks, no indentation, and no
space after `:` or `,`.

Under both, the document is followed by exactly one LF, as Section 24 requires of every text output.

Section 6.4.3 fixes the diagnostic stream's bytes separately and does not agree with this section in
every detail — it uses lowercase `\u00xx`, and writes one compact object per line rather than either
layout here. That is two encodings for two purposes, not one rule stated twice, and neither may be
inferred from the other.

A mapping key carries the Section 11.4 markers, so an attribute component `x` is the key `@x`, a content component is `#0`, and a qualified element is `Q{uri}local` — the same spellings Section 9.1 reads back. An ordinary component whose own literal text begins with `@`, `#`, `\`, or `Q{` is written with a leading `\`, so that it too reads back as itself. This makes a JSON document this tool writes readable by it, and lets a JSON input name any component an XML input can carry.

Distinct logical paths must never both emit a member of one mapping under the same key. Escaping removes the ordinary case, so a collision now requires two components that are distinct in the model and spell one key regardless — but emitting both would produce a duplicate-key document that Section 9.3 forbids and that this specification's own reader rejects, so a mapping-key collision after projection is blocking `FLAT001`. A later contribution to the *same* logical path is an override under Section 4.4 and is never a collision; both colliding paths remain separately addressable, so an input may override either one to resolve the conflict.

### 19.4 YAML

YAML output:

- preserves mapping and sequence order;
- preserves supported scalar types;
- emits retained comments in normalized positions;
- uses literal block scalars for multiline values that a block scalar can carry exactly and that can legally terminate the document, and the double-quoted form otherwise;
- does not emit `---`;
- does not preserve original quote style, tag syntax, anchors, aliases, or folded-versus-literal source style;
- applies structural merge before serialization.

A literal block scalar reproduces its content from its indentation and chomping indicator alone, which is not enough for every multiline value. A line with trailing whitespace, a CR line break, a control character outside YAML's `c-printable`, and a first non-empty line that is itself indented are each altered or lost by a block scalar, and Section 3.3 requires a same-format round trip to preserve them. A value ending in a blank line needs the `|+` indicator, whose block ends with two line breaks and so cannot satisfy Section 24's requirement that a text output end with exactly one LF.

The double-quoted form carries all of these exactly, so it is the fallback rather than a defect. A value that cannot terminate the document is quoted in *every* position, not only where it would fall last; otherwise a value's spelling would depend on where it sorts among its siblings, and adding an unrelated key would silently rewrite an untouched value.

A string whose plain spelling would resolve to a non-string kind under `RestrictedYaml1` is emitted single-quoted, with a literal single quote doubled as `''`.

At an overlay containing both payload and container contributions, Section 4.4 selects exactly one YAML shape and warns about the omitted shape.

A mapping key carries the Section 11.4 markers and escapes a marker-shaped ordinary component, and a mapping-key collision after projection is blocking `FLAT001`, on the same rules and for the same reasons as Section 19.3.

#### YAML output bytes

Section 24 requires two conforming implementations to produce identical bytes. YAML offers more spellings of one string than any other supported format — plain, single-quoted, double-quoted, literal block, folded block — and a writer that chooses among them by its own taste satisfies YAML while breaking Section 24. The rules above select the block form; the rest of the selection, and the spelling of each style, is fixed here.

Exactly one style is chosen for a scalar, by taking the first rule below that applies.

1. **Literal block scalar**, under the conditions already stated in this section. The indicator is `|` when the value ends with exactly one LF, and `|-` when it ends with any other character; `|+` is never emitted, for the reason given above. Content lines are indented two spaces past the indentation of the key or `-` that introduces the value, and the terminating LF of the last content line is the block's own.

2. **Double-quoted**, when the value contains any character that no other style carries exactly: a C0 control other than TAB (U+0009), a LF that rule 1 declined, U+007F, U+0085, U+2028, U+2029, or U+FEFF. A TAB alone does not select this style, because a single-quoted scalar carries an interior TAB unchanged; rule 3 quotes it there.

3. **Single-quoted**, when the plain form would not read back as the same string. That is the case when the value:

   - is empty;
   - begins with one of the nineteen YAML indicator characters `-`, `?`, `:`, `,`, `[`, `]`, `{`, `}`, `#`, `&`, `*`, `!`, `|`, `>`, `'`, `"`, `%`, `@`, or `` ` ``;
   - begins with `...`;
   - begins or ends with a space or a TAB;
   - contains a TAB anywhere;
   - contains `,`, `[`, `]`, `{`, or `}` anywhere;
   - contains `: ` or ` #` anywhere, or ends with `:`; or
   - would resolve to a non-string kind under `RestrictedYaml1`.

4. **Plain**, otherwise.

Rule 3 is deliberately stricter than YAML's own plain-scalar productions in two places, and the strictness is the specified behavior rather than a permitted approximation. An indicator character is refused wherever it opens the value, though YAML admits `-`, `?` and `:` there when the next character is not a space; and a flow indicator is refused in every position, though only flow context requires it. Both make the spelling of a value independent of where it is written, which is what lets a fixture pin it. An implementation applying YAML's productions literally emits `-ab` and `a,b` plain and does not conform.

A single-quoted scalar has one escape, `''` for a literal single quote. A double-quoted scalar uses `\"`, `\\`, `\n`, `\r` and `\t`, and spells every other escaped character `\uXXXX` with four uppercase hexadecimal digits. YAML's remaining short forms — `\0`, `\a`, `\b`, `\v`, `\f`, `\e`, `\N`, `\_`, `\L`, `\P` — are never emitted, so that one rule covers the whole range rather than a table of exceptions. A character outside the sets named in rule 2 is written as itself in UTF-8, including U+00A0 and every non-ASCII printable character.

A mapping key is spelled by these same rules, with one addition: a key whose text is `<<` is single-quoted, because Section 10 refuses to read an unquoted merge key back. The addition applies to keys alone. A *value* of `<<` is written plain, since nothing resolves a merge key in value position and the plain form reads back as the two characters it is. `RestrictedYaml1` also resolves no tag on a key, so quoting a key is never required for resolution alone; a key is nevertheless quoted on the same conditions as a value, so that one string has one spelling wherever it appears.

Layout is fixed as well:

- each nesting level indents two spaces;
- a mapping entry is `key`, `:`, one space, then the value, or `key`, `:`, a line break, and the nested block;
- a sequence entry begins `- ` at two spaces past its key's indentation; a sequence nested directly in a sequence continues on the parent entry's line, and a mapping nested in a sequence begins its first key on that line;
- an empty mapping is `{}` and an empty sequence `[]`, at any depth;
- no `---` and no `...` are written, as stated above;
- every line ends with one LF, and the document ends with exactly one LF under Section 24.

Comments have their own layout, and it is fixed here for the same reason the scalar styles are: Section 19.4 emits them, so Section 24 requires two implementations to write the same bytes.

- A comment line is `#`, one space, and the comment text. Where the text is empty the separating space is not written either, so the line is `#` alone, under Section 24's rule that no line ends in whitespace. No other spelling is emitted, whatever the source used.
- A leading or trailing comment occupies its own line at the indentation of the entry it is bound to. A trailing comment of a mapping's last key indents with that key, not with the mapping.
- An inline comment follows its entry on the same line, separated by exactly one space: `key`, `:`, one space, the value, one space, then the comment line form above.
- A document-leading comment is written at column zero before the first line of the document, and a document-trailing comment at column zero after the last. Neither is indented, because neither is bound to a value.
- No blank line is written before, after, or between comments.

The comment text itself is written verbatim after that marker. It cannot contain LF: Section 4.5 admits no multi-line non-XML comment, and the one comment form that can — the standalone XML comment of Section 11.5 — never reaches a YAML destination, being discarded under Section 20.

### 19.5 XML

XML output:

- emits one document element;
- preserves expanded names and required namespace declarations;
- preserves ordered attributes and content;
- preserves mixed content without inserting indentation inside it;
- emits retained XML comments;
- emits retained CDATA when configured;
- applies `element`, `attribute`, `text`, and `cdata` types;
- applies structural merge before serialization;
- uses UTF-8;
- includes an XML declaration under the default `Declaration` option and omits it under `NoDeclaration`;
- uses normalized indentation outside mixed content by default.

#### XML output bytes

Section 11.8 places prefix choice, declaration placement, empty-element spelling and attribute quote
style outside the *preservation* contract: this tool does not promise to reproduce the spellings an
input used. That is not permission to emit whatever a given XML library emits. Section 24 requires
two conforming implementations to agree byte for byte, so what is emitted is fixed here even though
what was read is not reproduced.

The declaration, under the default `Declaration` option, is exactly:

```text
<?xml version="1.0" encoding="utf-8"?>
```

followed by one LF. Attribute values are delimited by `"` U+0022. Within a start tag the element name
and the attributes that follow it are separated by exactly one space U+0020, and attributes are
written in the order Section 4.6 records. `NewLineOnAttributes` (Section 16.9) replaces each of those
separators with a line break and an indent, and changes nothing else about the tag.

In element text content, exactly `&` U+0026, `<` U+003C, `>` U+003E and CR U+000D are escaped, as
`&amp;`, `&lt;`, `&gt;` and `&#xD;`. TAB and LF are emitted literally, and `"` and `'` are emitted
literally because neither can end text content.

In an attribute value, exactly `&`, `<`, `>`, `"`, TAB U+0009, LF U+000A and CR U+000D are escaped,
as `&amp;`, `&lt;`, `&gt;`, `&quot;`, `&#x9;`, `&#xA;` and `&#xD;`. `'` is emitted literally because
`"` is the delimiter.

CR is escaped in both positions rather than written literally because XML 1.0 line-end normalization
requires every parser to turn a literal CR into LF, so `&#xD;` is the only spelling that survives a
round trip. Section 3.3 requires that it does.

An element with no content is emitted as `<name />`, with one space before the slash, and that space
is present whether or not attributes precede it, so an empty element carrying attributes ends
`x="1" />`. An element whose content is the empty string is emitted as `<name></name>`. The
distinction is meaningful and is preserved: the first has no payload and the second has a payload
that is the empty string.

A CDATA section whose content contains `]]>` is split so that `]]` ends one section and `>` begins
the next, which is the only split that does not change the content:

```xml
<cd><![CDATA[x]]]]><![CDATA[>y]]></cd>
```

An element whose expanded name carries a namespace URI is emitted with no prefix, and the URI is
declared as the default namespace on that element unless the default namespace already in scope is
that URI. An unprefixed attribute is in no namespace, so an attribute whose expanded name carries a
URI cannot use that declaration and takes a generated prefix instead. The generated prefixes are
`n1`, `n2`, and so on, numbered in the order their namespaces are first needed in document order,
and every one of them is declared on the document element. Leaving the name to the writer is what
makes an XML library's private counter observable in the output of a specified tool.

A namespace declaration is written after every ordinary attribute of the element that carries it.
The generated prefix declarations therefore follow the document element's own attributes, in
numbering order, and the document element's default-namespace declaration follows them in turn. An
element in no namespace nested inside a default namespace undeclares it with `xmlns=""`, written in
that same position.

An element takes no prefix even where one is in scope for its namespace. A writer that reused an
in-scope prefix would make the spelling of one element depend on whether some attribute elsewhere in
the document happened to need a prefix at all, so that adding an attribute in one subtree rewrote
element tags in another. Both spellings denote the same element and neither is more correct as XML;
what is not acceptable is that a local edit moves bytes far away from it.

A comment is emitted as `<!--`, its content, and `-->`, with nothing added on either side. The
content of a comment read from XML is the text between those delimiters, so `<!--x-->` and
`<!--  x  -->` are distinct and both survive a round trip: spaces and tabs there are content, and
XML normalizes neither. The content of a comment converted from another format under Section 20 is
that comment's text, which Section 4.5 has already stripped of surrounding spaces and tabs; it
therefore emits as `<!--text-->`. The writer does not pad either one to the conventional
`<!-- text -->`, because a rule that padded would make the two indistinguishable in the output and
break the round trip.

A CR in comment content is the one detail that does not survive, and no implementation can make it.
XML 1.0 line-end normalization converts CR and CRLF to LF before the parser reports anything, and
the `&#xD;` escape that rescues CR in text and attribute values does not exist inside a comment,
where no reference is recognized. A comment containing CR therefore reads back with LF. This is
within the latitude Section 3.3 already grants — "line endings ... need not be preserved" — and it
is a property of XML rather than a choice made here.

A comment bound to a value is written immediately before that value's content, inside the element
that carries it, and a comment bound to no value is written at document level: a document-leading
comment after the XML declaration and before the document element, and a document-trailing comment
after the document element. A comment content node read from XML keeps the position its ordering
value gives it among the element and comment nodes it sits among, under Section 11.4.

Its position relative to the element's own text is not preserved. Section 11.4 exposes a lone text
run as the scalar at the element path rather than as a content node, so that run holds no ordering
value to be compared against, and the scalar is written first, ahead of every comment node the
element carries: `<a><!--c-->1</a>` emits as `<a>1<!--c--></a>`. Mixed content is unaffected,
because there every run is a content node with an ordering value of its own and the general rule
applies.

This is a limit rather than a preference, and `KNOWN-LIMITS.md` records it. Lifting it means giving
the exposed scalar an ordering value the overlay carries alongside its payload, so that a later
contribution replacing the value does not silently inherit the position of the one it replaced. The
comment, its text, its element and the value all survive the round trip; only which side of the
value the comment sits on does not.

Outside mixed content, a comment occupies its own line, indented like the elements it sits among.
Inside mixed content nothing is inserted, as stated above. Concretely: a comment whose parent also
holds text or CDATA is written exactly where it stands, with no line break or indentation added
around it, and a comment whose parent holds only elements and comments is written on its own line at
their indentation. Content containing LF is written with that LF literal and no re-indentation,
since indenting it would alter the comment.

#### XML sequence projection

XML has no anonymous sequence node. A sequence-valued mapping child therefore renders as repeated sibling elements whose expanded name is the sequence path's final element component.

For example, JSON:

```json
{"a":[1,2]}
```

with `root=cfg` renders conceptually as:

```xml
<cfg>
  <a>1</a>
  <a>2</a>
</cfg>
```

Sequence items are serialized in stable ordering-value order and are densified only in the emitted sibling order. A scalar or null item uses the repeated element's text content by default. A mapping or XML-element item uses the repeated element as its containing element and projects its fields, attributes, and children normally. A sequence-only item that has no named child element projection is `TYPE001`.

XML is a destination requiring one container shape under Section 17.1, so a node holding both a mapping and a sequence projection emits only the later container contribution and warns. The two projections are indistinguishable once written: a mapping renders as one element bearing its children, and a sequence renders as that same element repeated beside itself, under the same expanded name and in the same position. The mapping's element would therefore read as one more item of the sequence rather than as a mapping, and no reader could recover which of the siblings was which. The diagnostic is `TYPE002`, reported once per projected path and destination and carrying both `path` and `destination`.

An output view whose document root is itself a sequence requires `root` with at least two element components. The preceding components create the single document-element wrapper and the final component names each repeated item. For example, `root=cfg.item` renders a selected root sequence as one `<cfg>` containing repeated `<item>` elements. Without such a root, or with only one root component, rendering is `TYPE001`.

At a path with no scalar payload, `attribute`, `text`, and `cdata` are `TYPE001`: each names a rendering for one scalar and the path supplies none. `element` is not, because an element is what a mapping projection already emits and the directive therefore agrees with the default. `string` is not either; Section 16.6 makes it inert wherever there is no scalar to render.

At a sequence path:

- default or `element` projection emits repeated named elements;
- `text` or `cdata` is allowed only for scalar/null items with an existing containing element and emits one ordered content token per item;
- `attribute` is `TYPE001` because one XML attribute cannot represent repeated values;
- item-descendant `attribute`, `element`, `text`, and `cdata` directives apply normally inside each repeated item element.

An overlay payload plus element children is represented as text or CDATA plus children when the effective XML type permits mixed content. An attribute payload cannot coexist at the same XML address with child content; source-order shape projection selects the winning representation and warns.

Generic payload text, child elements, CDATA, and comments occupy ordered XML content-token slots by content-token ordering value, with the stable contribution key breaking any tie. A deep-merged singleton child retains its earliest token slot while later contributions update its content. Later-only tokens retain source-stream order.

Unsupported comments or metadata converted from another format are discarded with summarized warnings.

### 19.6 INI

INI output targets a conservative interoperable subset:

- UTF-8;
- one key per line;
- all global keys are emitted in one preamble before the first section, or, under `GlobalSection`, in one section named `global` written in the position that preamble would have occupied;
- global keys preserve their winning source order;
- sections follow their mapping order under Section 5.2, which for a section is the position of its first key in the emission order of Section 19.1; a section with no direct keys is not emitted;
- hoisting global keys ahead of sections is a format projection and does not change value precedence;
- no duplicate keys after merge;
- configured nested-section delimiter;
- no spaces around `=` for compatibility;
- deterministic section and key order;
- section and key names must match `[A-Za-z0-9_.:-]+` after delimiter joining;
- section and key names containing `[`, `]`, `=`, comment markers, whitespace, or control characters are errors;
- default values are unquoted single-line UTF-8 text;
- NUL, CR, and LF in a value are errors under `RejectMultiline`;
- a value beginning with `;` or `#`, or having leading/trailing whitespace, is an error unless `QuoteValues` is selected;
- `QuoteValues` emits double-quoted values, escaping `\` as `\\` and `"` as `\"`;
- `EscapeMultiline` additionally emits LF as `\n`, CR as `\r`, and tab as `\t`;
- under `EscapeMultiline`, a literal backslash is always emitted as `\\` before LF, CR, and tab escaping, whether or not `QuoteValues` is also selected;
- multiline values rejected by default unless an explicit strategy is selected.

Path projection is normative:

- a surviving scalar path with one part becomes a global key;
- for a path with two or more parts, the final part is the key and every preceding part is joined with the configured INI delimiter to form the section name;
- container-only paths do not emit keys;
- `root` is applied before this section/key split.

A null payload emits the text `null`, as in Section 19.1. `PortableIni1` has no null literal, and an empty value is a legal empty string, so spelling null as an empty value would write two distinct payloads as one line.

An overlay may emit both a scalar INI key and descendant sections when their projected identities are distinct. For example, a scalar at `a.x` and a descendant at `a.x.z` may emit key `x` in section `[a]` and key `z` in section `[a:x]`. No shape warning is emitted merely because one logical path supplies both projections. A genuine post-projection key or section collision is blocking `FLAT001`.

A section is a projection of a path prefix and not a node, so mapping order does not by itself place a section relative to a section nested beneath it. The order above resolves that: a section takes the position of its first key. The consequence is that a nested section precedes its parent whenever the parent's own keys come later in mapping order, as they do when a container child is declared before a scalar sibling. This is deliberate. INI output is a projection of the Section 19.1 emission stream, and every rule in this section is a function of that stream alone; ordering sections by the tree position of their prefix would require the INI writer to consult structure the projection has already discarded, and INI readers do not ascribe meaning to section order.

With `root=x.y`, former global keys are emitted inside section `[x:y]`; `root` parts are section-path parts rather than part of the key text.

#### Global keys and the `GlobalSection` option

A preamble is the one construct in `PortableIni1` that a widely deployed reader may refuse outright. Python's `configparser`, among others, requires a section header before the first key and raises rather than skipping the preamble, so a document with any one-part scalar path is unreadable to it in its entirety and not merely in part.

`GlobalSection` removes the preamble. Under it, global keys are written inside a section named `global`, placed where the preamble would have been — before every other section — and keeping their winning source order. Nothing else about the projection changes: the keys are the same keys, in the same order, with the same values, and the option is a decision about section framing rather than about content.

The name is fixed rather than configurable. An author who needs a particular name already has `root`, which this section already defines as placing former global keys inside a section; the difference is that `root` restructures the output view for every format the run produces, while `GlobalSection` is a projection detail of one. Offering a second, INI-only way to spell a section name would make two directives answer the same question differently.

The name is not `DEFAULT`. `configparser` gives `[DEFAULT]` a defined special meaning — its keys are inherited by every other section on read-back — so a file written under that name to be readable would read back carrying keys in sections that never declared them. A name chosen for compatibility that changes the document's meaning in the reader it was chosen for is worse than the preamble it replaces.

A path that already projects to a section named `global` collides with the hoisted section, and the collision is blocking `FLAT001`. The two have different origins — one is a set of one-part scalar paths, the other a path of two or more parts — and silently merging them would make the document's content depend on a name this specification chose rather than on anything the author wrote. `GlobalSection` is opt-in, so the author who meets this has both the option and the section in view. The diagnostic names the first path that projects to that section in the order Section 19.6 fixes, so that a document with several has one stated place to start.

When no global key survives, `GlobalSection` writes nothing: there is no empty `[global]` header, for the same reason Section 19.6 does not emit a section with no direct keys.

An INI destination that writes a preamble emits `WARN012` once per output instance, naming the destination. The preamble is produced by ordinary input rather than by an option the author selected, so without the warning nothing in the run would tell them that the file they just wrote may be unreadable where they intend to use it. Selecting `GlobalSection`, or configuring `root`, removes the preamble and the warning together. A document with no global keys never had a preamble and never warns.

When the selected output root is a bare scalar, INI retains the final concrete selector part as a global key. `root` places that key in a section without altering it, under the rule above that `root` parts are section-path parts rather than part of the key text: with `root=x.y` the key remains `k` and moves into `[x:y]`.

This dialect is named `PortableIni1`. Consumers must opt into `QuoteValues` or `EscapeMultiline` only when their parser recognizes those escapes. An implementation's compatibility documentation names the parsers it holds itself interoperable with, and conformance tests must cover every parser it names. Naming none is a permitted and complete answer: it states that the dialect is verified against this specification and against no external reader. The documentation must say so explicitly rather than leaving the list absent, because an absent list and an empty one are not the same claim — a reader who takes silence for "the usual parsers" will choose options on an assumption the implementation never made.

#### INI output bytes

The rules above fix which lines are written and in which order. Their layout on the page is fixed here, because blank lines and alignment are the decisions an INI writer is most likely to make for readability and least likely to make identically to another writer, and Section 24 requires two implementations to agree byte for byte.

- A key line is the key text, `=`, and the value text, with no space on either side of `=`, as stated above.
- A section header is `[`, the joined section name, and `]`, alone on its line.
- A comment line, when `inioutputoptions` enables comments, is the marker selected by `SemicolonComments` or `HashComments`, one space, and the comment text; where the text is empty the separating space is not written either, under Section 24's rule that no line ends in whitespace. It immediately precedes the line it is attached to, with no blank line between them.
- **No blank lines are written anywhere**: not between the global preamble and the first section, not between one section's last key and the next section header, and not around comments.
- Every line ends with one LF, and the file ends with exactly one LF under Section 24.

A blank line before each section header is the more conventional layout and every INI parser ignores it, so the choice here is presentational rather than semantic. It is made in favour of writing nothing, because a rule that writes no blank lines has no edge at the first section, no edge at an empty preamble, and no interaction with the comment rule, while a rule that writes one has three. An implementation that finds the output hard to read may not add spacing: the bytes are the contract.

## 20. Comments across formats

Comments are preserved when both the source and destination support the common comment association:

| Destination | Comment behavior |
|---|---|
| namespace | Emit normalized leading `#` comments. |
| quotednamespace | Emit normalized leading `#` comments. |
| YAML | Emit normalized leading, inline, and trailing comments where representable. |
| XML | Emit ordered XML comments where representable. |
| JSON | Discard with summarized warning. |
| INI | Emit only when enabled by `inioutputoptions`; otherwise discard with summarized warning. |

INI comments are emitted as full-line comments. A comment attached to a global key is emitted immediately before that key. A comment attached to the first direct key in a section is emitted after the section header and before that key. Comments attached to later keys are emitted immediately before those keys. An inline comment is normalized to a full-line comment immediately before the key it was attached to. Document-leading comments precede the first global key or section. Document-trailing comments are emitted at end of file, after the final key of the final section.

A comment attached to a container-only path is emitted immediately before the first key the projection emits beneath that path, as Section 19.1 requires. When that key is the first direct key of a section, the comment therefore follows the section header rather than preceding it. INI is given no special case hoisting such a comment above its header, even though the header is a line this format does emit for that path: as with section ordering below, every rule here is a function of the Section 19.1 emission stream alone, and a section is a projection of a path prefix rather than a node, so the writer has no container to attach a comment to. The comment still introduces the section's first key, which is the association these formats can represent.

A document-trailing comment is the only one INI cannot place by looking forward, because by the definition below nothing follows it. It is emitted at end of file rather than folded into the nearest preceding key, for three reasons: source order is preserved, the position agrees with namespace, quoted namespace, and YAML output for the same input, and a note the author wrote last is not silently reattached to a value it does not describe. A full-line comment at end of file needs no key to own it, so the placement costs INI nothing it can otherwise represent.

Cross-format comment association follows source order:

- a comment before the first payload or item is document-leading;
- a comment between two payloads or items becomes a leading comment of the following payload or item;
- a comment after the final payload or item is document-trailing;
- inline YAML comments remain attached to their payload;
- when several source documents merge, document-leading comments precede that source's first surviving contribution and document-trailing comments follow its final surviving contribution.

Converting a *value-bound* comment between formats uses these associations: a namespace, INI or YAML comment reaching an XML destination becomes an XML comment adjacent to the value it is bound to, and a YAML inline comment reaching a flat destination becomes the full-line comment described above. The conversion runs into XML and between the non-XML formats, and never out of XML, because Section 11.5 leaves XML no value-bound comment to convert: every comment an XML source contributes is an ordered content node.

An XML comment therefore does not convert. Section 4.5 keeps it an ordered content node that is "not reassigned to adjacent values", so it has no value association for the rules above to carry, and no non-XML destination can place it. It is therefore discarded by every non-XML destination, with the one summarized `WARN003` per output file and feature category that Section 7 requires of any discarded source concept. This is the asymmetry the model implies rather than an omission: a comment that was never bound to a value cannot acquire a binding by being written somewhere else, and inventing one would attach the author's note to whichever value happened to follow it.

One consequence is worth stating, because Section 19.4 and Section 19.6 would otherwise look incomplete. An XML comment is the only comment whose text may contain a line break: Section 8.1 rule 2 makes a namespace or INI comment a single record, which is a single line, a YAML comment ends at end of line, and two adjacent comment lines are two comments rather than one comment of two lines. Since no XML comment reaches a non-XML destination, multiline comment text cannot reach YAML or INI output, and neither section states a rule for splitting it — there is no case for such a rule to govern, and a rule written for an unreachable case is one no fixture can hold to account. The rule below states the split for namespace and quoted-namespace output anyway, because there it is a safety property rather than a rendering choice, and a safety property should not rest on an argument about reachability.

Exact lexical spacing is not guaranteed across a conversion. What each destination writes is not thereby left open: the comment bytes of every format are fixed in Section 19, because Section 24 requires two implementations to agree on them.

For namespace and quoted-namespace output, comment text is normalized to LF and every physical line is prefixed independently with `#` and one space, the space being omitted on a line whose text is empty under Section 24's rule that no line ends in whitespace. Prefixing each physical line independently means a multiline source comment can never introduce an executable shell assignment or an uncommented namespace entry. NUL is rejected.

When rendering a non-XML comment as XML, invalid XML comment sequences are normalized deterministically: every `--` is separated as `- -`, and a terminal `-` receives one trailing space.

## 21. Output planning, paths, and publication

### 21.1 Output-root confinement

Every output path must remain inside `--output`.

If at least one destination is planned and the configured output root does not exist, it is included as the first directory in the validated creation plan and is created only after the global validation gate. A zero-destination plan does not create it. An existing non-directory output root is `PATH001`.

The implementation must:

- reject rooted, drive-absolute, drive-relative, UNC, device, and extended-length `filename` forms, including `C:\x`, `C:x`, `\\server\share`, `\\?\`, and `\\.\`;
- normalize platform separators;
- reject `.` and `..` path segments after filename expansion;
- reject canonical paths outside the output root;
- open and publish through handle-relative or equivalent no-follow filesystem operations;
- verify symbolic-link, junction, and reparse-point containment when opening each destination;
- fail with `PATH001` before creating directories or opening destinations if the host platform or filesystem cannot provide the primitives needed to establish secure containment;
- create required directories only after validation.

Users requiring unrelated output roots should invoke the tool separately or choose a common parent output root.

### 21.2 Global validation gate

Before opening or truncating any destination, the tool must:

1. complete pipeline steps 1 through 18 in Section 15.1;
2. serialize every planned output completely into immutable in-memory byte buffers;
3. validate every final path and create the complete deterministic directory plan.

Any parsing, transformation, reference, scheme, collision, or serialization error therefore prevents all destination changes.

Output byte buffers consume `--max-total-output-bytes`. This version does not use temporary output files.

The configured output-byte ceiling is also the upper bound on aggregate live serialized buffer payload, excluding implementation overhead. Implementations may document a lower hard safety ceiling and reject a larger CLI value as `CLI001`; they must not silently stream or spill after the validation gate because that would change the specified failure model.

### 21.3 Direct publication

After the validation gate:

- order distinct destinations by the minimum §17.5 fold key among contributions whose data or retained destination high-water state survives into the final folded plan, then by canonical relative path compared as unsigned UTF-8 bytes. Same-format `replace` preserves prior high-water state and therefore its earlier publication key; a cross-format replacement discards the complete prior plan and resets the key to the replacing contribution;
- create each destination's missing parent directories immediately before that destination, ancestor first, using the same path comparator for ties;
- write destinations in that destination order;
- create or truncate each destination only after its complete byte buffer exists;
- flush and close each destination before beginning the next one.

An external storage failure is reported immediately and returns exit code `1`.

No rollback is attempted. Files already completed remain updated; the failing destination may be partial; later destinations remain untouched.

The output root is considered semantically owned by one CLI invocation during publication, like a compiler output directory. Concurrent writers or unrelated mutation of that root are outside the supported execution contract.

The tool makes no atomic-publication guarantee. Its guarantee is instead that all semantic work and serialization complete before the first destination is opened.

### 21.4 Existing files

Replacing an existing destination is allowed and is logged at information level.

Replacing or merging because of two output declarations emits a warning as described under file collisions.

## 22. Diagnostics

Every diagnostic must include, where applicable:

- severity;
- stable diagnostic code;
- pipeline phase, as enumerated in Section 6.4.3;
- specification anchor for the clause being enforced;
- source file;
- line and column;
- qualified path;
- declaration or wildcard rule;
- destination path;
- concise explanation.

Line and column, where present, are measured over the decoded character stream defined by Section 7.4, after any byte-order mark has been removed. Line 1 is the first line. A line is terminated by LF, CRLF, or a lone CR, and by nothing else; consistently with Section 8.1, U+0085, U+2028, and U+2029 do not terminate a line. Column 1 is the first Unicode scalar value of a line, and each subsequent scalar advances the column by one, so a character outside the Basic Multilingual Plane occupies one column and a tab occupies one column. A condition with no position in a source omits line and column rather than reporting a default.

The `rule` member is an array of Appendix A canonical wildcard-rule names, holding one element per rule the condition holds responsible, in the Section 12.4 source order of those rules. It carries names and nothing else: a condition that already locates its rule through `source`, `line`, and `path` omits `rule` rather than restating them, and a condition that reports one rule carries a one-element array. The member is an array rather than a joined string because an Appendix A component may contain any ordinary scalar, including a comma and a space, so no textual separator would be unambiguous.

**Which members a condition supplies.** The field list below is the set of members a *code* may carry. A code covers several conditions, and they do not all carry the same members, so the list alone does not determine any one diagnostic. Each member is supplied when the condition itself has the fact that member names, and omitted otherwise:

- `source` — the condition arises at one identifiable input or scheme file, and names that file. A condition arising inside a command-line variable supplies no `source`, and therefore no `line` or `column`, for the reason Section 8.1 gives: a variable is not a file, and a synthetic name in this member would be indistinguishable from a real one. Such a condition identifies the variable in its `message` instead, by the variable's one-based position in `-v` token order;
- `line` — the condition further names one physical record within that origin;
- `column` — the condition further names one position within that record. A condition about a whole record, such as one raised over a compiled declaration or a wildcard rule rather than over the text that produced it, supplies `line` without `column` rather than inventing a precision it does not have;
- `path` — the condition concerns one overlay node or one projected output key, and names its Appendix A canonical spelling;
- `declaration` — the condition concerns one scheme declaration, and names its canonical spelling;
- `destination` — the condition concerns one planned or published output file;
- `rule` — as defined above.

Note that the cardinality column does not answer this question. Cardinality states how many occurrences of a code one run may report, not what identifies one: `PARSE001` is counted once per failing source and still names the record that failed, while `LIMIT001` and `WILDCARD002` are counted once per invocation and still name what crossed the bound.

Where a cardinality is stated per source, the unit is one `-i`, `-s`, or `-v` occurrence and not one distinct path. A path written twice is two sources under Section 4.7, each with its own ordinal and its own contribution, so each may report the code once. Counting per path would let one source's occurrence displace another's, and the displaced occurrence can differ in `phase` — the same path supplied once as an input and once as a scheme fails in both phases — which would make the stream report a condition in one phase while silently dropping it in another.

The tie-breaker, where a condition could be attributed to more than one place, is that these members name **where the condition is, not where its subject came from**. A condition raised over the merged model, an output plan, or the invocation as a whole therefore supplies no `source`, even where a contributing entry could be traced back to one and even where doing so would be helpful. A `merge=error` conflict at a path is a property of that path after folding, so it supplies `path` and no `source`; the same holds for the Section 8.7 concatenation warning. Attributing such a condition to one of its contributions would make the diagnostic depend on which contribution the implementation happened to hold, which Section 24 forbids.

A member this rule does not reach is omitted rather than defaulted, and `column` additionally requires `line`.

A cardinality that admits fewer records than the run detects must also say which occurrence is reported. The surviving occurrence is the one Section 24 orders earliest, and the rest are suppressed rather than merged: an occurrence names one place, and a record combining two of them would name neither. Where Section 24 does not separate them — the same phase, the same ordering key or destination, the same code, and the same path, which is what an invocation-wide or source-wide cardinality typically produces — the survivor is the one detected first in the traversal that phase specifies. Command-line parsing traverses arguments left to right under Section 6, so an invocation carrying two invalid option values reports the leftmost, and reports the other only once the first is corrected.

Selecting by Section 24 rather than by arrival makes the survivor a property of the input. A phase that works concurrently must therefore reach the same record as one that does not, which it cannot do by holding a single shared slot and letting the first worker fill it, and this is a difference no fixture would reveal on a machine fast enough to be consistent about which worker that is.

Blocking errors include:

- malformed namespace syntax;
- malformed or unterminated reference;
- unknown scheme directive or value;
- undefined explicit wildcard capture;
- mixed explicit and legacy captures in one rule;
- reference cycle;
- missing reference;
- unsupported non-scalar text concatenation;
- insecure or unsupported XML DTD;
- invalid output path;
- invalid XML name or unbound namespace;
- invalid shell identifier;
- unsupported INI value under the chosen dialect;
- destination collision rejected by `filemerge=error`;
- resource-limit violation;
- output rendering or publication failure.

Warnings include:

- missing input or scheme file;
- validated output plan contains zero destinations;
- deprecated alias;
- unsupported metadata discarded during cross-format conversion;
- output shape conflict resolved by later precedence;
- inferred sequences concatenated without an explicit merge directive;
- output destination collision;
- cross-format file override;
- processing instruction discarded;
- normalized XML whitespace discarded;
- native JSON/YAML numeric mapping inferred as a sequence;
- scheme directive binds to no concrete output instance or survives only beneath an ignored ancestor.

Warnings do not change the success exit code.

The normative diagnostic registry is:

| Code | Severity | Condition | Cardinality |
|---|---|---|---|
| `CLI001` | error | Invalid command line or option value | once per invocation |
| `PARSE001` | error | Malformed namespace, JSON, YAML, XML, or scheme syntax | once per failing source |
| `PARSE002` | error | Invalid or unsupported character encoding | once per failing source |
| `SCHEME001` | error | Unknown directive, value, or illegal option/type combination | once per declaration |
| `SCHEME002` | error | Ambiguous canonical/simple scheme path | once per expanded declaration |
| `WILDCARD001` | error | Invalid, undefined, or mixed capture outside a reference | once per rule |
| `WILDCARD002` | error | Nonterminating expansion or wildcard limit | once per invocation |
| `REFERENCE001` | error | Malformed or free-wildcard reference | once per owning value |
| `REFERENCE002` | error | Missing reference | once per reachable owning value |
| `REFERENCE003` | error | Reference cycle | once per canonically distinct reachable cycle |
| `REFERENCE004` | error | Ambiguous reference alias | once per reachable owning value |
| `REFERENCE005` | error | Non-scalar reference target | once per reachable owning value |
| `TYPE001` | error | Invalid shape, input merge conflict, root removal, or transformation target | once per path and applicable source/output instance |
| `TYPE002` | warning | Shape conflict resolved by precedence | once per projected path and destination |
| `FLAT001` | error | Distinct logical paths collide after output projection or normalization | once per projected key and output instance |
| `SHELL001` | error | Invalid quoted-namespace shell identifier | once per projected key and output instance |
| `XML001` | error | DTD, external entity/resource, or prohibited XML feature | once per failing document |
| `XML002` | error | Invalid XML name, namespace, declaration, or canonical address | once per failing node or document |
| `INI001` | error | Value or name unsupported by `PortableIni1` options | once per path and output instance |
| `COLLISION001` | error | `filemerge=error` rejects a second contribution to one destination | once per rejected contribution after the first |
| `SERIALIZE001` | error | Output view cannot be serialized under the selected format/options | once per output instance |
| `PATH001` | error | Invalid, escaping, or insecure output path | once per destination |
| `PATH002` | error | Publication/open/write/flush failure | once, for the failing destination |
| `LIMIT001` | error | Non-wildcard resource limit exceeded | once per invocation |
| `WARN001` | warning | Missing input or scheme file | once per missing-file occurrence on the command line |
| `WARN002` | warning | Deprecated alias | once per alias category and scheme |
| `WARN003` | warning | Unsupported metadata/comment discarded | once per feature category and output file |
| `WARN004` | warning | Native implicit sequences concatenate without explicit merge | once per sequence path |
| `WARN005` | warning | Output destination collision or cross-format override | once per folded contribution pair |
| `WARN006` | warning | Processing instruction discarded | once per input document |
| `WARN007` | warning | XML formatting whitespace discarded | once per input document |
| `WARN008` | warning | Output plan contains no destinations | once per invocation |
| `WARN009` | warning | Scheme directive binds to no concrete output instance or path, wildcard output creates no instance, or a concrete output instance selects nothing | once per declaration or expanded directive |
| `WARN010` | warning | Native JSON/YAML numeric mapping remains inferred as sequence in an output view | once per source contribution, canonical mapping path, and output instance |
| `WARN011` | warning | Later unmarked contribution aliases an existing XML component instead of overriding it | once per canonical path |
| `WARN012` | warning | INI output emits a global-key preamble, which a reader requiring a section header will refuse | once per output instance |

`TYPE001` includes a bare scalar selected for XML without a configured `root`, and a bare scalar selected by the empty root selector for namespace, quoted namespace, or INI, which in that case have no concrete selector part to supply a key. `FLAT001` covers namespace, quoted-namespace, and INI post-projection key collisions, and JSON and YAML mapping-key collisions. Ordering-value overflow and every configured non-wildcard resource-bound violation are `LIMIT001`, matching that code's registry condition and Appendix B row; a wildcard fixed-point, candidate, generated-node, or iteration bound is `WILDCARD002`. Malformed limit option values are `CLI001`. `SERIALIZE001` is used only before publication, while an open, write, or flush failure after the validation gate is `PATH002`.

A reference cycle is identified by its ordered ring of canonical paths, independent of discovery entry point. Rotate the ring so its lexicographically smallest canonical path under unsigned UTF-8 byte order is first; when the same smallest path appears more than once, choose the lexicographically smallest resulting rotated sequence. Report the chain from that canonical start and close it by repeating the first path.

Codes, severities, cardinalities, and ordering fields are compatibility-stable. Information, debug, and trace operational messages are not diagnostics and are outside this registry; they still write to stderr when enabled. When one condition qualifies for several entries, emit the most specific code only. Diagnostics are ordered under Section 24 by phase, source/order key or destination fold key, code, and canonical path. Localized message prose is not part of byte-identical determinism.

For XML processing instructions and discarded formatting whitespace, `WARN006` and `WARN007` respectively are more specific than `WARN003`; do not emit both for the same discarded feature occurrence.

Severity, cardinality, and the enumerated fields are properties of the code. Pipeline phase and specification anchor are properties of the individual occurrence, because `TYPE001`, `SERIALIZE001`, and `LIMIT001` each arise in more than one phase and enforce more than one clause.

The anchor is determined rather than chosen. An occurrence carries the numbered clause that *states* the rule it enforces, spelled at the deepest numbering the specification gives that statement. A clause that cites the rule, restates it for one format, or describes what the rule produces is not the anchor: `type=multiline` applied to a mapping is `TYPE001` at Section 16.6, which says what `multiline` may be applied to, and not at Section 19.4, which says only how YAML writes a block scalar. Where a rule is stated at section level and that section has no numbered subdivision stating it, the anchor is the section.

This is required rather than advisory because `spec` is a structured field, and Section 24 makes structured fields identical across conforming implementations. An anchor whose clause or granularity were left to the implementation could not meet that requirement, and Appendix C.4 would conceal the disagreement rather than detect it, because it compares `spec` only where a fixture declares one. The registry is silent on the anchor for the same reason it is silent on the phase: both are properties of the condition, and the registry records the code.

`REFERENCE001` covers two conditions that are detected in different phases, and its cardinality is
written "once per owning value" rather than "once per reachable owning value" for that reason.
Malformed or unterminated reference *syntax* is rejected by Section 15.1 step 6, in the input phase,
before any output instance exists; reachability is therefore not yet defined and cannot narrow it,
and such a value is `REFERENCE001` whether or not any output would have selected it. A free wildcard
or free capture in an otherwise well-formed reference is a planning-phase failure, and Section 13.3
scopes it to reachable owning values under Section 14.4. The other `REFERENCE00x` codes are resolved
at step 15 and so are uniformly reachability-scoped.

A conforming distribution must ship a machine-readable diagnostic registry alongside the specification text. The registry is authoritative for exactly the code-level facts enumerated above: code, severity, cardinality, and the set of fields the code may carry. It is not authoritative for phase, anchor, or message prose, and it may not introduce a code that Section 22 and Appendix B do not define. Where the registry and this section disagree within that domain, the registry is a defect and this section governs until the registry is corrected.

The registry is a JSON document at `spec/diagnostics.registry.json`, relative to the root of the distribution, encoded in UTF-8 with no byte-order mark, indented two spaces per nesting level, and terminated by exactly one LF. Its root object carries `specification`, naming the covered specification text by path and by the lowercase hexadecimal SHA-256 of its bytes; `authoritativeFor` and `notAuthoritativeFor`, listing the facts named in the paragraph above; and `codes`. `codes` is an array holding one object per code, in the order this section's table lists them rather than in any collating order, so that a reader can follow the two side by side. Each entry carries `code`, `severity`, `cardinality`, `condition`, `fields`, and `mappings`: `severity` is `error` or `warning`, `cardinality` and `condition` reproduce this section's table cells, `fields` names the Section 6.4.3 members the code may carry, and `mappings` names the Appendix B conditions that select it. A distribution may ship a JSON Schema for the registry beside it and reference it from a `$schema` member; that member is descriptive and does not extend the registry's authority.

Fixing the path, the member names, and the order is what makes the registry an artifact two implementations can exchange rather than a private file that happens to be published. A consumer that must discover the filename, or guess whether `codes` is sorted, is reading this distribution rather than the contract.

The specification text and the registry together form one versioned contract bundle, described by a JSON document at `spec/contract-bundle.json` under the same encoding, indentation, and termination rules. It names each covered artifact by distribution-relative path together with the lowercase hexadecimal SHA-256 of its bytes, so that a consumer can verify a claimed contract against the artifacts themselves instead of trusting the identifier that names it.

The bundle's revision identifier is the letter `r`, a decimal counter, `+`, and the first twelve lowercase hexadecimal characters of the SHA-256 of the UTF-8 bytes formed by the specification digest, one LF, and the registry digest. The digest component is the identity: two distributions whose covered artifacts are byte-identical must report the same digest component, and a consumer deciding whether two binaries implement the same contract compares that component and nothing else. The counter orders revisions within one lineage and has no meaning across distributions, which is the honest description of a number obtained by incrementing the previous one; a distribution with no predecessor starts at 1.

Deriving the identity from the artifacts rather than assigning it is what makes the identifier checkable. An identifier a distribution chooses can be reused for changed artifacts, or differ between two builds of the same ones, and a consumer holding only the string has no way to notice either.

`--version` reports the whole identifier as its `contract-bundle` field, so that a consumer, including an automated one, can determine exactly which contract a given binary implements and can cite it when reporting a defect.

## 23. Complexity and resource limits

Let:

- `N` be parsed source nodes and entries;
- `P` be total qualified-path length;
- `E` be reference edges;
- `M` be successful wildcard matches;
- `C` be wildcard `(rule,item)` candidate checks;
- `G` be generated nodes;
- `B` be rendered output bytes.

Ordinary parsing, merging, filtering, reference graph construction, and output planning should be `O((N + P + E) log N + C)` or better, excluding work proportional to independently requested output copies.

Wildcard evaluation may add `O(M + G)` work after indexed candidate discovery. Rendering may add `O(B)`. Scalar reference resolution adds `O(E)` plus emitted scalar text length. No requirement can make work asymptotically smaller than the produced output.

The implementation must avoid an unconditional `O(n²)` scan. Each `(rule,item)` wildcard candidate is tested at most once. Shape maxima and winning contributions are maintained incrementally rather than recomputed by rescanning complete subtrees after removal.

Recommended structures include:

- ordered hash maps or balanced maps for ordered mappings;
- qualified-name tries for prefix selection;
- dependency graphs for references;
- fixed-prefix or segment indexes for wildcard candidate selection;
- ordering-value maps with `O(log n)` or better lookup and patching;
- per-node ordered contribution indexes for winning shape and comment metadata;
- destination-path maps for collision planning.

Wildcard rules may use more expensive candidate matching because complex rules are expected to be rare, but the implementation must:

- index by literal prefix where possible;
- expose expansion counters in debug diagnostics;
- enforce configurable limits;
- fail explicitly rather than degrade without bound.

Reference closure work is bounded by the sum of entries and edges actually visited across output-instance closures. Destination folding must be linear in the total size of its contribution models plus produced output size; strict left-fold semantics do not require repeatedly copying the accumulated model.

Limits are configured by the CLI options in Section 6.2. Concurrent work never races to consume a shared budget.

- `--max-input-bytes` applies independently to each input file, scheme file, and command-line variable after encoding a variable as UTF-8;
- `--max-total-input-bytes` includes all scheme files in `-s` order, then all input files in `-i` order, then command-line variables in `-v` token order, matching pipeline consumption;
- document/path depth is checked per source, at parse time, against the paths the source itself supplies;
- XML attributes are checked per element within each source under `--max-xml-attributes`, as specified in Section 11.1;
- parsed node, comment, and comment-byte totals are accumulated at the parse-phase join in CLI source order as specified in Section 7.3;
- wildcard, reference, output, and serialization budgets are consumed in their normative pipeline order.

The source or operation that first crosses a cumulative limit is therefore independent of worker scheduling.

`--max-depth` is a parse-time guard on what a source may make the tool hold, not a constraint on the shape of what it writes. Paths produced later — by wildcard generation, by `key`, or by a `root` that wraps the selected view in further levels — are not checked against it, and a `root` of four parts legitimately writes a document deeper than `--max-depth` allows a source to be. The limit exists to bound the cost of parsing untrusted input, and that cost is already bounded once the source is parsed; re-checking generated paths would make a scheme author's `root` fail on a limit describing input they did not write, and would couple two settings that answer different questions.

`--max-generated` counts newly materialized overlay nodes, including carrier containers required for a generated descendant. A generated contribution targeting only already-existing nodes consumes no generated-node count.

Accounting occurs before allocation or expansion whenever possible. XML node creation, wildcard generation, comments, and serialized output buffers all consume the corresponding global budget.

Configurable limits include:

- total input bytes;
- nesting depth;
- XML attributes per element;
- node count;
- comment count and size;
- wildcard rule count;
- generated entry count;
- wildcard iterations;
- reference depth;
- output count;
- output bytes.

## 24. Determinism requirements

Given identical:

- input bytes;
- CLI argument order;
- scheme bytes;
- environment-independent options;
- tool version;

the tool must produce byte-identical output files. Diagnostic codes, severities, structured fields, and ordering must be identical; localized human-readable prose may differ.

All text outputs:

- use UTF-8 without a BOM;
- use LF as the physical line terminator;
- end with exactly one LF;
- contain no line ending in a space or a TAB.

The last of these is not presentational. Trailing whitespace is invisible in every editor, is stripped silently by many of them and by a good deal of tooling, and would therefore be the one class of byte in a specified output that a consumer could destroy without noticing — precisely the outcome the byte rules exist to prevent. It binds the writer only where a rule would otherwise produce it, and there is exactly one such place: a comment whose text is empty is written as its marker alone, with no separating space, in every format that emits comments. A block scalar's content lines are exempt in the only sense that matters, because Section 19.4 does not select the block form for a value whose lines end in whitespace.

A text output with no content is zero bytes and satisfies these rules vacuously. The termination rule applies to output that has content, so a single LF is never emitted merely to terminate nothing.

XML's declaration is `encoding="utf-8"`.

Diagnostics produced during concurrent work are buffered and emitted by pipeline phase, then by source ordering key within that phase. Scheme-loading diagnostics therefore precede input-parsing diagnostics, followed by transformation/output-planning diagnostics and publication diagnostics.

A diagnostic's ordering key is the Section 4.7 stable ordering key of the item it concerns. A diagnostic that concerns a source but no individual item carries the key whose CLI source ordinal is that source's and whose remaining components are zero, so it precedes every item of that source. A diagnostic about a conflict between two contributions at one path concerns the later of them: that is the contribution whose arrival made the earlier one insufficient, and the one an author edits to resolve it. Keying such a diagnostic at the earlier contribution would report a later source's mistake at an earlier source's position. Within one phase the order is:

1. diagnostics carrying a source ordering key, in that key's Section 4.7 comparison order;
2. then diagnostics carrying a destination but no source ordering key, in the Section 21.3 destination order;
3. then diagnostics carrying neither.

Within any one of those three groups, remaining ties are broken by diagnostic code compared as unsigned UTF-8 bytes, then by qualified path compared as unsigned UTF-8 bytes, with an absent path sorting before any present path. The Section 22 cardinalities leave no further tie to break: two occurrences agreeing on phase, ordering key, destination, code, and path are one occurrence.

A diagnostic whose Section 22 cardinality counts per output instance or per destination carries a destination and no source ordering key, and is therefore ordered by group 2 even when the condition it reports is visible at one item. Section 14.1 lets nested output declarations select overlapping data, so one item can reach several destinations and fail in each; keying those occurrences at the item would make them tie on phase, ordering key and code, leaving only the qualified path to separate them. That path is expressed in the output's own frame — it is the path after `root`, `key` and the instance's prefix filtering — so it is not a source coordinate and comparing it across two destinations orders nothing meaningful. It can also invert Section 21.3: an item reported as `k` in the second destination written would precede the same item reported as `x.k` in the first. Grouping such diagnostics by destination in publication order keeps each output's report contiguous and in the order the outputs are produced.

Results must not depend on:

- thread scheduling;
- process hash randomization;
- filesystem enumeration order;
- current locale;
- current time zone;
- operating-system line ending;
- dictionary implementation;
- installed XML catalog;
- network availability.

Logical line endings inside scalar data are LF. Serializers escape or encode them according to their format.

## 25. Backward-compatibility examples

### 25.1 Value override

Input 1:

```text
a.x=1
```

Input 2:

```text
a.x=2
```

Result:

```text
a.x=2
```

### 25.2 Output ignore override

Scheme:

```text
a*.output=xml
a0.output=ignore
```

Outputs XML for matching roots except `a0`.

### 25.3 Array concatenation

Input 1:

```yaml
a:
  - x: 1
  - x: 2
```

Input 2:

```yaml
a:
  - x: 3
```

Result:

```yaml
a:
  - x: 1
  - x: 2
  - x: 3
```

### 25.4 YAML wildcard enrichment

Input 1:

```yaml
a:
  - b: 1
  - b: 2
```

Input 2:

```yaml
a:
  '*':
    c: XXX
```

Result:

```yaml
a:
  - b: 1
    c: XXX
  - b: 2
    c: XXX
```

### 25.5 Typed reference

JSON input:

```json
{"port": 5432}
```

Namespace input:

```text
copy=${port}
label=port-${port}
```

JSON output contains:

```json
{
  "port": 5432,
  "copy": 5432,
  "label": "port-5432"
}
```

### 25.6 Strict literal reference-looking text

Input:

```text
pattern=\${yyyy-MM-dd SEVERITY MESSAGE}
```

Resulting scalar:

```text
${yyyy-MM-dd SEVERITY MESSAGE}
```

### 25.7 Uniform root

Scheme:

```text
a.output=json,yaml,xml,namespace
a.root=config.application
```

All four outputs contain the equivalent `config.application` wrapper path.

### 25.8 Scalar and descendant overlay

Input, in this source order:

```text
a.x=1
a.*.z=3
```

Namespace output preserves both facts:

```text
a.x=1
a.x.z=3
```

JSON and YAML cannot represent both shapes. Because the wildcard rule is later, the object shape wins:

```yaml
a:
  x:
    z: 3
```

The omitted scalar produces one shape-conflict warning. If `a.x=1` appears after the wildcard rule, the scalar shape wins instead.

### 25.9 Explicit ordering values and mapping escape

Input:

```text
a.0=b
```

Default YAML output treats the nonempty all-numeric mapping as an explicitly indexed sequence:

```yaml
a:
  - b
```

With:

```text
a.type=mapping
```

YAML output preserves the ordering value as a mapping key:

```yaml
a:
  '0': b
```

Conversely, `type=array` converts an ordered mapping with nonnumeric keys to a sequence in mapping order.

### 25.10 Logical-path comments across override

Input:

```text
z=0
# comment for a
a=1
b=2
a=2
```

Namespace output is:

```text
z=0
b=2
# comment for a
a=2
```

The comment remains bound to logical path `a`, and the winning `a` contribution determines their final position.

The leading `z=0` is load-bearing. Section 8.5 makes the comments preceding a source's *first* entry
document-leading and bound to no path, so a comment written at the very top of this profile would
describe the file and would not move with `a` at all. Writing one entry ahead of it is what Section
8.5 means by "a source whose first entry needs a comment of its own must be written with that entry
second", and without it this example would demonstrate the opposite of the rule it is here to show.

### 25.11 Permanent ignore mask

Input:

```text
b.p=0
b.*.z=9
!b.p.z
```

The generated `b.p.z` is suppressed. A later concrete or generated `b.p.z` remains suppressed for the rest of the run.

### 25.12 Stable matching versus dense rendering

Input:

```text
a.0=zero
a.1=one
a.2=two
!a.1
```

The surviving stable ordering values are `0` and `2`. A scheme directive or reference targeting the second surviving item must still use stable path `a.2`; `a.1` remains permanently masked and is not retargeted.

Namespace or INI sequence projection may render the surviving items densely as visible positions `0` and `1`. Dense output positions are serialization artifacts and never become new scheme, wildcard, or reference addresses.

### 25.13 XML sequence projection

Input:

```json
{"a":[1,2]}
```

Scheme:

```text
output=xml
root=cfg
```

Output:

```xml
<?xml version="1.0" encoding="utf-8"?>
<cfg>
  <a>1</a>
  <a>2</a>
</cfg>
```

If the selected output value itself is the sequence, `root=cfg.item` creates one `<cfg>` document element and repeated `<item>` children. `root=cfg` alone is insufficient because it would not provide distinct wrapper and item names.

### 25.14 XML singleton promotion

One XML child `<a><b>one</b></a>` exposes element/scalar path `a.b`. After another contribution adds a second `<b>`, canonical paths become `a.b.0` and `a.b.1`. The old `a.b` path is not silently redirected to either item.

## 26. Acceptance requirements

An implementation is conforming only when automated black-box tests cover:

1. CLI behavior and exit codes.
2. Case-insensitive file extensions.
3. Missing-file warnings.
4. Stable CLI/file/line precedence.
5. Namespace escaping and strict references.
6. Legacy and explicit wildcard captures.
7. Fixed-point expansion and expansion limits.
8. Strict output-prefix matching.
9. Typed scalar references, canonical and unique format-agnostic reference addressing, ambiguity errors, and string concatenation.
10. Winning-source mapping order and logical-path comment movement across overrides.
11. Sequence concatenation across every input format.
12. YAML wildcard enrichment across files.
13. YAML comment input and output.
14. JSON strict parsing and absence of comments.
15. XML DTD rejection and external-resource prohibition.
16. XML namespaces, attributes, text, mixed content, comments, CDATA, and whitespace modes.
17. Cross-format metadata-loss warnings.
18. Every scheme directive and deprecated alias.
19. Source-order scheme precedence, including wildcard then specific and specific then wildcard.
20. Negative `output=ignore`.
21. Uniform `root`.
22. Output-neutral `key`.
23. Locale-independent scalar inference.
24. JSON, YAML, and XML input structural merge.
25. Array concatenation, explicit-index patching, and input `merge=replace`.
26. Deterministic same-path `filemerge` and cross-format complete-plan overrides.
27. Portable shell quoting and invalid identifiers.
28. The documented INI dialect against representative parsers.
29. Output-root path confinement, including symlink or reparse-point escape attempts.
30. Global semantic/serialization validation gate and deterministic direct publication.
31. Byte-identical repeated runs under different thread schedules and locales.
32. Complexity bounds expressed in `N`, `P`, `E`, `M`, `C`, `G`, and `B`, verified through deterministic instrumentation of node visits, candidate checks, reference-edge visits, and produced bytes rather than wall-clock thresholds.
33. Scalar-plus-descendant overlays across namespace, JSON, YAML, XML, and INI.
34. Permanent ignore masks across concrete input, wildcard generation, references, selectors, and comments.
35. Stable ordering-value matching after cross-file concatenation.
36. Concrete output-instance expansion and wildcard default filenames.
37. `RestrictedYaml1` scalar and key behavior.
38. XML canonical addresses for namespaces, attributes, repeated children, and mixed content.
39. XML singleton-versus-sequence merge classification across three or more contributions.
40. Complete output-option replacement and contradictory flag errors.
41. Flat-key collision detection after delimiter and shell-identifier normalization.
42. Namespace multiline escape round trips.
43. UTF BOM handling, LF output, no output BOM, and deterministic diagnostic ordering.
44. Direct-publication external failure behavior without rollback.
45. Empty output-plan warnings and inferred-sequence compatibility warnings.
46. Scalar-only references and rejection of subtree and free-wildcard references.
47. Native sequence concatenation versus explicit ordering-value override.
48. Stable ordering values across deletion, wildcard matching, references, and final dense rendering.
49. One concrete output instance per last-wildcard capture tuple, never per descendant.
50. Output-instance-scoped scheme overrides and `output=ignore` restoration.
51. Substituted relative subfolders, encoded captures, and rejection of every `..` segment.
52. INI global-key preamble hoisting and rooted-section behavior.
53. Final fixed-point numeric-map inference, including empty mappings and generated or ignored nonnumeric children.
54. `type=mapping` preservation of numeric mapping keys and `type=array` conversion of nonnumeric mappings.
55. Deterministic multiple-capture partitioning and literal capture insertion.
56. Bare-scalar output behavior for every format.
57. Output-option defaults, XML declaration control, and comment-option enablement.
58. Deterministic distinct-destination publication order under partial storage failure.
59. Single-pass native decoded-string escaping for adjacent backslashes, wildcards, and reference starts.
60. JSON `EscapeNonAscii` for keys, BMP values, and supplementary Unicode scalars.
61. `--help`, `--version`, and every verbosity threshold without changing processing or exit status.
62. Fixed two-space YAML indentation, block-scalar indentation, and type-preserving string quoting.
63. XML comment exclusion from reference aliases and whole-element replacement under both input `merge=replace` and destination `filemerge=replace`.
64. Portable filename device-name handling, ASCII-case-insensitive destination collision detection, and cross-format high-water reset identically on every platform.
65. Namespace record classification, malformed no-`=` records, literal ordinary-value `*`, and Unicode-scalar escape validation.
66. INI path-to-section/key projection, namespace-only delimiter disambiguation, and `FLAT001` collisions.
67. Scheme-reference filename separators as encoded data, empty directive rejection, and `type=array` plus `key` rejection.
68. Output-instance-scoped `WARN010` suppression by `type=mapping` and stable diagnostic stream/code behavior.
69. Differential compatibility fixtures against namespace2xml 2.4.0 for every behavior claimed in Section 3.1, with explicit expected divergences for every correction in Section 3.2.
70. XML sequence projection for mapping children, root sequences, scalar items, record items, and illegal attribute projection.
71. Typed XML component recognition in namespace input, variables, scheme paths, references, and `root`.
72. Deterministic global-budget attribution under varied parser thread schedules.
73. Phase-local diagnostic collection, complete failed-source discard, and phase-boundary abort.
74. Informational-mode precedence, repeated list options, `--`, and explicit-filename extension behavior.
75. Empty concrete output views and wildcard declarations producing no concrete instance.
76. `multiline` on lone scalars, empty sequences, scalar/null sequences, mappings, and container-only items.
77. INI simultaneous scalar-key and descendant-section projection.
78. XML singleton-to-sequence promotion without implicit reference or directive retargeting.
79. Generated `key` string typing and comment movement.
80. The conformance fixture layout, diagnostic-code mapping, and grammar appendices.
81. `--diagnostics-format` pre-scan resolution, covering `--` termination, the `=value` form, repeated occurrences, a missing value, an unrecognized value, and reporting the resulting `CLI001` in the resolved encoding.
82. `json` diagnostic-stream framing and closed-schema conformance, standard-error purity with operational messages suppressed, `[]` under `--verbosity none`, and a single emitted array that still contains late `PATH002` publication diagnostics.
83. Per-occurrence `phase` and `spec` fields for codes that arise in more than one phase.
84. XML attribute-count limits, absence of any separate entity-expansion budget, and deterministic `LIMIT001` attribution when per-source and global bounds are crossed together.
85. `--version` contract-bundle reporting and machine-readable field layout, and registry agreement with the Section 22 code-level facts.
86. The uniform option-token grammar of Section 6.2: the `--name=value` inline form on every long option, the absence of an inline form on short options, a value that is not attached to any option, an option token that ends the argument vector still requiring a value, and `-` as an ordinary value.
87. Marker-carrying JSON and YAML mapping keys: reading an attribute, content, and qualified-element key, the leading-backslash escape and its suppression of marker recognition, `PARSE001` for a key that begins like a marker without completing it, escaping on output, and an XML → JSON → XML round trip that preserves attributes.
88. INI global-key framing: `WARN012` on a written preamble and its absence when no global key survives, `GlobalSection` hoisting the global keys into a leading section named `global` that a reader requiring a section header accepts, no empty header when the hoisted section would be empty, and the blocking `FLAT001` when a path of two or more parts already projects to that section.

## 27. Deferred features

The following are intentionally deferred and must not be implemented implicitly:

- JSON comments;
- native INI input;
- native POSIX shell-assignment input;
- YAML document separators and multiple documents;
- preservation of YAML anchors, aliases, tags, quote style, or multiline style;
- XML DTDs, custom entities, and processing instructions;
- exact lexical round trips;
- a standardized extended namespace metadata syntax;
- automatic quoted-namespace identifier sanitization or collision-prone renaming;
- subtree references or implicit hierarchical substitutes;
- globally transactional multi-file publication;
- temporary-file output staging;
- unrestricted absolute output paths;
- automatic network or schema access;
- environment-variable interpolation beyond explicitly defined profile references.

Adding a deferred feature requires an explicit specification update and compatibility tests.

## Appendix A. Namespace and reference grammar

This appendix is normative. It uses ABNF notation from RFC 5234 with the following Unicode extension:

- `scalar` means one Unicode scalar value other than a physical record terminator;
- semantic predicates written below the grammar remain normative where pure ABNF cannot express escape context or typed-component recognition;
- parsing is performed on decoded Unicode text after Section 7.4 encoding validation.

### A.1 Physical records

```abnf
document        = [record] *(line-end [record])
line-end        = LF / CRLF / CR
record          = *record-scalar
record-scalar   = scalar
```

Each `record` is classified in the exact order from Section 8.1:

1. empty or spaces/tabs only;
2. comment when its first non-space/tab scalar is `#`;
3. permanent mask when its first non-space/tab scalar is `!`;
4. ordinary entry when it contains a separating `=`;
5. malformed `PARSE001`.

A separating `=` is defined in Section 8.1: an unescaped `=` outside a `Q{...}` URI. Recognizing it therefore requires scanning the name with the `qname` productions of A.2 rather than searching the record for a character.

`U+0085`, `U+2028`, and `U+2029` are `record-scalar`, not `line-end`.

### A.2 Qualified names

```abnf
qname               = component *("." component)
component           = typed-attribute / typed-content / qualified-element / ordinary-component
typed-attribute     = "@" xml-name-component
typed-content       = "#" canonical-decimal
xml-name-component  = qualified-element / ordinary-component
qualified-element   = "Q{" uri-body "}" local-component
canonical-decimal   = "0" / (%x31-39 *DIGIT)

uri-body            = *(uri-scalar / escaped-rbrace / escaped-backslash)
uri-scalar          = scalar
escaped-rbrace      = "\" "}"
escaped-backslash   = "\" "\"

ordinary-component  = 1*(ordinary-scalar / name-escape / wildcard-token)
local-component     = 1*(ordinary-scalar / name-escape / wildcard-token)
ordinary-scalar     = scalar
wildcard-token      = "*" [capture-id]
capture-id          = "[" identifier "]"
identifier          = 1*(ALPHA / DIGIT / "_" / "-")

name-escape         = "\" ("." / "*" / "=" / "#" / "!" / "$" / "@" / "}" / "Q" / "\" / unicode-escape)
unicode-escape      = "u{" 1*6HEXDIG "}"
```

Semantic requirements:

- an unescaped `.` separates components and cannot begin, end, or occur twice consecutively;
- `ordinary-scalar` excludes unescaped `.`, `=`, backslash, physical line terminators, and any scalar consumed as part of a leading typed marker; it excludes unescaped `*` only where the effective `substitute` mode enables name interpretation;
- `uri-scalar` excludes `}` and backslash;
- `unicode-escape` must encode one Unicode scalar and therefore excludes surrogates and values above U+10FFFF;
- inside `Q{...}`, only `\}` and `\\` are escapes; every other backslash sequence is `PARSE001`;
- the first unescaped `}` ends `uri-body`;
- at a component start, typed-marker recognition follows the source-position rules in Section 8.2, and commits: a component beginning with an unescaped marker must match that typed production in full or is `PARSE001`;
- an escaped leading marker creates an ordinary component;
- wildcard tokens are legal only in contexts whose effective `substitute` mode enables name interpretation. Where that mode is `None`, an unescaped `*` is instead an `ordinary-scalar` carrying its own literal character, which is how Section 13.4's "`substitute=None` disables interpretation in names" is realized in this grammar; the two exclusions above are complementary, so an unescaped `*` is always exactly one of the two and never unparseable.

### A.3 Namespace values

```abnf
namespace-value = *(value-scalar / value-escape)
value-scalar    = scalar
value-escape    = "\\" / "\*" / "\${" / "\n" / "\r" / "\t" / unknown-value-escape
unknown-value-escape = "\" scalar
```

The tokenization is left-to-right and longest-token-first among the listed recognized escapes. `unknown-value-escape` preserves both the backslash and following scalar. Namespace values do not recognize `\u{...}`.

This ABNF covers escape tokenization only. Reference recognition from Section 8.4 and wildcard-token recognition from Sections 12.1 and 12.2 apply at `value-scalar` positions of the same single left-to-right pass. At each position the pass tries, in this order: a `value-escape`, longest first; then `${`, which must begin a `reference`; then a `wildcard-token`, where the owning name's captures and the effective `substitute` mode make one possible; otherwise the scalar is literal text.

Emitted text is never rescanned. The `${` emitted by `\${` therefore never begins a reference and the `*` emitted by `\*` is never a wildcard token, which is what makes those two escapes effective. `\\${a}` emits a literal backslash followed by a reference to `a`, because the escape consumed both backslashes and the pass resumes at the `$`; the same text in a decoded native string is governed by A.5 instead and emits the literal text `\${a}` with no reference.

A trailing backslash with no following scalar matches `value-scalar` and emits itself.

### A.4 References

```abnf
reference       = "${" reference-name "}"
reference-name  = qname
```

The closing brace is the first unescaped `}` outside a `Q{...}` URI. Wildcard syntax in `reference-name` is legal only for explicit captures bound by the owning template. A legacy bare `*`, malformed name, free capture, or missing terminator is `REFERENCE001`.

### A.5 Decoded native-string escape transducer

JSON, YAML, and XML strings have already been decoded by their native parser and do not use `namespace-value` ABNF. They use this deterministic transducer:

1. read Unicode scalars from left to right;
2. `\*` emits literal `*`;
3. `\${` emits literal `${`;
4. any other backslash emits itself and consumes no following scalar;
5. emitted text is never rescanned.

### A.6 Output delimiter disambiguation

Delimiter escaping in Section 16.4 is an output encoding phase, not part of input ABNF. It runs before ordinary namespace name escaping, emits atomic `\u{HEX}` text, and never rescans emitted escapes.

## Appendix B. Normative diagnostic mapping

Every blocking or warning condition maps to exactly one most-specific code. This table supplements the registry in Section 22.

| Condition | Code |
|---|---|
| Unknown CLI option, missing option value, invalid repeated/list syntax, invalid `--` use, invalid limit value, invalid `--diagnostics-format` or `--verbosity` value | `CLI001` |
| Malformed namespace record/name, malformed JSON/YAML/XML syntax, duplicate native mapping key, unsupported native document structure | `PARSE001` |
| Invalid byte sequence, unsupported BOM/encoding, XML declaration encoding inconsistent with decoded input | `PARSE002` |
| Unknown/empty directive, illegal directive value, illegal option combination, `type=array` plus `key` | `SCHEME001` |
| Ambiguous simple/canonical scheme path | `SCHEME002` |
| Invalid, undefined, or mixed wildcard capture outside a reference | `WILDCARD001` |
| Wildcard fixed-point, candidate, generated-node, or iteration limit | `WILDCARD002` |
| Malformed/unterminated reference, legacy bare wildcard in reference, free explicit capture | `REFERENCE001` |
| Missing exact or alias reference target | `REFERENCE002` |
| Canonically distinct reference cycle | `REFERENCE003` |
| More than one canonical scalar for one simple alias | `REFERENCE004` |
| Mapping/sequence/comment/non-scalar target or unsupported non-scalar concatenation | `REFERENCE005` |
| Invalid transformation target; input `merge=error` conflict; `key` field/value collision; illegal multiline target; invalid XML sequence projection; missing required output root | `TYPE001` |
| Exclusive-shape projection omits a scalar or container contribution | `TYPE002` |
| Distinct paths collide after namespace, quoted-namespace, or INI projection | `FLAT001` |
| Invalid quoted-namespace identifier or NUL value | `SHELL001` |
| DTD, external entity/resource, network retrieval, or prohibited XML feature | `XML001` |
| Invalid XML name/namespace/canonical address/declaration structure other than byte-encoding disagreement | `XML002` |
| Value/name/comment cannot be represented by effective `PortableIni1` options | `INI001` |
| `filemerge=error` rejects a second destination contribution | `COLLISION001` |
| Final output model cannot be serialized under its selected format/options | `SERIALIZE001` |
| Invalid, escaping, insecure, traversal, portability-key-colliding, or uncontainable destination path | `PATH001` |
| Destination open, create, write, flush, or close failure after publication starts | `PATH002` |
| Ordering-value overflow or any non-wildcard resource limit | `LIMIT001` |
| Missing CLI input/scheme path | `WARN001` |
| Deprecated alias | `WARN002` |
| Unsupported metadata/comment discarded | `WARN003` |
| Native implicit sequences concatenate without explicit merge | `WARN004` |
| Same-destination fold or cross-format replacement | `WARN005` |
| XML processing instruction discarded | `WARN006` |
| XML formatting whitespace discarded | `WARN007` |
| Validated output plan contains no destinations | `WARN008` |
| Directive binds to no concrete output instance or path, or wildcard output creates no concrete instance | `WARN009` |
| Concrete output instance selects nothing | `WARN009` |
| JSON/YAML numeric mapping remains inferred as a sequence | `WARN010` |
| Later unmarked contribution adds an ordinary component aliasing an existing XML component | `WARN011` |
| INI output writes a global-key preamble without `GlobalSection` | `WARN012` |

`COLLISION001` has severity error and cardinality once per rejected destination contribution after the first. It is compatibility-stable with the Section 22 registry.

When a sentence could match several rows, the narrowest row wins. In particular:

- encoding disagreement is `PARSE002`, while an otherwise invalid XML declaration is `XML002`;
- a non-scalar reference is `REFERENCE005`, not `TYPE001`;
- a portable-path case collision is `PATH001`, while an intentional same-path collision accepted for folding is `WARN005`;
- pre-publication representability is `SERIALIZE001`, while format-specific transformation misuse is `TYPE001` or `INI001`.

Elsewhere in this specification, an otherwise unqualified phrase maps as follows: "blocking parse error" to `PARSE001`, "blocking scheme error" to `SCHEME001`, "blocking wildcard error" to `WILDCARD001`, "blocking reference error" to the most specific `REFERENCE001` through `REFERENCE005`, "blocking type error" to `TYPE001`, "blocking path error" to `PATH001`, and "blocking serialization error" to `SERIALIZE001`.

## Appendix C. Conformance fixture format

The portable conformance corpus uses one directory per case:

```text
conformance/<case-name>/
  args.txt
  args-diagnostics.txt        (only when args.txt contains `--` or `--diagnostics-format`)
  inputs/
  schemes/
  expected/
  expected-diagnostics.json
  expected-exit-code.txt
  expected-stdout.txt          (only when the case asserts standard output)
  requirements.txt
  legacy.md
```

The harness never runs a case in place. It copies the case into a fresh working directory per run, so that repeated runs and the Section C.7 determinism matrix always start from an unpolluted output root. The reserved names above belong to the fixture; every other path under the working directory after a run is a produced destination and is compared against `expected/`.

### C.1 `args.txt`

- UTF-8 without BOM and LF line endings;
- one exact CLI token per physical line;
- paths are relative to the case directory unless the case explicitly tests absolute-path rejection;
- blank tokens are represented by an empty line;
- repeated options and token order are preserved exactly;
- environment substitutions are prohibited.

The harness invokes the compatibility command with these tokens and sets the working directory to the case directory.

### C.2 Inputs and schemes

`inputs/` and `schemes/` contain immutable fixture files. `args.txt` determines their processing order; directory enumeration order is irrelevant.

### C.3 Expected output tree

`expected/` contains the complete expected output-root tree. Every file is compared byte-for-byte. Unexpected, missing, differently cased, or differently normalized paths fail the case. An absent `expected/` directory means no destination may be created.

### C.4 Expected diagnostics

`expected-diagnostics.json` is the exact content of the `json` diagnostic stream defined in Section 6.4.3: a UTF-8 JSON array in normative emission order, using that section's byte layout and closed schema. The member catalogue below is exploded across lines so that each member can be annotated; it is **not** the emitted layout. Section 6.4.3 requires each element to be one compact object on its own line, and a fixture is compared as literal text, so an expected file written in the layout below will fail every case. A conforming single element is:

```json
{"code":"TYPE001","severity":"error","phase":"planning","source":"inputs/example.properties","line":3,"column":1,"path":"a.b","declaration":"a.b.type=multiline","destination":"output.json","spec":"§16.6","message":"…"}
```

Each object may contain:

```json
{
  "code": "TYPE001",
  "severity": "error",
  "phase": "planning",
  "source": "inputs/example.properties",
  "line": 3,
  "column": 1,
  "path": "a.b",
  "declaration": "a.b.type=multiline",
  "destination": "output.json",
  "spec": "§16.6",
  "message": "…"
}
```

Fields that are inapplicable are omitted rather than set to null. Paths use `/` separators relative to the case directory. An absent `expected-diagnostics.json` means the case writes no diagnostic stream at all, which is how the Section 6.1 informational modes are expressed; it is not the same as an empty array.

The harness runs every case twice:

- run A uses the `args.txt` tokens verbatim and verifies the exit code, standard output, and the expected output tree;
- run B obtains the JSON diagnostic stream and verifies it.

Run B's token vector is formed as follows. When the case supplies `args-diagnostics.txt`, those tokens are used verbatim. Otherwise, when `args.txt` contains neither a bare `--` nor any `--diagnostics-format` token, the harness appends `--diagnostics-format` and `json`. Otherwise the case is malformed and the harness fails it, because appending after a bare `--` would turn the appended tokens into list-option values, and appending after an existing `--diagnostics-format` would silently mask the value the case is exercising. A case that deliberately exercises `--` or `--diagnostics-format` therefore states its second token vector explicitly, and that vector must select the `json` encoding.

Run B's stream is validated in three steps: it must parse as JSON, it must conform to the Section 6.4.3 schema and byte layout, and it must match `expected-diagnostics.json` structurally. Structural matching compares array length, element order, and every member exactly, except that `message` is never compared and `spec` is compared only when the expected object declares it. Codes, severities, phases, structured fields, cardinality, and array order are therefore compared exactly, while localized prose and specification renumbering do not invalidate the corpus.

### C.5 Exit code, standard output, and requirement traceability

`expected-exit-code.txt` contains one ASCII decimal exit code followed by LF.

`expected-stdout.txt` constrains standard output. It is UTF-8 without BOM with LF line endings, and each of its non-empty lines is an assertion about one line of standard output. Blank lines are ignored, so a case may group its assertions readably.

- A line beginning with `!` asserts that the rest of that line does **not** occur as a complete line of standard output.
- A line beginning with `\` drops that first character and is then read as a required line, which is how a case requires a line that genuinely begins with `!` or `\`.
- Every other line is required: it must occur, exactly, as a complete line of standard output.

Required lines must occur in standard output in the relative order the file states, though standard output may contain other lines between and around them. Forbidden lines carry no ordering.

Standard output is asserted rather than reproduced because Section 6.4.1 fixes a *minimum* field set for `--version` and leaves informational prose localizable under Section 6.4.2. A byte-exact expectation would therefore fail an implementation that conforms, and asserting nothing would pass an implementation that prints nothing at all. The forbidden form exists because the Section 6.1 precedence between `--help` and `--version` is a statement about what must *not* be printed, which no set of required lines can express.

A line may contain a placeholder of the form `${name}`, which the harness expands before comparing. The placeholder set is closed:

| Placeholder | Value |
|---|---|
| `${contract-bundle}` | the bundle revision identifier |
| `${specification-sha256}` | the digest of the specification text the bundle covers |
| `${registry-sha256}` | the digest of the diagnostic registry the bundle covers |

Every placeholder is resolved from the distributed contract bundle described in Section 22, never from the binary under test. A harness that asked the tool what its own contract revision is would report agreement between a binary and itself, which is not the property Section 22 exists to establish.

When `expected-stdout.txt` is absent, standard output must be empty. Section 6.2 confines standard output to the informational text of `--help` and `--version`, so a case that does not declare that text asserts its absence; anything else on standard output is a defect, and most obviously the generated configuration content that Section 6.2 requires be written only to planned destination files.

`requirements.txt` contains one Section 26 item number per line. Every fixture must reference at least one item, and every Section 26 item must be discharged by at least one fixture or by at least one named gate.

Most items are discharged by fixtures, and a fixture is the preferred evidence because it is black-box: it states an input and an expected result and knows nothing about how the tool is built. Some items cannot be. An item about behaviour under an external storage failure cannot be provoked by a case whose only side effect is writing files, because the corpus harness has no way to make a write fail. An item about the corpus itself — that Appendix C's layout is read correctly, or that every Section 3.1 behaviour has a case — cannot be discharged by a case inside that corpus without circularity. For these the manifest records a `gates` list instead, naming the test or continuous-integration job that does the checking.

A named gate must exist, and that is itself checked: a gate naming a test must resolve to a test that is actually declared, and a gate naming a continuous-integration job must resolve to a job defined in the workflow. Without that check the field would be an accounting fiction — a claim of coverage discharged by writing a plausible name into a file — which is precisely the failure this appendix exists to prevent. An item discharged by a gate *alone*, with no fixture naming it, must also say in the manifest why a fixture cannot discharge it, so that the exemption is argued rather than assumed. An item may carry both, and some should: a fixture can state the expected result while a gate supplies the condition under which the fixture is run, and neither alone is the whole claim.

A reference is a claim, and a number in a text file costs nothing to write and nothing to keep true. The manifest must therefore name exactly the fixtures that reference each item, so that adding, removing, or silently retargeting a claim fails the gate until the manifest is re-authored and reviewed. This holds for every item and not only for the ones marked `required`: restricting it to `required` items lets a pending item quietly accumulate fixtures the manifest never records, so the manifest understates coverage exactly where coverage is still being built and is most worth reading. For an item the manifest marks `required`, one further condition holds. Each of the fixtures naming it must carry at least one expectation beyond its exit code: an expected output tree, an expected standard output, or a declared diagnostic stream. Declaring the empty array is such an expectation, because Appendix C.4 distinguishes it from writing no stream at all and the distinction is observable; declaring nothing at all is not, because an exit code alone distinguishes too little to be evidence that the item was exercised.

### C.6 Legacy differential metadata

`legacy.md` is required for a Section 3 compatibility or correction case. It records:

- a verdict on namespace2xml 2.4.0, written on its own line as `- namespace2xml 2.4.0: **<verdict>**` and continuing in prose on that line or the lines that follow, where `<verdict>` is exactly one of `agrees`, `differs`, `crashes`, or `nondeterministic`;
- the relevant Section 3.1 or 3.2 contract;
- the expected legacy observation and clean behavior;
- why the difference is intentional.

The verdict is a claim about the observable result — the output tree and the exit code — and nothing else. Two tools can produce the same result for unrelated reasons: a case that rejects an option 2.4.0 never had exits `1` under both, one because the value is invalid and one because the option is unknown. That case `agrees`, and its prose must say that the option does not exist, because the verdict answers "will this run behave differently" and the prose answers "why". A verdict that tried to mean both would be checkable as neither.

A case declares at most one verdict. A case carrying none claims that the baseline reproduces its expected result, which the harness checks exactly as it checks a declared `agrees`: the baseline is run against every case in the corpus, and a divergence with no verdict to explain it fails the build. Coverage of Section 3 therefore cannot be dodged by omission, and the corpus is differential evidence in its entirety rather than only where someone remembered to say so.

The harness verifies every verdict by observation rather than recording it, and it observes more than once. A single run cannot tell a stable result from a lucky one: 2.4.0 was measured producing three different observable results for one fixture across ten identical runs, and one sample would have reported whichever arrived first as settled fact. Each verdict is therefore a claim about a set of samples of the same case, and the four verdicts partition those samples by how they stand to the case's expected result. `agrees`, and an absent verdict, require every sample to produce the case's expected output tree and expected exit code. `differs` requires every sample to diverge from them, so a correction the baseline turns out to reproduce fails as loudly as a compatibility claim it breaks. `crashes` requires every sample to diverge and to exit nonzero. `nondeterministic` records a baseline observed to stand in both relations on different runs, and it is the required verdict whenever that is seen, because neither agreement nor difference is then true of the baseline. An unchecked verdict is a comment, and the compatibility policy of Section 3 would then rest on the recollection of whoever wrote it.

Sampling refutes; it does not confirm. One contradicting sample proves `agrees`, `differs` or `crashes` false, and no number of consistent samples proves any of them true. 2.4.0 was measured reproducing one case's expected result on a single run in forty while aborting on the other thirty-nine, and a budget large enough to be confident of catching a branch that rare is a budget whose own outcome is decided by chance. The harness therefore does not attempt to re-derive `nondeterministic`: it is the verdict a case is forced into when one of the other three is refuted, it carries its measurement in prose, and no set of samples refutes it. It is consequently the one verdict a contributor may assert without the harness's agreement, and the generated migration notes list every case that asserts it, so the concession is published rather than silent.

A baseline may also vary *within* one of those classes, producing a different wrong answer on different runs. That does not change the verdict. The verdict answers whether a migrating run will behave differently from what the contract requires, and a run that is wrong in two different ways is wrong either way; the prose is where the instability is described. Scoring the exact bytes instead would make the lane fail on a schedule set by whatever it is that varies, which is a property of the machine rather than of the contract.

The sample count is an implementation choice, bounded below by two and by whatever makes the lane's own result reproducible. Sampling detects instability; it does not prove stability, and a `nondeterministic` case discovered later is a fixture correction rather than a contract change.

The differential baseline is the published namespace2xml 2.4.0 .NET tool and the analyzed source commit `b1c230e`. The NuGet package at `https://api.nuget.org/v3-flatcontainer/namespace2xml/2.4.0/namespace2xml.2.4.0.nupkg` has SHA-256 `92472F4F191A8FC32B81CE30A8F3E2FC97CF99C968F635155172F111EE65C3ED` and size 1,095,996 bytes. The harness must reject a baseline package whose hash or size differs, and must run it on the .NET 9 runtime the package targets rather than rolling it forward, because a baseline observed on a runtime it was never published against is evidence about a configuration nobody shipped.

The harness must establish that the required runtime is available before it observes anything, and must never treat a failure to launch as a baseline result. A host that cannot find the runtime reports what is indistinguishable, after the fact, from a tool that wrote nothing and exited nonzero. The confusion is not symmetric: a baseline that never started diverges from every case's expected result, so it fails each `agrees` case and *confirms* every `differs` and `crashes` one. The lane then reports a plausible list of apparently wrong verdicts whose obvious repair — flipping them — turns the entire differential corpus green while measuring nothing, and Section 3 would afterwards rest on a binary that was never executed. The absence of the runtime is therefore a failure of the lane itself, reported as such and distinguishable from any case's verdict, and never evidence about 2.4.0.

### C.7 Determinism runs

Every successful fixture is repeated under:

- at least two parser worker counts, including one;
- at least two supported locales with different decimal conventions;
- at least two time zones;
- repeated fresh output roots.

Outputs and structured diagnostics must remain byte-identical. Performance instrumentation fixtures compare deterministic counters from Section 23, not elapsed wall-clock time.
