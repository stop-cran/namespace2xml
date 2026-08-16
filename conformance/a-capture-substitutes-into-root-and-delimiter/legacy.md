# A capture substitutes into `root` and `delimiter`

Acceptance item 6. Sections 12.1, 14.1, 16.2, 16.3, and 16.4.

## What the inputs ask for

`a.*.output=namespace` expands into the concrete instances `a.db` and `a.web`. Two more wildcard
directives then configure each of them from the same capture tuple:

```text
a.*.root=r*
a.*.delimiter=-*-
```

Section 12.1:

> A scheme directive's value is decided the same way, from the captures its own pattern defines:
> its selector for the output-instance-scoped directives, its path for the path-scoped ones.

The clause names `filename`, `root`, and `delimiter` as its common cases rather than as a closed
list. `filename` is covered by `wildcard-filename-substitutes-the-selector-captures`; this case is
the other two.

## What this asserts

**Both values are substituted, from the instance's own captures.** `a.db` gets the root `rdb` and
the delimiter `-db-`; `a.web` gets `rweb` and `-web-`. A build that substituted into `filename`
alone would have to do something else with these two, and there is no correct something else: the
text `r*` is not a root name and `-*-` is not a delimiter.

**Neither is silently dropped.** This is the failure the case is aimed at. A directive value holding
a wildcard has no literal text, so reducing it to that text yields `null` — the encoding for *no
directive at all*. Both instances would then fall back to the Section 16.3 empty root and the
Section 16.4 default `.`, emitting `x=1` twice, with no diagnostic. Section 6.3 does not admit
configuration that is read, discarded, and not mentioned.

**The instance's default filename still uses the whole concrete selector.** Section 16.2 spells it
"the dot-joined concrete selector", so the two files are `a.db.properties` and `a.web.properties`,
not `db.properties` and `web.properties`. Nothing in this scheme sets `filename`, so the default is
what is being asserted.

**Section 16.3 removes the instance prefix before wrapping.** The content under `a.db` is `x=1`, and
`root=rdb` wraps that rather than `a.db.x=1`: "The concrete output selector prefix is removed
first … The original selector name is not retained unless it is also present in the `root` value."

**Section 16.4 joins with the substituted delimiter.** The parts `rdb` and `x` join as
`rdb-db-x`. No part contains an occurrence of `-db-`, so no `\u{HEX}` escape is emitted here; the
escaping path belongs to `an-asterisk-in-a-directive-value-under-a-literal-selector-is-text`.

## Not asserted

What happens when a substituted `root` value contains a `.`. Section 16.3 gives the value the name
grammar, so a dot in captured data would create a level rather than being encoded as `filename`
would encode it — a real question that this case deliberately does not settle.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 12.1 capture substitution into a scheme directive value; Section 16.2 default file names; Section 16.3 `root`; Section 16.4 `delimiter`; Section 26 item 6.
- Legacy observation: the baseline exits `0` and writes two files whose *contents* agree with the expected tree — `rdb-db-x=1` and `rweb-web-x=2` — so 2.4.0 does substitute captures into `root` and `delimiter` here. It writes them to `db.properties` and `web.properties`, using only the last selector part as the default file name, and its bytes are CRLF-terminated under the Section 24 divergence the corpus records generally.
- Clean behavior: the same two lines, at `a.db.properties` and `a.web.properties`, LF-terminated.
- Why the divergence is the specified one: Section 16.2 defines the non-root default as `<selector>.properties` where `<selector>` "means the dot-joined concrete selector", and adds that it "is always one filename segment, and different selector-part sequences cannot collapse merely because a part contained a dot". Taking the last part alone is what makes `a.db` and `b.db` collide on `db.properties`, which is exactly the collapse that sentence forbids.
