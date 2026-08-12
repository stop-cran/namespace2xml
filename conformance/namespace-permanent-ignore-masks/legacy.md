# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 8.6; Section 8.5 for the comment run; Section 20 for INI comment emission.
- Legacy observation: `!` removal was applied in source order like any other entry, so an entry
  written after the ignore reinstated the path, and an ignore in a later file did not reach a value
  contributed by an earlier one. Whether a comment bound to a removed entry survived was not
  stated. Measured, `app.ini` keeps a whole `[legacy]` section — `host=old` and `port=1` — that
  `!app.legacy` names, so the mask does not reach a container path at all, and it writes no comment
  of any kind despite `HashComments` being selected.
- Clean behavior: a mask is a permanent run-wide subtree exclusion. It suppresses a matching
  contribution "regardless of whether it appears before or after the ignore entry", a later
  contribution "cannot recreate the path", the exclusion reaches descendants, and comments bound to
  suppressed paths are suppressed with them.
- The difference is intentional: Section 8.6 names this "an explicit exception to universal
  later-source precedence". A mask exists to guarantee that a value cannot leave the tool, and a
  guarantee an ordinary later entry can revoke is not one.
- The case carries two comments one line apart to separate the two rules that were previously
  conflated here. The comment before `app.legacy.host` owns a masked entry and dies with it. The
  comment at the very top of the file is document-leading under Section 4.5 — it precedes the first
  entry of the source — so it has no value owner and survives the mask over the entry it sits
  against. Until issue #87 the INI writer dropped every ownerless comment, and this case passed
  without ever exercising either rule.
