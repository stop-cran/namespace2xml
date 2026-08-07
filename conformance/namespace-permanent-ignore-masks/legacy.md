# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 8.6; Section 8.5 for the comment run.
- Legacy observation: `!` removal was applied in source order like any other entry, so an entry
  written after the ignore reinstated the path, and an ignore in a later file did not reach a value
  contributed by an earlier one. Whether a comment bound to a removed entry survived was not
  stated.
- Clean behavior: a mask is a permanent run-wide subtree exclusion. It suppresses a matching
  contribution "regardless of whether it appears before or after the ignore entry", a later
  contribution "cannot recreate the path", the exclusion reaches descendants, and comments bound to
  suppressed paths are suppressed with them.
- The difference is intentional: Section 8.6 names this "an explicit exception to universal
  later-source precedence". A mask exists to guarantee that a value cannot leave the tool, and a
  guarantee an ordinary later entry can revoke is not one.
