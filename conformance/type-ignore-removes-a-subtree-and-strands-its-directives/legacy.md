# `type=ignore` removes a subtree and strands the directives beneath it

Acceptance item 18. Sections 16.6 and 15.2.

## What the inputs ask for

A scheme that ignores `cfg.a` and then asks for a transformation at `cfg.a.p`, which is inside the
ignored subtree.

Section 16.6:

> Removes the complete matched overlay subtree, including payload, all container projections,
> descendants, and comments, from the selected output instance only.

and

> An effective ignore at path `P` removes every descendant regardless of directives matching those
> descendants. Only a later complete non-ignore type set matching `P` itself restores `P`. A
> directive matching only a descendant of an ignored path is inert and emits the unbound-directive
> warning from Section 15.2.

Both halves are asserted. The file contains only `b=3`, so the whole of `cfg.a` is gone including
the scalar two levels down. And one `WARN009` is emitted for the stranded directive.

## Why the stranded directive is a warning rather than an error or a silence

`cfg.a.p.type=array` is not wrong in itself; `cfg.a.p` is a mapping and the conversion would have
succeeded. It is inert only because of where it sits relative to the ignore, and a reader who wrote
it expected it to do something. Section 15.2 makes that a warning:

> A selector-qualified `filename`, `root`, `delimiter`, output-options, `filemerge`, or output-view
> transformation that binds to no concrete output instance emits one scheme warning and is otherwise
> inert.

Silence would leave the author with a directive that appears to be honoured. An error would refuse a
scheme that has one redundant line in it, which is not what the specification asks for.

The order matters and is asserted implicitly: the ignore is applied first, so the directive beneath
it is never evaluated against data. An implementation that applied `type=array` before `type=ignore`
would still produce this file, but it would have no reason to warn.

## Why the diagnostic carries these members and not others

- `source` and `line` — the stranded declaration itself, at line 3.
- `path` — `cfg.a.p`, the scheme path that bound to nothing live. It is the directive's own path
  rather than the ignored ancestor, because the finding is about this directive.
- `declaration` — `cfg.a.p.type`, the directive's canonical spelling.
- `column` — absent. The condition is about the whole record.
- `destination` — absent. A file is produced, but the finding is not about that file; the same
  warning would be emitted if the directive were stranded in every instance.

The anchor is `§15.2` rather than `§16.6`. `WARN009` covers two conditions, and Section 14.1's
wildcard-output condition is the other one; the anchor is what distinguishes them, and Section 16.6
names Section 15.2 as the source of this warning rather than defining a second one.

## Not asserted

That the ignore does not "mutate the common model", which needs a second output instance to observe
and is a separate case.

`type=default` restoration, which Section 16.6 also describes and this fixture does not exercise.

The exit code is 0: `WARN009` is a warning, and Section 6.3 reserves a nonzero code for a blocking
error.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.1 preservation of scheme-directive precedence and the `type=ignore` name; Section 3.2 corrections against parallel-execution-order dependence and against unhandled user-input conditions; Section 16.6 `type=ignore` scope and Section 15.2 unbound-directive warning; Section 26 item 18.
- Legacy observation: the baseline exits `0` and writes a `cfg.properties` whose bytes differ from the expected file. The measurement records `content cfg.properties`. Standard error is empty beyond the banner.
- Clean behavior: `type=ignore` at `cfg.a` removes the whole subtree from this output instance and the stranded `cfg.a.p.type=array` directive is inert but is reported once as a `WARN009` scheme warning. The rendered file therefore contains only `b=3`, exit `0`.
- Why the difference is intentional: the *shape* of the tree matches -- one `cfg.properties` at exit `0` -- but the bytes differ, so what the baseline wrote to it is not what the specification prescribes. Section 16.6's rule "an effective ignore at path `P` removes every descendant regardless of directives matching those descendants" is a specification decision about which directive wins at a path an ignore covers; without that rule an implementation is free to evaluate `type=array` on `cfg.a.p` in whichever order its passes happen to visit them, and the resulting bytes depend on that accident. The content divergence here is exactly the observable Section 3.2's "dependent on parallel execution order" and "dependent on dictionary iteration order" corrections were written to remove. The stranded-directive warning is not observable in the tree comparison and is not scored by this verdict.
