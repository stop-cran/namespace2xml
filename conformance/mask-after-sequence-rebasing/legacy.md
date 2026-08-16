# Legacy differential

- namespace2xml 2.4.0: **nondeterministic**. Ten identical runs of this case on Linux produced
  three different observable results: `a.ini` containing `0=first` three times, `a.ini` containing
  `0=second` six times, and one run that exited `1` and wrote no file at all. The inputs, the
  scheme, the working directory and the environment were the same every time.
- Contract: Sections 8.6, 15.1 step 10, 16.10 `append`, and 5.4 for the rule under test; Section 1
  and Section 24 for the determinism this observation refutes in the baseline. Section 3 does not
  enumerate the ordering-value model that this fixture pins.
- Legacy observation: sequence concatenation had no stated ordering-value model, and the baseline
  resolves it differently from run to run. `0=first` is the result this case expects, so roughly a
  third of baseline runs happen to look correct — which is exactly why a single sample is not
  evidence. Both surviving values are drawn from the two inputs, and the mask `!a.1` removes
  whichever item the run happened to place at index 1, so the instability is in the order the two
  contributions are folded rather than in the mask.
- Clean behavior: `merge=append` rebases the later contribution's items "onto fresh implicit
  ordering values above the current high-water mark", so the second document's only item lands at
  ordering value 1 on every run. The mask names that value and suppresses the item there. This is
  why Section 15.1 applies masks at step 10 and not only when contributions are merged: before the
  merge the item is at ordering value 0 in its own document, and no mask written against its final
  position could match it.
- The difference is intentional: Section 1 requires identical inputs to produce byte-identical
  outputs. An ordering that varies with whatever the baseline's fold happened to observe is not a
  behaviour worth preserving, and a configuration transformer whose output depends on the run is
  not usable in a build.

This case is also the reason Appendix C.6 samples each baseline run more than once. It was first
recorded as `agrees` from a single Windows run that returned `0=first`, and the claim survived
review because it was consistent with everything then measured. Sampling five times per case found
it, and then found a second unstable case — `json-strict-parsing-refusals`, whose minority branch
appears about once in forty runs and whose rarity is why C.6 does not ask the lane to re-derive
this verdict.