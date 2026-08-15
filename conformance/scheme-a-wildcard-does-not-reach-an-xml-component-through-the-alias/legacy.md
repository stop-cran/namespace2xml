# Legacy differential

- namespace2xml 2.4.0: **fails**. It terminates with an unhandled
  `System.InvalidOperationException: Sequence contains no elements` from `Enumerable.Single` in
  `Formatters/Extensions.cs:79`, exit `-532462766` (`0xE0434352`), writing nothing.
  **verified** — measured against the Appendix C.6 pinned 2.4.0 package.
- Contract: Section 15.2 — "Scheme paths use the same typed component model as canonical data
  paths. An explicitly marked `Q{}`, `@`, or `#n` component selects only that XML component. An
  unmarked component uses the simple alias index for compatibility and convenience; if ordinary and
  XML components make that alias ambiguous at a matched location, selector expansion at pipeline
  step 13 emits blocking `SCHEME002`".
- Legacy observation: the input is `<r><a x="1"><x>2</x></a></r>` — an attribute `x` and a child
  element `x` on one element — and the directive is `r.a.*.type=ignore`. 2.4.0 does not reach a
  diagnostic at all; the wildcard ignore over that shape reaches an internal `Single()` on an empty
  sequence and the process faults.
- Clean behavior: a wildcard does **not** consult the simple alias index. The alias maps a written
  name to the XML components that name could have meant, and a wildcard writes no name to look up.
  So `r.a.*` selects the ordinary component `r.a.x` and not the attribute `r.a.@x`, the element is
  ignored, the attribute is untouched, and the run is clean: `<a x="1" />`, exit `0`, no
  diagnostics.
- This fixture exists to pin the boundary of Section 15.2's alias, and it is a regression test. An
  implementation that folds every unmarked component — a wildcard is not explicitly marked, so the
  reading is available — makes this input **blocking**: the wildcard reaches `r.a.@x` and `r.a.x`,
  those fold to one aliased path, and `SCHEME002` fires. That diagnostic then tells the author to
  "mark the component to name one of them outright", which is the one thing a wildcard cannot do,
  because it was written to match both. The narrower reading is also what keeps `*` meaning the
  same thing in a scheme as Sections 8.6 and 12 give it over data.
