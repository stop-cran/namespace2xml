# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 6.2, and Section 3.1's preservation of the existing option names. The names are
  preserved; the arity spelling is not something 2.4.0 accepted in the first place.
- Legacy observation: the baseline rejects the command line outright. It prints
  `Option 'i, input' is defined multiple times.`, exits `1`, and writes no file, so `seq.properties`
  is missing from its output tree. 2.4.0's CommandLineParser configuration accepts a list option
  once, with its values space-separated, and treats a second occurrence of the same option as a
  usage error.
- Clean behavior: Section 6.2 makes repeated occurrences concatenate in exact token order, so the
  run succeeds and produces the three-item sequence in `-i` order.
- The difference is intentional: `-i a -i b` is the spelling every other command-line tool accepts
  for an ordered list, and rejecting it forced callers to build one space-separated token list. The
  space-separated form still works — it is half of this case's own command line — so the change adds
  a spelling rather than replacing one, and no 2.4.0 invocation stops working because of it.

# Why the case is shaped this way

Acceptance item 74. Section 6.2 states two rules that together decide how several inputs are
spelled:

> Repeated `-i`/`--input`, `-s`/`--scheme`, and `-v`/`--variables` occurrences concatenate their
> values in exact command-line token order.

> a list-valued option accepts values until the next option token; every other option accepts
> exactly one value, and a later occurrence overrides an earlier one.

So `-i a b` and `-i a -i b` name the same ordered input list, and the general "a later occurrence
overrides an earlier one" rule is exactly what the first sentence exempts the list options from.

`args.txt` exercises both spellings in one invocation and separates the two `--input` occurrences
with an unrelated option, so an implementation that only merged *adjacent* occurrences fails here:

```
-i inputs/a.txt inputs/b.txt -s schemes/scheme.txt --input inputs/c.txt
```

It also spells the second occurrence in the long form. The two spellings name one option, so `-i`
and `--input` accumulate into the same list rather than into two.

The assertion is on order, not merely on membership. Each input contributes one sequence item at
ordering value `0`, and `seq.merge=append` rebases each later contribution "onto fresh implicit
ordering values above the current high-water mark" (Section 16.10). The resulting sequence therefore
records the exact order in which the three inputs were consumed:

```
0=a
1=b
2=c
```

A set-valued reading, a reversed list, or a grouping that read `inputs/c.txt` before the two
adjacent values would each produce a different, visible sequence. Asserting a merged mapping instead
would not have distinguished them, because the winning value alone cannot show where the losers were.