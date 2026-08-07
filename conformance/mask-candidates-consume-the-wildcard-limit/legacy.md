# Mask candidates consume the wildcard limit

Section 12.4 names the three categories that consume the shared candidate-check limit -- "generative
templates, permanent wildcard ignore masks, and wildcard scheme selectors" -- and closes with "every
wildcard rule category consumes the shared candidate-check limit once per eligible pair". A mask is
one of the three, so a run whose only wildcard rule is a mask is subject to the limit.

## Where the charge falls

Section 12.4 states the three conditions under which a check is counted:

1. the item has at least the number of parts required through the rule's last wildcard-containing
   part;
2. every literal name part before that point equals the corresponding item part;
3. the pair has not previously been considered.

and then: "Full capture matching may then succeed or fail without another candidate charge."

The charge is therefore levied on eligibility, before the outcome of matching is known. A mask that
suppresses an item is charged for it, exactly as a template that fails to match one is. Charging
only the items a mask leaves alone would make the limit measure the mask's failures rather than its
work, and would leave the one shape a mask is normally written in -- one that matches -- entirely
unbounded.

## This case

`!a.*` has its last wildcard-containing part at depth 2, so by Section 12.4's own accounting rule
the eligible items are "the distinct depth-`k` prefixes of existing paths": `a.x` and `a.y`. `b.keep`
fails condition 2, because the literal first part `a` does not equal `b`. That is two eligible
pairs.

`--max-wildcard-candidates 1` admits one of them. The second crosses the bound, which Section 12.4
reports as `WILDCARD002` naming the responsible rule. Appendix B maps a crossed wildcard bound to
exit code 1, and no output is produced.

The limit is set to 1 rather than 2 so that the assertion is about the count and not merely about
the mask being present in the worklist at all: at 2 the run succeeds, and the fixture would pass
against an implementation that charges nothing.

## Why the count is not observable through the model

Section 8.6 discards a masked contribution "before literal-path merge validation", so by the time
the fixed point runs, `a.x` and `a.y` are gone. An implementation that charges only what it can see
at that point charges nothing here, and this fixture is the difference between the two readings.

Section 8.6's "suppressed paths and descendants never become wildcard candidates" is not in tension
with this: it governs what a *later* rule may match, which is why a generative template is charged
for none of them. The mask's own check on an item is what made that item suppressed.