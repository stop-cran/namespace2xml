# Legacy differential

- namespace2xml 2.4.0: **agrees**, for a reason the differential lane cannot see. It exits 0 and
  writes `cfg.ini` with the single key and no comment, which is the same file 3.0 writes. The
  baseline has no diagnostic stream at all, so it cannot announce the discard the case is about;
  the lane compares the output tree and the exit code, and on both it matches.
- Contract: Section 20 — comments are discarded when `inioutputoptions` selects neither
  `SemicolonComments` nor `HashComments`, and `WARN003` announces the discard.
- The comments here are deliberately ownerless: one precedes the first entry of the source and one
  follows the last, so neither is attached to a key. Before this change 3.0 raised `WARN003` only
  when a surviving entry still owned a comment, and a file whose every comment was ownerless lost
  them in silence. This case is the one that fails against that narrower condition.
- Silence is the whole subject, so the case pins the diagnostic stream rather than the tree. Its
  companion `an-ini-document-trailing-comment-is-emitted-at-end-of-file` pins placement when the
  option *is* selected.
