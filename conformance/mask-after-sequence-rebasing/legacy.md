# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 8.6, 15.1 step 10, 16.10 `append`, and 5.4.
- Legacy observation: sequence concatenation did not have a stated ordering-value model, so there
  was no defined answer to what an ignore pattern written against an index of a concatenated
  sequence removed, or whether it removed anything at all.
- Clean behavior: `merge=append` rebases the later contribution's items "onto fresh implicit
  ordering values above the current high-water mark", so the second document's only item lands at
  ordering value 1. The mask names that value and suppresses the item there. This is why Section
  15.1 applies masks at step 10 and not only when contributions are merged: before the merge the
  item is at ordering value 0 in its own document, and no mask written against its final position
  could match it.
- The difference is intentional: a mask addresses the model the run actually builds, and after
  Section 16.10 rebasing that model is the only place the item's ordering value exists.
