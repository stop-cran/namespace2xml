# Legacy differential

- namespace2xml 2.4.0: **crashes**. It reads both inputs and then exits `1` with
  `Error parsing input: unexpected 'r', file: inputs/over.txt, line: 2, column: 1`, writing no
  output at all. **verified** — measured against the Appendix C.6 pinned 2.4.0 package.
- Contract: Section 11.4 canonical XML addressing, its `Q{}local` explicit spelling, and its
  `WARN011` clause; Section 13.1 for the simple alias the warning is named after.
- Legacy observation: line 2 is `r.canon.Q{}z=dev`, and 2.4.0 has no `Q{...}` production, so the
  whole run fails on the one line whose purpose is to say "the element, not the attribute" — there
  was no way to say it. Deleting that line makes the rest measurable, and it exits `0` writing
  `<r>\n  <both x="" />\n  <canon z="base" />\n  <attr y="dev" />\n</r>` with CRLF endings. Three
  separate things are visible there. `r.attr.y=dev` **overrode the attribute**, which is the 2.x
  behaviour this case's warning exists to talk about. `<both x="1"><x>2</x></both>` came back as
  `<both x="" />`: the child element was collapsed into an attribute of the same name, so the
  attribute's value `1` and the element's text `2` were both destroyed by a document that merely
  passed through. And `attr` moved to the end of the parent, because touching an element from a
  profile reordered it.
- Clean behavior: Section 11.4 makes `@y` and `y` different components, so `r.attr.y=dev` adds the
  ordinary component beside the attribute rather than replacing it, and `WARN011` reports that the
  contribution "does not override the existing one" and names `r.attr.@y` as the address that
  would. The two negatives are the point of this case. `both` carries an attribute `x` and a child
  element `x` that arrived in one XML document, and Section 11.4 excludes it — "components arriving
  together in one contribution never warn, since a single XML document may legitimately carry an
  attribute and a child element of the same name". `r.canon.Q{}z=dev` names the element outright,
  and Section 11.4 has a marked component "bypass that index and name one canonical component
  outright", so it is not a mistaken override and is not reported either. All three elements keep
  every value both inputs supplied, and Section 5.2 keeps them in the order the XML document gave
  them.
- The difference is intentional. 2.4.0's override is the more convenient behaviour and the reason
  the hazard exists at all: a 2.x profile that specialized an attribute reads as though it still
  works. What 3.0 will not do is guess, because the same spelling has to serve documents where the
  element is meant; `both` is that document. So the address stays unambiguous and the run says
  which of the two it took, which is the one thing 2.4.0 could not do — it destroyed `both`
  silently in the same pass.
