# A JSON scheme nests directive paths

Acceptance items 2 and 18. Section 15, Section 9.1, Section 19.3.

## What the inputs ask for

One namespace profile carries three entries under `app`. One scheme, written as JSON, declares
`output` and `filename` for the selector `app` by nesting them inside an `app` object.

## What Section 15 requires

> Scheme files may use the same case-insensitive format extensions as input files for
> compatibility. Their parsed content must project to qualified directive paths and scalar
> directive values.

and

> The final qualified-name part identifies a directive.

Section 9.1 supplies the projection a JSON document uses:

> Each object-property name becomes one literal qualified-name part. Dots and backslashes in the
> native property name remain literal characters.

Applying the second sentence to the first: `{"app": {"output": "json"}}` spells the two parts `app`
and `output`, the final part `output` identifies the directive, and `app` is what it selects. The
file therefore means exactly what `app.output=json` means in the canonical namespace-profile form.

## The discrimination

Every rule under test is visible in the produced tree, and each is falsified by a different wrong
implementation:

- an implementation that refused the file, or read it as a namespace profile, produces no
  `app.json` at all;
- one that took only the leaf name, discarding the enclosing objects, would declare a root-level
  `output` and write the whole model to the default destination rather than to `app.json`;
- one that treated the whole nesting as the selector — reading `app.output` as a selector with no
  directive — would find no directive at all and write nothing;
- one that projected the JSON *values* into the model, rather than reading them as directive
  values, would emit `output` and `filename` as data.

`db.name` is present so that the selected subtree has more than one child under more than one
parent: a projection that lost a level of nesting cannot produce this tree by accident.
## Why the value is a native string

Section 15 requires "a nonempty scalar value after format parsing", and a JSON string is the
spelling that carries the same characters a profile would. The typed cases — a number, a Boolean,
`null` — are not exercised here; the sibling fixture
`a-container-value-in-a-structured-scheme-is-scheme001` covers `null` and the two container shapes.

## Not asserted

That the file extension is matched case-insensitively, which acceptance item 2 covers elsewhere for
input files and which Section 15 extends to schemes by reference rather than restating. Nor the
YAML projection, which Section 10.4 states separately and the sibling YAML fixture exercises. Nor
XML, which Section 15 permits and never gives a projection for.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 15 and Section 9.1. Section 24 for the difference that remains.
- Legacy observation: the baseline exits 0 and writes `app.json` with the same eight lines, the same
  nesting, the same key order and the same values — every character of the JSON structure agrees.
  It differs only in the bytes between them: 2.4.0 writes CRLF and no trailing newline, where
  Section 24 requires LF and a final newline. **2.4.0 reads JSON scheme files.** The preview that
  refused one was a regression against it, which is what issue #66 records.
- Clean behavior: the same projection, emitted under Section 24's byte contract.
- The difference is intentional, and it is narrow on purpose. This fixture is worth having precisely
  because the baseline agrees: it pins a compatibility claim rather than a change, and a future
  implementation that decided a JSON scheme should mean something else would break a file that has
  worked since 2.x. The `\r` and the missing final newline are Section 24's business and are
  asserted in every fixture, not this one's subject.
