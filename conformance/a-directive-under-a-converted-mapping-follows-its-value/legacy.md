# A directive under a converted mapping follows its value

Acceptance item 54. Section 15.1 step 16, Section 16.6, Section 15.2.

## What the inputs ask for

`hosts` is a mapping with two named children. `hosts.type=array` converts it, and Section 16.6 says
what that costs: "every key is discarded and every child ... receives a fresh implicit ordering
value". Two further directives are bound *beneath* those discarded keys — `hosts.web.line` selects
`multiline`, and `hosts.db.tag` selects `string` — and a fifth directive is bound at an address that
exists only after the conversion.

## The rule the case is about

Step 16 is a sequence of passes, and `type=array` runs before `multiline`. By the time the
`multiline` pass walks the view, the node the author called `hosts.web` is `hosts.0`. Section 15.1
says what a directive addressed to the old spelling does then:

> a directive bound beneath such a node is re-addressed together with the value it bound to, exactly
> as Section 17.5 re-addresses the per-path high-water map

So `multiline` still joins the two lines into `alpha\nbeta`, and `string` still reaches the
serializer for a value that is now the sole field of the second item. Section 16.6 places the
explicit scalar types outside the passes entirely — they "select an XML rendering for a scalar" and
force string rendering of one, and are read at serialization — which is why the case asserts one of
each: a pass that must find its target, and a table lookup that must find its entry, are two
different mechanisms and both are re-addressed.

The quoted `"7"` is the whole assertion for the second. An unquoted `7` would mean the directive was
looked up under a spelling `type=array` had already destroyed.

## The other half of the rule

`hosts.0.tag.type=string` names the address the conversion produced. Section 15.1 forbids acquiring
it:

> No directive acquires a path that step 16 created: a directive spelling an address that exists
> only after a reshaping matches nothing at step 11, binds nowhere, and emits the Section 15.2
> warning.

The expected stream therefore carries exactly one `WARN009` for that declaration and nothing for the
two that did bind. Without it the case would be satisfied by an implementation that re-matched every
rule after each pass, which produces the same tree here and is the restart Section 15.1 forbids.

## Not asserted

Where `root` lands, which is not a pass; and re-addressing across `key`, which
`a-directive-under-a-generated-record-follows-its-value` covers.

## Legacy differential

- namespace2xml 2.4.0: **differs**, in three ways at once.
  - The two items come out in the reverse order, `tag` before `line`, so the conversion does not
    order items by the mapping order the children were read in.
  - `line` is emitted as `null` and the value is destroyed, with `warn: Multiline value type is not
    supported for JSON` on standard error. Section 16.6 gives JSON a rendering for a joined value —
    "JSON emits a JSON string whose line breaks serialize as `\n`" — so there is nothing here to
    decline.
  - Exit `0` in both cases, so a consumer reading only the status sees a successful run that wrote a
    null where a value was.
- Contract: Section 15.1 step 16 re-addressing; Section 16.6 `array` and `multiline`; Section 15.2
  unbound-directive warning.
- The baseline's `"tag": "7"` is quoted, so it applied that directive; the case does not record why,
  because it pins the correct answer rather than reconstructing the wrong one.
