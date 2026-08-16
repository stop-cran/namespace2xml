# Masks and the Section 4.4 shape marks

Section 4.4 defines the effective mapping shape-mark as "the latest **surviving** explicit
mapping-presence or descendant contribution that requires mapping shape", and Section 8.7 fixes the
word: "*Surviving* means not suppressed by a permanent mask."

A mask therefore does not merely delete a subtree. It withdraws the evidence that subtree supplied
for its ancestors' shape, and the Section 4.4 exclusive-shape contest has to be settled on what is
left.

`kept` is the case that costs data when it is got wrong. A native sequence arrives first, a
mapping child arrives later, and the mapping child is then masked. Section 17.1 would keep the
later mapping and drop the sequence -- but after masking there is no later mapping, because the
only contribution that ever required mapping shape at `kept` has been suppressed. The surviving
sequence is the sole container contribution, so it wins and the item is emitted. An implementation
that copies the mark across the prune emits an empty file instead, and the diagnostic it raises
points at a conflict between a real sequence and a contribution that no longer exists.

`phantom` is the same rule one level further out. `phantom.gone` exists only because
`phantom.gone.deep` needed a container, and the mask removes the whole subtree. Nothing addressed
`phantom.gone` in its own right, so once its descendant is gone it is not a surviving
contribution at all and cannot refresh the mapping shape-mark of `phantom`.

Neither root emits a diagnostic: a conflict requires two surviving projections, and after masking
each root has exactly one.

## Legacy differential

- namespace2xml 2.4.0: **differs**. The baseline writes `phantom.properties` with different
  content than the case expects (the harness records `content phantom.properties`); `kept` and
  the exit code match.
- Contract: Section 4.4 defines the effective mapping shape-mark as "the latest **surviving**
  explicit mapping-presence or descendant contribution that requires mapping shape", and
  Section 8.7 fixes the word — surviving means not suppressed by a permanent mask. Section 3
  does not enumerate this defect; it is a new precise statement rather than a preservation or
  correction.
- Legacy observation: 2.4.0 had no defined surviving-mark rule at Section 4.4. On `phantom` the
  only contribution requiring mapping shape came from a descendant that is entirely masked, so
  once the descendant is gone there is nothing left to require the mapping shape — but the
  baseline retains the mark set before masking, and the `phantom.properties` bytes reflect that
  retained mark rather than the empty projection the specified rule leaves. The measurement
  records only that the bytes differ; the exact content 2.4.0 produces is implementation-
  defined for the baseline.
- Clean behavior: after masking, `phantom.gone` is not a surviving contribution and cannot
  refresh `phantom`'s mapping shape-mark. The projection is empty, and no diagnostic fires
  because a conflict requires two surviving projections. `kept` agrees on both baselines: the
  masked contribution is not what discriminates that root, so the surviving-mark subtlety does
  not reach the emitted bytes there.
- The difference is intentional: a mask that only deletes data would leave phantom containers
  behind wherever a suppressed subtree had shaped its ancestor, so the specification withdraws
  a masked contribution's shape evidence at the same step it withdraws the data.
