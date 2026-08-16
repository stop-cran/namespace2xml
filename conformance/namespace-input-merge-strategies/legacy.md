# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 16.10; Section 15.1 steps 8 and 11; Section 5.4.
- Legacy observation: input merging had one behavior. Later contributions merged into earlier ones
  recursively and there was no way to say "this path is replaced wholesale", "these items
  concatenate rather than patch", or "a second contribution here is a mistake". A profile that
  wanted any of those three had to be restructured until deep merge happened to produce them.
- Clean behavior: `merge` selects among four strategies at the node it matches, and descendants use
  their own effective strategy. This case puts all four at sibling roots over identical two-source
  input, so each strategy is read against the others rather than in isolation:
  - `deep` folds recursively, so `deep.y` is overridden and `deep.z` is added;
  - `replace` substitutes the later complete value, so `repl.x` is gone even though the later
    source never mentioned it;
  - `deep` over an all-canonical-numeric mapping patches at the supplied ordering value, so `pat.0`
    becomes `gamma` and `pat.1` survives;
  - `append` rebases the later contribution above the high-water mark instead of patching, so the
    same input that patched under `pat` concatenates under `app`;
  - `error` counts source contributions, not entries, so two entries written at `solo` in one file
    fold without complaint. The rejecting half of `error` is the separate
    `namespace-merge-error-second-source` case, because it publishes nothing.
- The difference is intentional: Section 3 makes the merge strategy an explicit part of the
  contract, and the `pat`/`app` contrast is the reason. The same two sources produce a patched
  two-item sequence or a concatenated three-item one purely by declaration.
