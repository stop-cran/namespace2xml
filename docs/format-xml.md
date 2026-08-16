# XML

XML is the richest format namespace2xml both reads and writes. It is the only one that carries
attributes, mixed content, CDATA and namespace-qualified names, and the only one whose ordinary
namespace projection cannot reconstruct the source without extra vocabulary. Section 11 of
[the specification](specification.md) is the contract; this page is the reader's guide to it.

The claims below cite the specification section each one comes from. If a claim and the
specification disagree, the specification is right and the claim is a defect in this page — please
report it.

## What the extension selects

An input path with the extension `.xml`, matched case-insensitively (§7.1), is parsed as XML.
Every other extension routes to namespace-profile parsing, including `.ini` and `.sh`, which stay
namespace-profile for compatibility. XML output is selected by an `output=xml` directive; the
default file name is `<selector>.xml` for a non-root selector and `output.xml` for the empty root
(§16.2). An explicit `filename` is used verbatim without appending `.xml` (§3.1).

§15 gives scheme files the case-insensitive `.json`, `.yaml` and `.yml` extensions and routes every
other extension, including none at all, to namespace-profile parsing. **`.xml` is excluded by name**:
§15 defines no projection from an XML document to a qualified directive path, so a `-s` file ending
in `.xml` is `PARSE001` against §15, once per failing source, reported before the file is read. Three
questions have no answer and none is obviously right — an element and an attribute are distinct
component kinds under §11.4 and only one can be an ordinary name part, the single root element sits
where no JSON or YAML top-level key does, and §12.2 spells a capture `*`, which is not a legal XML
name, so a wildcard selector cannot be written at all. The extension decides, not the content, so a
scheme written in namespace-profile syntax and saved as `scheme.xml` is rejected too. JSON and YAML
schemes **are** read; the examples below use namespace-profile schemes. See
[#72 (closed)](https://github.com/stop-cran/namespace2xml/issues/72).

## The parser is locked down

§11.1 fixes the parser's security posture and there is no way to relax it. External entity
resolution is prohibited, external schema retrieval is prohibited, and network retrieval is
prohibited. A document containing a DTD is refused outright rather than partially processed, with
`XML001` at the position of `<!DOCTYPE` — even the internal subset form is refused, and even when
no `SYSTEM` identifier is present. The five predefined entities (`&amp;`, `&lt;`, `&gt;`,
`&apos;`, `&quot;`) and numeric character references are accepted; every other entity name resolves
to nothing at all, because there is no way to declare one. §11.1 explains why unbounded-expansion
attacks are structurally impossible under this rule set rather than merely bounded: each
recognized reference expands to exactly one Unicode scalar, so decoded length never exceeds
encoded length.

The one XML-specific budget is `--max-xml-attributes`, default 4,096, applied per element and
including namespace declarations (§11.1). Everything else falls under the general
Section 23 budgets: `--max-input-bytes`, `--max-total-input-bytes`, `--max-depth`,
`--max-nodes`, and the comment budgets.

Input decoding is fixed by §7.4 alone. If the XML declaration names an encoding, it must agree
with the byte-order-mark selection or the strict UTF-8 default; disagreement is a blocking
`PARSE002`, distinct from `XML001` and the other `XML002` conditions per Appendix B. Processing
instructions are discarded with a summarized `WARN006` (§11.2, §22).

Verified. This input:

```xml
<!DOCTYPE doc [<!ENTITY e "x">]>
<doc>&e;</doc>
```

is refused with

```text
error XML001 §11.1: this document declares a DTD, and Section 11.1 prohibits DTDs,
external entities, and external resource retrieval.
```

and exit code 1. See `conformance/xml-prohibited-dtd-and-external-resources` for the six shapes
of DTD reference the tool refuses in one run and their line/column positions.

## The supported subset

§11.2 fixes exactly what XML input models:

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

The XML declaration itself is not retained as a data node. §11.8 lists what is not preserved,
which matters when a round trip fails silently: DTDs, custom entities, processing instructions,
schema type annotations, entity-reference boundaries, exact namespace-declaration placement,
exact prefix choice when several prefixes name the same URI, exact empty-element spelling, and
exact attribute quote style. §3.3 makes the normalized same-format round trip a guarantee only
within this list.

## Canonical XML addressing

This is the heart of the guide, because it is the vocabulary a scheme writer needs when picking
between an attribute and a same-named child element, or targeting the third of five repeated
children, or referring to a piece of mixed content. Read §11.4 in full; the summary below picks
out the parts a scheme uses.

### The typed components

§11.4 introduces three marked forms and one addressing convention:

- `@name` addresses an attribute, `@Q{urn:p}name` addresses a namespace-qualified attribute;
- `Q{uri}local` addresses a namespace-qualified element; `Q{}local` addresses an unqualified
  element explicitly;
- `#n` addresses one content-token slot in the parent's ordered stream of text, CDATA,
  child elements and comments, where `n` is a canonical decimal.

Dots inside `Q{...}` are part of the URI and do not split the qualified path (§11.4). The first
unescaped `}` closes the URI; a literal `}` inside the URI is written `\}` and a literal
backslash is `\\` (§11.4, Appendix A.2). Inside the URI, no other escape is admitted: any other
backslash sequence is `PARSE001`. Once out of the URI, the local name uses ordinary Section 8.2
name escaping.

The reserved `xml` prefix binds to `http://www.w3.org/XML/1998/namespace` (§11.4). Its
attributes therefore address canonically: `xml:space` is
`@Q{http://www.w3.org/XML/1998/namespace}space`. That is the shape a scheme selector must use to
match it.

### An element name containing a dot needs an escape

XML admits `.` inside an NCName, and §8.2 makes `.` the delimiter between namespace name parts.
An element named `system.web` is therefore **one** name part whose text contains a dot, and every
namespace spelling of that part has to escape it:

```text
configuration.system\.web.compilation.@debug=true
```

Appendix A.2 gives that character two spellings, and both denote the same name part and so the
same overlay node:

- `\.` — the literal-character escape, which is what a person writes;
- `\u{2E}` — the Unicode-scalar escape, which is what the namespace **writer** emits. §16.4 fixes
  the delimiter's own output form as `\u{HEX}`, so rendering the model of an `app.config`-shaped
  document hands back `system\u{2E}web.compilation.@debug=false`, never `system\.web…`. Only the
  output spelling is fixed; both forms read back identically, and a profile may use either.

The same rule applies inside a qualified component once the URI has closed, because §11.4 gives
the local name ordinary Section 8.2 escaping: `Q{urn:p}system\.web` and `Q{urn:p}system\u{2E}web`
are one component. Dots *inside* `Q{...}` are part of the URI and need no escape, as above.

Written without the escape, the address is not malformed — it is a **different, valid** address.
`.` separates, so `configuration.system.web.compilation.@debug` names four parts, none of which
exists in the document, and §17.1 creates them. The run exits `0` with an empty diagnostic stream,
the real element keeps its value, and a parallel subtree appears beside it:

```xml
<configuration>
  <system.web>
    <compilation debug="false" />
  </system.web>
  <system>
    <web>
      <compilation debug="true" />
    </web>
  </system>
</configuration>
```

Verified, and pinned by the `xml-an-element-name-with-a-dot-needs-an-escape` conformance case.

No diagnostic reports this, and none is specified. Creating an absent path is ordinary overlay
behaviour, and nothing distinguishes a mis-escaped address from an intended new element.
`WARN011` does not reach it either: §11.4 confines that warning to an attribute and a
namespace-qualified element sharing one simple alias, and a dotted name is neither.

One partial signal exists, and the obvious fix for it hides the problem. Without `root`, the
phantom raises the view's top-level member count and XML refuses the document:

```text
error TYPE001 §19.5: the selected view has 2 top-level members and XML has one document
element, so Section 14.1 requires an explicit 'root'.
```

Adding `root`, which is what that error asks for and what the shape genuinely needs, silences the
only hint that the override missed. So do not read a clean run as confirmation that an address
bound to something. Render the model first — `docs/usage-methodology.md` §3 makes that a habit.

### The alias shorthand

An XML document contains typed components; a namespace scheme can address them either
canonically or through a **simple alias index** that lets an unmarked component do the work when
context makes it unambiguous (§11.4, §13.1). The alias index replaces every `Q{uri}local` or
`@Q{uri}local` with `local`, every `@local` with `local`, removes a leading `#n` before a child
element, and removes a terminal text/CDATA `#n` so a scalar aliases to its owning element
path (§13.1). Ordinary JSON, YAML and namespace paths alias to themselves.

Two consequences to keep in the front of your mind:

- **`a.b` and `a.Q{}b` are one component.** They are two spellings of one addressing form for
  the same overlay node (§11.4). Marking a component does not narrow its identity; it narrows
  the addressing to bypass the alias index.
- **The element-path scalar and the sole-text-node scalar of the same element are one
  scalar identity.** They are two canonical addresses for the same value, not two candidates
  in the alias ambiguity index (§11.4).

### When the alias is ambiguous

If more than one canonical scalar has the same alias, an unmarked reference to that alias
fails as `REFERENCE004`, listing the canonical candidates. That is a design property, not an
implementation detail: the alias is a convenience for the unambiguous case, and the tool refuses
to guess in the ambiguous one.

Verified. For this input:

```xml
<r><a x="attr-val"><x>elem-val</x></a></r>
```

the value `${r.a.x}` reports:

```text
error REFERENCE004 §13.1: the reference 'r.a.x' has 2 canonical scalars with that
simple alias: 'r.a.@x', 'r.a.x'. Address one of them exactly.
```

`${r.a.@x}` selects the attribute; `${r.a.Q{}x}` selects the child element. Verified — both
reference forms resolve to their intended values in the same run. This is the point of the marked
forms: they are the vocabulary that survives the ambiguity.

For **scheme selectors**, §15.2 promises a parallel diagnostic — `SCHEME002` — when an unmarked
selector could bind to more than one canonical component at a matched location. `KNOWN-LIMITS.md`
§1.10 records that the current build resolves an unmarked scheme selector directly to the typed
component with the same simple text (element `x`, never attribute `@x`), so `SCHEME002` currently
has no call site and is emitted nowhere. A directive that would be ambiguous under the alias
index therefore binds *canonically*, without a warning. This is a live gap between specification
and implementation and is expected to close before 3.0 ships; write the canonical spelling in a
scheme where the difference matters and the same scheme will work under both readings.

### `#n` content tokens

Every XML parent assigns stable content-token ordering values across all of its child
elements, text, CDATA and comments, including element-only parents (§11.4). Element-only children
retain their ordinary element-name addressing *and* carry their content-token ordering value
for deterministic placement. So a comment among element-only siblings — `<a><b/><!--c--><d/></a>` —
addresses as `a.#1` (§11.4, worked example).

Mixed content is any element that contains at least one text or CDATA node (§17.4). In a mixed
element, every content node — text, CDATA, child element and comment — occupies a `#n` slot:

- a text or CDATA node addresses as `#n`;
- a child element inside mixed content addresses as `#n.element-name` (with the element still
  addressable by name for scalar and reference purposes only where the mixedness classification
  in §11.4 permits — see below).

Comments alone do not make a parent mixed-content (§17.4). Comment content-token paths never
enter the alias index because comments have no scalar payload; they are invisible to
format-agnostic reference resolution (§13.1) and are not value-reference targets (§11.4).

Verified. This input:

```xml
<a xmlns:p="urn:p">text<p:b x="1"/><b>two</b></a>
```

emits, in namespace projection:

```text
a.#0=text
a.#1.Q{urn:p}b.@x=1
a.#2.b=two
```

The three content tokens are the text run `text`, the namespaced element `<p:b>` and the plain
`<b>`. `<p:b>`'s attribute lives beneath `#1` via the mixed-content wrapper; the URI is what
identifies the namespace, not the source's prefix `p`.

### Repeated same-name element children

Repeated same-name children under an element-only parent form a sequence at `parent.child` and
use Section 5.4's stable ordering values (§11.4). A single child keeps its name; the moment a
second same-named sibling arrives — in the same document or in a later contribution — the
canonical paths become `parent.child.0`, `parent.child.1`, and so on. This is
**singleton-to-sequence promotion**, and it re-addresses the former singleton: the path
`parent.child` no longer names a scalar or element, and a reference to it fails under the
ordinary missing- or non-scalar-reference rule (§11.4). Implementations must not silently retarget
`parent.child` to the first repeated child (§11.4).

Verified. This input, from `conformance/xml-canonical-addresses`:

```xml
<a><tag>first</tag><tag>second</tag></a>
```

emits:

```text
a.tag.0=first
a.tag.1=second
```

The parent's own content-token stream still counts elements, text and comments for `#n`
placement in mixed content, and the repeated-child sequence path has its own high-water
allocator that is independent of that stream (§11.4). An explicit numeric-index contribution
patches a sequence item at its supplied ordering value:

Verified. Given `<r><b>1</b><c>2</c><b>3</b></r>` and the namespace contribution `r.b.1=30`,
the merged model emits `<b>1</b><c>2</c><b>30</b>` — `<c>` keeps its place between the two `<b>`
elements and the second `<b>` is patched to `30`. See
`conformance/xml-element-only-children-keep-their-place`.

### Mixedness is a property of the merged element

Whether an element is mixed is decided across all input contributions at concrete merge time
(§11.4). If any contribution makes it mixed, every content node in the merged element uses `#n`,
even ones that originated in an element-only source document. Child elements in mixed content
also stop deep-merging with elements from another contribution (§17.4), so a converted
element-only contribution is placed above the mixed document's own content-token slots rather
than colliding with them.

Verified. Merging `<r><a>t<b>1</b></a></r>` with `<r><a><b>9</b></a></r>` under `deep` merge
produces:

```xml
<a>t<b>1</b><b>9</b></a>
```

which projects to `r.a.#0=t`, `r.a.#1.b=1`, `r.a.#2.b=9` in namespace output. See
`conformance/xml-mixedness-spans-contributions`.

### Scalarization

Reference and scalar-transformation lookups follow §11.4:

- an attribute owns its string scalar at its attribute path;
- text and CDATA own string scalars at their content-token paths;
- an element with no child elements and exactly one non-comment text or CDATA node also
  exposes that scalar at the element path;
- every other element has no scalar payload at its element path.

A canonical reference directly to a comment content-token fails as a non-scalar reference.

### The document element and `root`

When converting a non-XML mapping to XML, the document element comes from the selected view, not
from the selector. §16.3 removes the concrete output selector prefix first, and does not retain
the selector name unless `root` also names it. What remains must be a single top-level member,
which becomes the document element; `root` overrides that and wraps the remaining content in a
name of its own (§11.4).

So `a.output=xml` over `a.b.x=1` emits `<b>`, not `<a>`. A selected view with more than one
top-level member, or one that is a bare scalar, has no single element to serve as the document
element and fails as `TYPE001` (§19.5, §14.1) until `root` supplies one.

## XML comments

XML comments are retained as ordered document nodes (§11.5). They are not forced into a
"leading comment for the next value" representation because a comment may occur between
mixed-content nodes or after the final child. Standalone XML comments remain ordered content
nodes and are not reassigned to adjacent values (§4.5, final bullet).

Where an output format supports comment nodes as first-class nodes, XML comments are emitted in
place. §20 summarizes the cross-format behaviour: XML output emits ordered XML comments;
namespace, quoted-namespace and YAML output normalize; JSON discards with a summarized
`WARN003`; INI emits only when `SemicolonComments` or `HashComments` is selected in
`inioutputoptions`, otherwise discards with a summarized `WARN003`. When rendering a non-XML
comment as XML, invalid comment sequences are normalized deterministically: every `--` is
separated as `- -`, and a terminal `-` receives one trailing space (§20).

## CDATA

CDATA is a distinct XML node kind (§11.6). XML output preserves imported CDATA as CDATA unless
`CDataAsText` is selected in `xmloutputoptions` (§16.9). If CDATA content contains `]]>`, the
writer splits it into a valid sequence of CDATA and/or text nodes without changing the logical
text (§11.6). On input, adjacent CDATA segments produced solely by such safe splitting are
coalesced into one logical CDATA run; adjacent ordinary text is coalesced separately; CDATA
and ordinary text are not coalesced with each other (§11.6). The next round trip is therefore
stable.

Verified. The input

```xml
<cfg><plain><![CDATA[x < y]]></plain><mixed>before<![CDATA[m]]>after</mixed><split><![CDATA[a]]]]><![CDATA[>b]]></split></cfg>
```

emits, with default options:

```xml
<?xml version="1.0" encoding="utf-8"?>
<cfg>
  <plain><![CDATA[x < y]]></plain>
  <mixed>before<![CDATA[m]]>after</mixed>
  <split><![CDATA[a]]]]><![CDATA[>b]]></split>
</cfg>
```

Two adjacent CDATA segments in `<split>` are the safe split of the logical text `a]]>b`; the
lexical concatenation of their contents contains no lexical `]]>` and cannot be misread by
another XML parser. See `conformance/xml-cdata-survives-a-round-trip`.

CDATA is a spelling of a scalar, not of an identity. A scalar reference carries a value, not a
node kind, so `${r.a}` at a destination written as `<d><![CDATA[${r.a}]]></d>` emits CDATA there
and at `<e>${r.a}</e>` emits ordinary text (§11.4). See
`conformance/xml-a-reference-does-not-import-a-spelling`.

## Whitespace

The default XML input mode is `PreserveWhitespace` (§11.7): every text node survives, including
whitespace-only text between element children. That makes the normalized same-format round trip
byte-stable without an opt-in (§3.3).

Verified. This input round-trips byte-identically under the default:

```xml
<r>
    <a>
        <b>1</b>
    </a>
    <m>t<b>2</b>
    </m>
</r>
```

See `conformance/xml-whitespace-is-preserved-by-default`.

The compatibility mode `NormalizeFormattingWhitespace` is the explicit opt-in for the older
convenience of dropping formatting indentation between element children (§11.7). Under it:

- non-whitespace text is preserved;
- whitespace in mixed content is preserved;
- whitespace under `xml:space="preserve"` is preserved;
- whitespace-only text between element children is discarded.

Because XML has no universal test for insignificant whitespace without a schema or DTD, enabling
`NormalizeFormattingWhitespace` weakens the normalized same-format round-trip guarantee and
emits one `WARN007` per input document that had whitespace discarded (§11.7).

Verified. Under `xmlinputoptions=NormalizeFormattingWhitespace`, the same input above emits:

```xml
<?xml version="1.0" encoding="utf-8"?>
<r>
  <a>
    <b>1</b>
  </a>
  <m>t<b>2</b>
    </m>
</r>
```

with

```text
warning WARN007 §11.7: whitespace-only text between element children was discarded as
formatting indentation, because 'xmlinputoptions=NormalizeFormattingWhitespace' asked for it.
```

`<m>` is mixed content, so its internal whitespace — including the whitespace after `<b>2</b>` —
is preserved as data, and only element-only parents (`<r>`, `<a>`) are re-indented by the writer.
See `conformance/xml-normalize-formatting-whitespace`.

## Format input options

The XML-relevant part of §16.8 is small:

```text
xmlinputoptions=PreserveWhitespace | NormalizeFormattingWhitespace
```

The two values are mutually exclusive; the default is `PreserveWhitespace`; a later complete
directive replaces the earlier one (§16.8, §16.9).

`xmlinputoptions` is **root-level only**. Selector-qualified input-option directives are
blocking `SCHEME001` because input parsing occurs before output instances exist, and a
directive whose scope cannot be honoured would silently continue (§16.8). Writing
`r.xmlinputoptions=NormalizeFormattingWhitespace` — even when the rest of the scheme only
selects the subtree under `r` — is refused. See
`conformance/xml-input-options-are-root-level-only`.

## Format output options

The XML output-options set is §16.9:

```text
[selector.]xmloutputoptions=Indent | NoIndent | NewLineOnAttributes |
                            PreserveCData | CDataAsText |
                            Declaration | NoDeclaration
```

There are four contradictory pairs: `Indent`/`NoIndent`, `NoIndent`/`NewLineOnAttributes`,
`PreserveCData`/`CDataAsText`, and `Declaration`/`NoDeclaration`. The default is
`Indent,PreserveCData,Declaration`. A later
complete directive replaces the earlier flag set; flags from separate declarations do not
accumulate; naming both flags of a pair in one declaration is `SCHEME001`; naming neither
selects that pair's default rather than leaving it unset, because every pair governs a decision
the serializer has to make (§16.9).

Verified behaviour:

- `Indent` inserts two ASCII spaces per element nesting level outside mixed content.
- `NoIndent` inserts no formatting whitespace at all — one line.
- `Declaration` (default) writes `<?xml version="1.0" encoding="utf-8"?>` as the first line;
  `NoDeclaration` omits it. Output is always UTF-8 (§19.5).
- `PreserveCData` (default) keeps imported CDATA as CDATA; `CDataAsText` renders it as ordinary
  text, so the input `<![CDATA[x < y]]>` becomes `x &lt; y`.

`NewLineOnAttributes` places *every* attribute on its own line, including the first, indented two
spaces beyond the owning start tag (§16.9). It was once specified as covering only the attributes
after the first; [#53 (closed)](https://github.com/stop-cran/namespace2xml/issues/53) settled that
against the older wording, and `KNOWN-LIMITS.md` §1.18 records why the clause moved rather than the
code. `conformance/xml-newline-on-attributes` now pins the layout.

Because `NewLineOnAttributes` requires a line break and two spaces before every attribute and
`NoIndent` inserts no formatting whitespace, the two cannot both be honored on any element that
carries an attribute. §16.9 therefore makes them a contradictory pair, and naming both is
`SCHEME001` rather than a combination in which one flag is silently discarded.

## XML output

§19.5 fixes the writer:

- one document element;
- expanded names and required namespace declarations preserved;
- ordered attributes and content preserved;
- mixed content preserved without inserting indentation inside it;
- retained XML comments emitted;
- retained CDATA emitted when configured;
- `element`, `attribute`, `text` and `cdata` types applied;
- structural merge applied before serialization;
- UTF-8 encoding;
- XML declaration under the default `Declaration` option, omitted under `NoDeclaration`;
- normalized indentation outside mixed content by default.

### `type` directives

The XML-specific type values in §16.6 are `element`, `attribute`, `text` and `cdata`. They are
mutually exclusive and pin how a scalar payload at an XML address is written. Legal
combinations with `string` and `multiline` are enumerated in §16.6.

Verified. This scheme, applied to plain namespace input:

```text
cfg.output=xml
cfg.root=doc
cfg.item.@id.type=element
cfg.item.name.type=attribute
cfg.item.note.type=cdata
cfg.item.tail.type=text
```

with data

```text
cfg.item.@id=7
cfg.item.name=widget
cfg.item.note=x < y
cfg.item.tail=trailing
```

emits

```xml
<?xml version="1.0" encoding="utf-8"?>
<doc>
  <item name="widget"><id>7</id><![CDATA[x < y]]>trailing</item>
</doc>
```

`type=attribute` is refused with `TYPE001` for a sequence path, because one XML attribute
cannot carry repeated values (§19.5). Item-descendant `attribute`, `element`, `text` and
`cdata` directives apply normally inside each repeated item element.

### XML sequence projection

XML has no anonymous sequence node. A sequence-valued mapping child renders as **repeated
sibling elements** whose expanded name is the sequence path's final element component (§19.5).

Verified. This JSON input:

```json
{"cfg":{"port":[8080,8081],"server":[{"name":"a","host":"h1"},{"name":"b","host":"h2"}]}}
```

with `cfg.output=xml` and `cfg.root=cfg` emits

```xml
<?xml version="1.0" encoding="utf-8"?>
<cfg>
  <port>8080</port>
  <port>8081</port>
  <server>
    <name>a</name>
    <host>h1</host>
  </server>
  <server>
    <name>b</name>
    <host>h2</host>
  </server>
</cfg>
```

A scalar or null item uses the repeated element's text content; a mapping or XML-element item
uses the repeated element as its containing element and projects its fields normally (§19.5).

An output view whose document root is *itself* a sequence requires `root` with at least two
element components: the preceding components create the wrapping document element and the final
component names each repeated item (§19.5). `cfg.server.root=servers.server` therefore emits
one `<servers>` containing repeated `<server>` items, verified in
`conformance/xml-sequence-projection-covers-mapping-children-scalars-records-and-root`.

At a sequence path (§19.5):

- default or `element` projection emits repeated named elements;
- `text` or `cdata` is allowed only for scalar/null items with an existing containing element
  and emits one ordered content token per item;
- `attribute` is `TYPE001`, because one XML attribute cannot represent repeated values.

An overlay payload plus element children is represented as text or CDATA plus children when the
effective XML type permits mixed content; an attribute payload cannot coexist at the same XML
address with child content, and source-order shape projection selects the winner (§19.5).

Payload text, child elements, CDATA and comments occupy ordered content-token slots by content-
token ordering value, with the stable contribution key breaking any tie. Deep-merged singleton
children retain their earliest token slot while later contributions update their content; later-
only tokens retain source-stream order (§19.5).

## Merge semantics

XML uses the general merge rules (§17.1) adapted to XML node kinds (§17.4). When XML
contributions ultimately target the same destination and have the same expanded root name:

- attributes form an ordered mapping by expanded attribute name and merge recursively as scalars;
- later duplicate attributes override earlier and move to the winning contribution's position;
- each parent is classified once across all destination-level contributions after output-view
  transformations, before folding, so grouping is associative;
- the presence of any text or CDATA node makes the parent mixed-content; comments alone do not;
- for a mixed-content parent, every text, CDATA, comment and child-element node is a sequence
  item and complete content streams concatenate in source order — child elements in mixed
  content do not deep-merge with elements from another contribution;
- for an element-only parent, children are classified by expanded name: if every contribution
  has at most one occurrence, singletons deep-merge; if any contribution has more than one,
  every occurrence forms one sequence and all occurrences concatenate in source order;
- canonical addresses established before output transformations are never reassigned by
  destination-level classification.

Incompatible root names follow the effective `filemerge` strategy (§16.11, §17.4). When XML
destination-fold intent is ambiguous, `filemerge=replace` provides deterministic whole-document
override. Under `replace` the later element's complete value — attributes, content tokens,
comments and children — replaces the earlier element outright, and singleton/sequence
classification and recursive child merging are not applied to the replaced earlier element
(§17.4). See `conformance/xml-filemerge-replace-takes-whole-document` and
`conformance/xml-input-merge-replace-takes-whole-element` for the destination-level and input-
time cases respectively.

## Comments across formats

§20 sums up the cross-format comment behaviour:

| Destination | Comment behaviour |
|---|---|
| XML | Emit ordered XML comments where representable. |
| YAML | Emit normalized leading, inline and trailing comments where representable. |
| namespace / quoted namespace | Emit normalized leading `# comment` lines. |
| INI | Emit only when enabled by `inioutputoptions`; otherwise discard with summarized warning. |
| JSON | Discard with summarized warning (`WARN003`). |

Cross-format association follows source order: comments before the first payload are
document-leading; comments between two payloads become leading comments of the following
payload; comments after the final payload are document-trailing (§20).

## Scalar inference and references

§18 fixes scalar inference for untyped namespace values and is locale-independent:

1. exact case-insensitive `null` becomes null;
2. exact case-insensitive `true` or `false` becomes Boolean;
3. `[+-]?[0-9]+` becomes an arbitrary-precision integer;
4. a JSON-compatible decimal or exponent form becomes an arbitrary-precision decimal;
5. otherwise the value remains a string.

Typed JSON and YAML input scalars retain their source kind without re-inference (§18); typed
values reaching the XML writer via `type=element`, `type=attribute`, `type=text` or
`type=cdata` are rendered in their canonical locale-independent text (§13.2). Numeric source
spelling is never retained.

References use the same lexer (§13, Appendix A.4). Their addressing is described above under
canonical addressing; the two rules worth restating here are that a reference is strictly scalar
(§13.1) — descendants of the referenced path are not copied and a canonical reference to a
comment fails as a non-scalar reference — and that type is forwarded when the value is exactly
one reference and no literal text (§13.2), so `port=${database.port}` remains numeric when the
referent is numeric.

## Round-trip guarantee

§3.3 fixes three guarantees. For XML round trip, "reading and writing the same format must
preserve" data structure, scalar type, key and item order, supported comments, supported XML
node kinds, significant XML text and whitespace, and namespace names. Lexical formatting may
change: indentation, quote style, attribute layout, line endings, and equivalent scalar spelling
need not be preserved unless explicitly stated. §11.8 enumerates what is outside the
preservation contract at all — DTDs, custom entities, processing instructions, schema type
annotations, entity-reference boundaries, exact namespace-declaration placement, exact prefix
choice, exact empty-element spelling and exact attribute quote style.

The cross-format round trip preserves what both formats can represent (§3.3). When a source
concept has no destination expression, it is discarded during rendering with one summarized
warning per output file and feature category — one `WARN003` per format for discarded comments,
not one per comment.

## Escapes

Appendix A.2 fixes name escapes; the XML-relevant subset is:

- `\}` inside a `Q{...}` URI is a literal closing brace; every other backslash sequence inside
  the URI is `PARSE001` (§11.4, Appendix A.2);
- `\\` inside a `Q{...}` URI is a literal backslash;
- `\@` at a component start is a literal `@` — an ordinary name part beginning with `@`, not a
  typed attribute component (§8.2);
- `\#` at a component start is a literal `#` — an ordinary name part, not a content token;
- `\Q` at a component start is a literal `Q`, principally to disambiguate an ordinary name
  part beginning with `Q{` from an XML canonical component.

Section 19.1 covers the output side for the namespace writer: leading `@`, `#` or `Q{` in an
ordinary name part is escaped so it will not be re-read as a typed component. Every marker text
that is not itself a typed component reaches the writer already escaped, so the reader that
receives the emitted output cannot mistake it (§8.2, §19.1).

## Deprecated aliases

Two `type` values remain accepted for compatibility with 2.x schemes but are treated as
**no-ops** (§15.3):

- `type=xmlns`;
- `type=xmlnssuffix`.

They emit one `WARN002` per alias category per scheme file and do not affect output.
Verified: a scheme carrying `cfg.item.name.type=xmlns` writes exactly the XML output it would
have written without the directive, with

```text
warning WARN002 §15.3: 'xmlns/xmlnssuffix' are deprecated legacy 'type' values and are
treated as no-ops.
```

If you are migrating a 2.x scheme, delete these lines. The XML namespace on an element or
attribute in 3.0 is a property of the canonical name, addressed as `Q{uri}local`, and does not
come from a `type` directive.

## Traps

The following bite in ways the specification itself explains, but they are worth a heading
because their failure mode is easy to misread.

**Mixed content is a merged-model property, not a per-source one.** An element that is
element-only in one contribution and mixed in another is *mixed* after merge, and its
element-only children move to `#n.name` addressing. Child elements in mixed content do not
deep-merge across contributions, which is why the converted element-only content is placed
above the mixed document's own content-token slots rather than colliding with them (§11.4,
§17.4). If your scheme addresses a child of an element that has become mixed at merge time
and you had authored the address as though it were still element-only, the address no longer
selects anything.

**Whitespace preservation is the default.** `PreserveWhitespace` keeps every text node,
including formatting indentation between element-only siblings. That is what makes the XML
round trip byte-stable without an opt-in (§11.7, §3.3). Opting into
`NormalizeFormattingWhitespace` weakens the round-trip guarantee and emits `WARN007`; it is a
compatibility mode, not the recommended one. If your input round-trips lose whitespace, check
whether you have opted in by mistake.

**Alias ambiguity refuses rather than guesses.** When `<a>` has both an attribute `x` and a
child element `x`, `${a.x}` is `REFERENCE004`, not one of the two candidates. The tool refuses
to pick a winner, because the alias is a convenience for the unambiguous case and no other
answer would be predictable. Write `${a.@x}` or `${a.Q{}x}`. The equivalent scheme-selector
diagnostic, `SCHEME002`, is emitted for the same shape: `a.x.type=string` against an element and
an attribute both named `x` is an error, not a resolution to the element. Its message names both
canonical paths it reached, so the fix is a marked component rather than a guess.

**Singleton-to-sequence promotion re-addresses the singleton.** A single `<b>` addresses as
`a.b`; the moment a second `<b>` arrives — anywhere across contributions — the addresses
become `a.b.0`, `a.b.1`, and `a.b` no longer names a scalar or element. A reference from an
earlier contribution that used to target the singleton scalar becomes a non-scalar or
missing-reference error (§11.4). This is deliberate: silently retargeting `a.b` to the first
repeated child would let an unrelated later contribution change the meaning of a reference in
an earlier one.

**The XML declaration is not a data node.** It is discarded on input and re-emitted on output
under the `Declaration` option (§11.2, §19.5). If the declared encoding disagrees with the
decoded encoding, the disagreement is a blocking `PARSE002`, not `XML002` (§11.2, Appendix B).

**Processing instructions are not preserved.** They are discarded with a summarized `WARN006`
per document (§11.2, §11.8). If a scheme depends on a `<?target ... ?>` PI, the tool has no
vocabulary for it — a feature request is the correct route, not a workaround.

**Empty-element spelling is not preserved.** `<a/>` and `<a></a>` are the same XML — the
writer picks one form, and either form on input yields the same overlay (§11.8). Same for
prefix choice when several prefixes name the same URI: the canonical model is the URI, not
the prefix, and a scheme selector must use `Q{uri}local` rather than `p:local` to match a
namespace-qualified name.

**`xmlns` and `xmlnssuffix` are no-ops.** If they are in your 2.x scheme, they do nothing in
3.0 (§15.3). Delete them, and use `Q{uri}local` for XML namespaces.

## Where to go next

- The general document model behind the XML nodes is §4.2 (overlay nodes) and §4.6 (XML nodes).
- Merge semantics for XML across input contributions and destination folds are §17.1 and §17.4.
- Universal source order, precedence and stable ordering values are §5.
- Diagnostic codes referenced above (`XML001`, `PARSE002`, `TYPE001`, `WARN003`, `WARN006`,
  `WARN007`, `REFERENCE004`, `WARN002`) are in [diagnostics.md](diagnostics.md).
- Worked XML fixtures live under
  [`conformance/`](../conformance) with names beginning `xml-` or `limit-xml-`; each has a
  `legacy.md` explaining what it pins and why.
