# Legacy differential

- namespace2xml 2.4.0: **differs**. It picks the *same* shape at every node — `emptywins.x` renders
  as `{}` and `scalarwins.x` as `1` in both formats — and then diverges twice: `cfg.json` ends at
  `}` with no final newline, and neither loss is reported. `cfg.yaml` matches. Exit 0. **verified**
  — measured against the Appendix C.6 pinned 2.4.0 package.
- Contract: Section 4.4's exclusive-shape contest for an empty container; Section 19.3; Section
  19.4; Section 24's trailing newline.
- Legacy observation: 2.4.0 emitted JSON and YAML, and its last-contribution-wins behaviour happened
  to agree with Section 4.4 on this input. What it did not do is tell anyone. A node that carried
  both a scalar and an empty mapping silently kept one and dropped the other, and an empty mapping
  is exactly the shape where that is hardest to notice: the winner has no content, so the file gives
  a reader nothing to compare against what they wrote. It also ended `cfg.json` without a final
  newline, and wrote `Environment.NewLine` rather than LF, so the same input produced different
  bytes on Windows and on Linux.
- Clean behavior: Section 4.4 ranks the latest scalar/null contribution against the latest container
  contribution and renders the later one, and "empty mappings therefore participate in precedence
  even though they have no children". `emptywins.x` receives the scalar first and the empty mapping
  second, so the empty mapping wins; `scalarwins.x` receives them the other way round, so the scalar
  wins. Step 4 requires the losing shape to produce "one shape-conflict warning", counted per path
  and output instance, so each of the two paths warns once for `cfg.json` and once for `cfg.yaml` —
  four `TYPE002` in total. Section 24 ends a text output with exactly one LF on every platform.
- The difference is intentional: an agreement that is never stated is not a guarantee. 2.4.0 chose a
  shape by an emergent property of its overlay rather than by a stated rule, so a user had no way to
  know the choice was made at all, let alone to predict it. The warning is the correction here, not
  the shape.
