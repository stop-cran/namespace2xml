# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 17.5's rule that file-level merge "can therefore create a shape conflict that
  exists in no contribution", reported "once for the destination and the projected path, carrying
  `destination` and `path` and naming no output instance"; Section 4.4's shape contest; Section 22's
  `TYPE002` cardinality of "once per projected path and destination"; Section 24's group 2 ordering
  and its code tie-break, which puts `TYPE002` before `WARN005` at one destination.
- Legacy observation: 2.4.0 logged `Writing output ... out.json` and then
  `Overriding output ... out.json`, wrote `{"x": {"y": 2}}` and exited 0, reporting neither the
  destination collision nor the lost scalar.
- Clean behavior: the two instances fold, the mapping from `b` is the later container contribution
  and renders, the scalar from `a` is omitted, and the run reports one `TYPE002` for the
  destination and one `WARN005` for the collision, exiting 0.
- Why the difference is intentional: the file bytes happen to coincide, which is the point of the
  fixture. The baseline reaches them by discarding the earlier plan wholesale, so it would produce
  the same file whatever `a` contributed, and it says nothing when a contribution is dropped. Here
  the conflict belongs to neither instance -- `a` supplies only a scalar at `x` and `b` only a
  mapping, and each is internally consistent -- so an implementation that warns per output instance
  has no instance to blame and stays silent, exactly as the baseline does. Keying the warning to
  the destination is what makes the loss reportable at all.
