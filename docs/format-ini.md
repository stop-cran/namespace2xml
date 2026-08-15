# INI format guide

INI is **output only**. The tool can write INI; it cannot read it. Every other format on the
support matrix (§7.1) is round-trip; INI is the one asymmetric row, and the entire shape of this
guide follows from that.

If the phrase "one-way" reads as a minor caveat, read the first trap first. The tool will not
refuse a `-i config.ini` — it will silently misparse the file whenever the file happens to be
valid namespace-profile text, which INI text often is.

The output dialect is named `PortableIni1` (§19.6). This document is the guide to what it is, how
depth is projected onto its two-level structure, and what to do about the interoperability gap
recorded at the end.

## `-i config.ini` does not read INI

§7.1 fixes the input-extension set at `.json`, `.yaml`, `.yml`, and `.xml`. Every other extension,
including `.ini`, selects **namespace-profile parsing**. This is not a warning; it is the
compatibility fallback the section names as normative.

The two failure modes are unpleasant in opposite ways.

A syntactically real INI file, one with a `[section]` header, fails at the first section marker
with `PARSE001` at line 1 column 1 — the profile parser tries to read `[db]` as an entry, does not
find a separating `=`, and reports it as malformed. The message says nothing about INI, because
nothing in the tool has classified the file as INI; the reader is a namespace-profile reader that
happens to be pointed at the wrong file. Verified:

```text
$ dotnet namespace2xml.dll -i config.ini -s scheme.txt --output out
config.ini(1,1): error PARSE001 §8.1: this record is neither a comment nor a mask and has no
separating '=', so Section 8.1 rule 5 makes it a parse error.
```

A **sectionless** INI file — `key=value` lines only, which the `PortableIni1` dialect emits for
any input with no path depth beyond one — is *valid* namespace-profile text. The tool parses it,
succeeds, and produces output that is wrong in a way nothing on standard error mentions:

```text
$ cat config.ini
host=localhost
port=5432

$ dotnet namespace2xml.dll -i config.ini -s scheme.txt --output out
$ cat out/output.json
{
  "host": "localhost",
  "port": 5432
}
```

That output is coincidentally reasonable because a flat INI preamble and a flat namespace profile
happen to share a spelling. It is not the tool reading INI; it is the tool reading a namespace
profile that resembles one. A file with `[section]` headers, comment lines beginning with `;`, or
duplicate keys will not survive the coincidence.

Native INI input is out of scope for 3.0 (§7.1). If a downstream tool needs to consume INI, do the
INI→namespace-profile conversion outside this tool.

## The projection

INI is a two-level format: sections, then keys under a section. The overlay model is a tree of
arbitrary depth. §19.6 fixes exactly how the tree is flattened into that two-level shape, and the
rule is short:

- a surviving scalar path with **one part** becomes a **global key** in the preamble;
- a path with **two or more parts** puts the final part in the key position and joins every
  preceding part with the configured INI delimiter to name the section;
- container-only paths do not emit keys;
- `root` (§16.3) is applied before this split, so a `root=x.y` prefixes both parts onto every
  path.

The default nested-section delimiter is `:` (§16.4).

### Depth beyond two levels

There is no third level in the output. A path of depth three, four, or more all still projects to
one section header and one key, with every intermediate part folded into the section name by the
delimiter. Verified:

```text
# profile
a.b.c=1
a.b.c.d.e=2

# scheme
a.output=ini

# a.ini
[b]
c=1
[b:c:d]
e=2
```

If you want the intermediate structure to appear as nested sections, INI cannot do it. Use JSON,
YAML, or XML. The dialect is called `PortableIni1` because this projection is what interoperates
— some INI parsers understand `[db.host]` or `[db:host]` as nesting; more do not — and picking the
delimiter-joined single header is the choice that reads back correctly on the most parsers,
whether or not those parsers recognise the nesting convention.

### Global keys in a preamble

All globals emit before any section. This is a **format projection**, not a precedence change
(§19.6): value precedence is settled by §5.2 mapping order and §4.4 shape contributions, and
hoisting the globals ahead of sections is a rearrangement of the same emission stream. Global
keys keep their winning source order among themselves.

A preamble is the one construct in `PortableIni1` that a widely deployed reader may refuse
outright — Python's `configparser` raises `MissingSectionHeaderError` on the first key rather than
skipping it, so one one-part path makes the whole file unreadable to it. A destination that writes
a preamble therefore emits `WARN012`, once per output instance, naming the destination.

Two directives remove both the preamble and the warning:

- `inioutputoptions=GlobalSection` writes the global keys inside a section named `global`, placed
  where the preamble would have been and keeping their winning source order. Nothing else changes:
  same keys, same order, same values. The name is fixed — `root` already covers the configurable
  case for every format — and it is deliberately not `DEFAULT`, because `configparser` inherits
  `[DEFAULT]` keys into every other section on read-back, which would change the document's meaning
  in the reader the name was chosen for.
- `root` (§16.3) wraps the whole output view in a section of your choosing, for every format the
  declaration produces rather than for INI alone.

When no global key survives, `GlobalSection` writes no header at all; there is no empty `[global]`.
A path of two or more parts that already projects to a section named `global` collides with the
hoisted section and is a blocking `FLAT001`, naming the first such path — the two have different
origins, and merging them would make the file's content depend on the name the option chose rather
than on the paths you wrote. Without `GlobalSection`, a `global` section is an ordinary section and
nothing collides.

```text
# profile                # scheme                                # app.ini
app.name=demo            app.output=ini                          [global]
app.db.host=localhost    app.inioutputoptions=GlobalSection      name=demo
app.port=8080                                                    port=8080
                                                                 [db]
                                                                 host=localhost
```

Verified by the conformance cases `an-ini-preamble-is-announced`,
`ini-global-section-hoists-the-preamble`, and
`ini-global-section-collides-with-an-existing-section`.

### Section order

A section takes the position of its **first key** in the §19.1 emission order. §19.1's pre-order
walk emits a node's own scalar before anything beneath it, so a section whose parent path has a
scalar payload later in mapping order can precede its parent's own section header. This is
deliberate, and the specification is explicit: ordering sections by tree position would require
the INI writer to consult structure the projection has already discarded, and INI readers ascribe
no meaning to section order.

The conformance case `ini-projection-and-section-order` pins this and is the case to look at when
the rule seems surprising. Its `app` profile writes both a global key and a nested section under
`db`, along with siblings whose section headers appear in a non-alphabetical order that
nevertheless follows the specification exactly. Verified against the fixture:

```text
# profile
app.name=demo
app.db.host=localhost
app.db=primary
app.port=8080
app.z.k=1
app.a.k=2
app.p.x.y=1
app.p.q=2

# scheme
app.output=ini

# app.ini
name=demo
db=primary
port=8080
[db]
host=localhost
[z]
k=1
[a]
k=2
[p:x]
y=1
[p]
q=2
```

Three things are worth noting in that output. `db=primary` and `[db]` coexist: §19.6 permits a
scalar INI key and a descendant section to be emitted for the same logical path when their
projected identities differ, and this is not a shape warning. `[z]` precedes `[a]` because
`z.k=1` is the earlier line in the profile, and section order follows the first key. `[p:x]`
precedes `[p]` because the container child `p.x.y=1` was declared before the scalar sibling
`p.q=2`, so `p:x`'s first key wins the earlier emission position — and yes, this puts a "nested"
section header before its "parent" in the file. INI does not care.

### `root`

`root=x.y` (§16.3) prefixes `x` and `y` as section-path parts before the two-level split. A
one-part path becomes `[x:y]` with the original part as the key; a two-part path `s.t` becomes
`[x:y:s]` with key `t`. Verified against the same conformance case's `cfg` half:

```text
# profile
cfg.k=1
cfg.s.t=2

# scheme
cfg.output=ini
cfg.root=x.y

# cfg.ini
[x:y]
k=1
[x:y:s]
t=2
```

### Sequences

INI has no array syntax. §8.7 answers what happens: for INI (and namespace) the writer displays
**fresh dense indices** at the section-or-key position where the projection requires them,
while matching and precedence continue to use the stable ordering values.

A sequence of scalars becomes global keys named `0`, `1`, `2`. A sequence of mappings becomes
sections named `[0]`, `[1]`, `[2]`. Gaps close on the way out. Verified — the profile below has
ordering values `0` and `5` and the emitted file has dense keys `0` and `1`:

```text
# profile
p.0=a
p.5=b

# scheme
p.output=ini

# p.ini
0=a
1=b
```

If you need a way to identify sequence items for a downstream INI reader, use the scheme `key`
directive (§16.5) to project the sequence into a mapping of records first.

### Bare-scalar output root

For an output whose root is a bare scalar — `k=1` with scheme `k.output=ini` — the specification
says INI "retains the final concrete selector part as a global key" (§19.6), so the file is
`k=1`. Verified.

Adding `root` on top of that keeps the key and moves it into a section, because §19.6 makes `root`
parts section-path parts rather than part of the key text and §16.3 says `root` "wraps; it never
renames". So `k.root=s` emits:

```ini
[s]
k=1
```

and `k.root=x.y` emits `[x:y]` with the same `k=1`. The key is never spent on a root part. This was
[#54](https://github.com/stop-cran/namespace2xml/issues/54), resolved in favour of §19.6; earlier
previews emitted `s=1` and `[x]` / `y=1`.

## Values

The value grammar of `PortableIni1` is defined against §18 canonical scalar text and §4.3 scalar
kinds. Once the payload at a node has a kind and a canonical text (§18), INI writes that text as
the value.

- `null` writes `null` literally (§19.6). `PortableIni1` has no separate null encoding, so
  spelling null as an empty value would collide with a legal empty string.
- `true` and `false` write as those five- and four-character sequences.
- Integers and decimals write their §18 canonical text, which is locale-independent — the
  decimal `1.5e2` becomes `150.0`, not `150,0` or `150.00`. Verified.
- Strings write as unquoted single-line UTF-8 by default.

### What the default rejects

`RejectMultiline` is the default `inioutputoptions` value (§16.9). Under it:

- NUL, CR, and LF in a value are `INI001`;
- a value beginning with `;` or `#` is `INI001` — it would read back as a comment on any parser
  that respects either marker;
- a value with leading or trailing whitespace is `INI001` — most parsers strip it silently on
  read, so writing it unquoted would not round-trip.

Verified (default options):

```text
# profile
k=;hi

$ dotnet namespace2xml.dll -i profile.txt -s scheme.txt
error INI001 §19.6: the value at 'k' cannot be written: a value beginning with ';' or '#' reads
back as a comment, so Section 19.6 requires 'QuoteValues' for it.
[path: k] [destination: output.ini]
```

### `QuoteValues`

Selecting `inioutputoptions=QuoteValues` emits every value inside double quotes, escaping `\` as
`\\` and `"` as `\"` (§19.6). Values with leading or trailing whitespace, or values beginning
with `;` or `#`, become writable this way. Verified:

```text
# scheme
output=ini
inioutputoptions=QuoteValues

# profile             # output.ini
k=;hi                 k=";hi"
k=hi "you"            k="hi \"you\""
```

`QuoteValues` does not by itself make CR or LF writable — the value still rejects at `INI001`
under `RejectMultiline`, which is the default multiline mode.

### `EscapeMultiline`

Selecting `inioutputoptions=EscapeMultiline` swaps `RejectMultiline` out for the escape mode, in
which LF becomes `\n`, CR becomes `\r`, and tab becomes `\t` (§19.6). §19.6 also fixes an
ordering rule that catches consumers out: **a literal backslash is emitted as `\\` before the
LF/CR/tab escaping happens, whether or not `QuoteValues` is also selected**. That means an
`EscapeMultiline`-only file backslash-doubles every real backslash — for a consumer that does not
know the escape convention, the file is quietly not what the raw bytes suggest.

Verified (`EscapeMultiline` alone, value `hello<LF>world`):

```text
k=hello\nworld
```

Verified (`QuoteValues,EscapeMultiline`, value `hi "you"<LF>next`):

```text
k="hi \"you\"\nnext"
```

The escape table under `EscapeMultiline` is exhaustive and small: `\\`, `\n`, `\r`, `\t`. No
`\u`, no hex, no octal. Anything else that Section 19.6 would reject under `RejectMultiline` is
still an error under `EscapeMultiline` — it addresses newlines, carriage returns, and tabs, and
nothing else.

### Escape context and Appendix A

Namespace-profile input has its own value escape table (§8.3), and INI output has its own
(§19.6). They are independent: `\n` in a namespace profile decodes to a real LF *before* the INI
writer sees the value, and the INI writer then either rejects that LF (`RejectMultiline`) or
emits it as `\n` (`EscapeMultiline`). The INI writer is unaware whether the LF came from an
escape or from a genuine newline in a structured input.

Appendix A does not have an INI-specific escape table for the same reason: namespace-value
escapes are consumed in §A.3 by the profile parser and never reach the writer; INI's own escape
alphabet is given directly in §19.6, which is normative.

Delimiter escaping (§A.6) does not apply to INI — that phase is namespace-output specific.
INI's own name validation is `[A-Za-z0-9_.:-]+` after delimiter joining (§19.6), and characters
outside that set — `[`, `]`, `=`, comment markers, whitespace, control characters — are name
errors, not escapable ones. §16.4 says as much: INI applies its own validation after joining,
not the `\u{HEX}` escape.

## Comments

INI comments are governed by §20 and by the comment-related `inioutputoptions` flags in §16.9.

By default, comments are **discarded** with a summarized `WARN003`. This is not a bug and it is
not silent: it is a design choice, because the `PortableIni1` dialect does not commit to a comment
marker until the scheme selects one. Selecting `SemicolonComments` enables `;`-prefixed comments;
`HashComments` enables `#`-prefixed. They are mutually exclusive. Verified:

```text
# profile
# hi
k=1

# scheme (default options)                  # output.ini
output=ini                                   k=1
                                             (WARN003 on stderr)

# scheme (SemicolonComments)                 # output.ini
output=ini                                   ; hi
inioutputoptions=SemicolonComments           k=1

# scheme (HashComments)                      # output.ini
output=ini                                   # hi
inioutputoptions=HashComments                k=1
```

INI comment placement is limited to full-line leading comments (§20). Trailing and inline
comments from a source that has them (YAML) are normalized to full-line leading comments at the
nearest deterministic position; document-leading comments precede the first global key or
section; a comment attached to the first direct key of a section is emitted after the section
header and before that key. Exact lexical spacing is not preserved.

## File-level collisions

Two output declarations that resolve to the same canonical destination path are folded under
§17.5. For INI, the same-format `filemerge` strategy (default `deep`) merges the two contribution
models before rendering, so `-i a.txt -i b.txt` with both writing to the same `.ini` file merges
their overlays under the ordinary override rules. Cross-format collisions replace the entire
plan under §17.5 rather than merging; INI cannot merge with JSON.

Post-projection key collisions — two distinct logical paths flattening to the same
section-plus-key text — are blocking `FLAT001` (§16.4, §22). If you rely on a non-default INI
delimiter, review your paths for delimiter occurrences inside part names.

## Interoperability

The `PortableIni1` dialect is specified in §19.6, self-consistent, and produces byte-identical
output for identical input on every supported platform. §19.6 additionally asks an implementation
to name the parsers it holds itself interoperable with, to cover each of them in conformance tests,
and to state the reader configuration and the envelope each claim holds within — because a bare
parser name is not a claim anyone can act on or test.

**This document names one parser: Python's `configparser`.** The claim is verified on every run by
`tools/check-ini-interop.py`, which the `ini-interop` CI job runs over the conformance corpus's
expected output. It is stated below in the three parts §19.6 requires.

### The reader configuration

```python
parser = configparser.ConfigParser(
    interpolation=None,          # the dialect has no interpolation; % is ordinary value text
    delimiters=("=",),           # : is a permitted key character, so it cannot also split a line
    comment_prefixes=(";", "#"), # both markers the dialect can emit
)
parser.optionxform = str         # §19.6 emits the key text; it does not case-fold
```

Every one of those settings is load-bearing, and each was measured. Under `configparser`'s
**defaults** the same files are read back wrong in four distinct ways, three of them silently:

| Emitted line | Default `configparser` reads | |
|---|---|---|
| `Host=localhost` | key `host` | key silently folded |
| `a:b=1` | key `a`, value `b=1` | key silently split |
| `ratio=100%%` | value `100%` | value silently rewritten |
| `ratio=50%` | `InterpolationSyntaxError` | file rejected |

Three of those four produce a successful parse and a different document, which is why §19.6 asks
for agreement rather than acceptance and why the check re-serializes what the parser returned
instead of merely confirming that it did not raise.

### The envelope

The claim holds for a file that satisfies all of the following. Outside it, this document makes no
interoperability claim at all — an option §19.6 defines and `configparser` cannot represent is a
limit on the pairing, not a defect in either.

1. **No preamble.** §19.6 projects a one-part scalar path as a global key written before the first
   section header, and `configparser` has no section to put those keys in: it raises
   `MissingSectionHeaderError` and reads nothing. Select `inioutputoptions=GlobalSection`, or give
   every output path at least two parts. This is the condition that excludes 10 of the corpus's 23
   emitted `.ini` files, and it is what [#88 (closed)](https://github.com/stop-cran/namespace2xml/issues/88)
   added `GlobalSection` for.
2. **Not `QuoteValues`.** `configparser` has no unquoting step, so the quotation marks and any
   `\\` or `\"` escapes arrive as literal characters of the value.
3. **Not `EscapeMultiline`.** `configparser` decodes no backslash escapes, so `\n` arrives as two
   characters. The unconditional backslash-doubling described above compounds this.
4. **No Appendix A escapes surviving into a value.** A value that reaches the file containing
   leading or trailing whitespace, or an embedded control character, is either rejected by
   `RejectMultiline` or requires one of the two options above.

Values may otherwise contain `=`, `;`, `#`, `%`, and `%%`; keys may contain upper case, `.`, `:`,
`-` and `_`. Those were measured to round-trip exactly.

### What the check establishes, and what it does not

`tools/check-ini-interop.py` reads each in-envelope expected file with the configuration above,
re-serializes what `configparser` recovered under §19.6's layout rules, and compares that to the
file's own section and key lines. Anything dropped, folded, split, reordered or rewritten is a
difference. The check imports nothing from this repository, so a defect in the writer cannot define
its own oracle.

It does **not** establish that other parsers agree. `ini4j`, the Windows profile API, `inih` and
Go's `gopkg.in/ini.v1` are not named here and are not tested; each has its own view of quoting,
comments and case. If you are aiming this tool at one of them, treat the envelope above as a
starting point rather than an answer, and test your own reader against the emitted file.

This discharges acceptance item 28 for the named parser. `KNOWN-LIMITS.md` §2.1 records what
remains, which is now the unnamed parsers rather than the absence of any named one, and
[#67](https://github.com/stop-cran/namespace2xml/issues/67) tracks it.
## Traps

- **`-i config.ini` is not an INI reader.** §7.1 sends `.ini` down the namespace-profile
  fallback. A sectioned INI errors at line 1 col 1 with `PARSE001`; a sectionless INI silently
  misparses as profile text and exits `0`. Neither failure message mentions INI. See the first
  section of this guide.
- **Section order is emission order, not tree order.** A nested `[p:x]` can precede its parent
  `[p]` when the container child was declared first. This is §19.6's deliberate choice, not a
  bug — see the conformance case linked above.
- **A scalar and a section can share a name.** `db=primary` and `[db]` can appear in the same
  file, no warning. §19.6 permits it because their projected identities differ.
- **All globals hoist to the preamble.** Global keys appear before any section, even when
  declared later in source order than a sectioned key. This is a format projection with no
  precedence effect. It also emits `WARN012`, because a preamble is what `configparser` and
  parsers like it refuse; `inioutputoptions=GlobalSection` removes both.
- **Sequences densify.** Ordering values `0` and `5` render as `0` and `1`. Downstream code that
  reads INI keys as identifiers will see a different set of keys than the overlay contains.
- **Default multiline mode rejects.** Under `RejectMultiline` (the default), CR, LF, NUL,
  leading `;`/`#`, and any leading or trailing whitespace fail with `INI001`. Select
  `QuoteValues` or `EscapeMultiline` deliberately, not accidentally.
- **`EscapeMultiline` doubles backslashes unconditionally.** Every literal `\` in a value becomes
  `\\` before LF/CR/tab escaping happens, whether or not `QuoteValues` is also on. A consumer
  that does not know the escape convention will read back a value with more backslashes than the
  writer had.
- **Default comment flags discard comments.** `WARN003` is on stderr, but no comment appears in
  the file. Select `SemicolonComments` or `HashComments` if you want them.
- **`PortableIni1` is verified against exactly one parser.** Python's `configparser`, under the
  configuration and envelope stated above and checked on every CI run. No other parser is named
  or tested; `KNOWN-LIMITS.md` §2.1 records what that leaves open.

## Open questions

One thing the specification does not settle that a reader might reasonably ask:

1. **Sequences in INI beyond dense global keys.** §8.7 requires INI to "display fresh dense
   indices where its projection requires indices" but §19.6 does not describe sequence
   projection specifically. The current writer treats a numeric mapping key inside a container
   as a section name and a numeric mapping key at the root as a global key, both densified. That
   reading is consistent with §8.7 and §19.6 taken together, but §19.6 does not explicitly say
   so, and a consumer that relies on it should test the emitted file against its own INI reader
   before shipping.

That is a candidate specification amendment, not an implementation defect.

A second question stood here until [#54](https://github.com/stop-cran/namespace2xml/issues/54) was
resolved: whether `root` over a bare-scalar output root replaced the retained key or wrapped it.
§16.3 now says `root` "wraps; it never renames", and the section on bare-scalar output roots above
gives the resulting files.
