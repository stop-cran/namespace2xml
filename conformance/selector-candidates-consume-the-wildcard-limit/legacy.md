# Selector candidates consume the wildcard limit

Section 12.4 names three categories that consume the shared candidate-check limit -- "generative
templates, permanent wildcard ignore masks, and wildcard scheme selectors" -- and closes with
"every wildcard rule category consumes the shared candidate-check limit once per eligible pair".
The third category is the one asserted here, and it is the one most easily left out: a selector is
expanded in the planning phase, a phase away from where the other two are evaluated, and the limit
is easy to read as belonging to the fixed point that spends most of it.

## Why the third category needs its own fixture

The other two categories are evaluated together, inside the Section 12.3 fixed point, and a single
budget threaded into that evaluator covers both. A wildcard output selector is expanded at
Section 15.1 step 13 against the finished model, by different code, and nothing about the fixed
point's accounting reaches it. An implementation can therefore satisfy every existing wildcard-limit
fixture and still expand a selector over an unbounded number of items.

That is a resource bound rather than a rendering rule, so no expected output tree can show it. Only
a run that crosses the bound can.

## Where the charge falls

Section 12.4 states the three conditions under which a check is counted:

1. the item has at least the number of parts required through the rule's last wildcard-containing
   part;
2. every literal name part before that point equals the corresponding item part;
3. the pair has not previously been considered.

and then: "Full capture matching may then succeed or fail without another candidate charge."

The charge is levied on eligibility, before the outcome of matching is known, exactly as it is for a
mask. A selector that examines an item has spent the check whether or not the captures agree.

## This case

`a.*` has its last wildcard-containing part at depth 2, so the eligible items are the distinct
depth-2 prefixes of existing paths whose first part is `a`: `a.x` and `a.y`. `b.keep` fails
condition 2. That is two eligible pairs.

`--max-wildcard-candidates 1` admits one of them. The second crosses the bound, which Section 12.4
reports as `WILDCARD002` naming the responsible rule -- here the selector, spelled as it was
written. Appendix B maps a crossed wildcard bound to exit code 1, and no output is produced.

The phase is `planning` rather than `input` because Section 15.1 places selector expansion at step
13. The same code reported from two phases is intended: Section 22 makes `phase` a property of where
the condition was detected, not of the code.

The limit is 1 rather than 2 so the assertion is about the count. At 2 the run succeeds, and the
fixture would pass against an implementation that charges nothing.

## Why `b.keep` is present

Without it the fixture would also pass against an implementation that charges one per node at
depth 2 across the whole tree, which is the wrong rule but agrees on this input. `b.keep` is a
depth-2 path that condition 2 excludes: an implementation charging it would cross the bound one item
earlier and still report `WILDCARD002`, so this file does not distinguish the two on its own. The
unit test `OnlyItemsUnderTheLiteralPrefixAreCharged` carries that half, where a green run is the
observable and a fixture cannot express it.
