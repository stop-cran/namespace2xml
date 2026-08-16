# Legacy differential

- namespace2xml 2.4.0: **agrees**, and for the same reason it agrees on most `substitute` cases --
  it has no rule here to disagree with. The baseline never applied substitution modes to masks
  because it never considered the question.
- Contract: Section 16.7; Section 8.6; Section 15.1 step 6.
- Clean behavior: `substitute=None` is in force at every node, and the mask `!cfg.drop.*` still
  expands, removing both `cfg.drop.x` and `cfg.drop.y`. Section 16.7 says an exclusion mask "is not
  an entry and is not governed by this directive", and Section 15.1 step 6 matches a `substitute`
  pattern against "an entry's declared pre-expansion path" -- a mask has a path and no value, so
  there is no entry for the pattern to match.
- The alternative reading is not absurd, which is why the case exists: a mask is written in a
  profile, it contains wildcard syntax, and `None` says names are not interpreted. Under that
  reading `cfg.drop.*` is a literal name matching nothing, both keys survive, and the output has
  three lines instead of one. The discriminator is deliberate -- masking a wildcard rather than a
  literal path is the only shape in which the two readings differ.
- The consequence of the other reading is worth stating plainly: `substitute=None` would silently
  disable every wildcard mask in the profile it governs, so a directive about *value* interpretation
  would change which paths exist. That is the kind of coupling a reader has no reason to expect.