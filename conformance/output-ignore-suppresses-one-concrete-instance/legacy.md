# `output=ignore` suppresses one concrete instance, and only that instance

Acceptance item 20. Section 14.2, Section 15.2.

## What the inputs ask for

`a.*.output=namespace` creates three concrete instances -- `a.x`, `a.y` and `a.z` -- and the
next line writes `a.y.output=ignore`.

Section 15.2 says the two declarations are not independent:

> exact and wildcard declarations that literalize to the same concrete selector participate in one
> source-ordered override stream

`a.*` literalizes to `a.y` among others, so the exact declaration and the wildcard's `a.y`
member are one stream, and the exact declaration is later in source order. It wins. Section 14.2
then settles what a winning `ignore` produces:

> A selector whose winning declaration is `output=ignore` creates no output instance and no
> reference-reachability root.

## What the expected tree asserts

Two files, `a.x.properties` and `a.z.properties`. The **absence** of `a.y.properties` is the
substance of the case, and the harness compares the whole produced tree, so an extra file fails.

Without the override stream the two declarations are two instances, both of which believe they own
`a.y`: the wildcard member writes the file and the ignore member quietly writes nothing beside it.
The run is green, the exit code is 0, no diagnostic is emitted, and the scheme's instruction has been
disregarded. Nothing but the tree comparison notices.

`a.x` and `a.z` are present because Section 15.2 scopes the suppression to "one concrete output
instance" and adds that `output=ignore` "never removes data from another output instance". A fold
that suppressed the whole wildcard declaration would delete all three files and would also pass a
fixture that only asserted `a.y` was gone; the two survivors are what distinguishes the specified
behaviour from that one.

## Not asserted

The publication order of the two surviving files. Section 21.3 fixes it, but with one file per
destination nothing here can observe it.