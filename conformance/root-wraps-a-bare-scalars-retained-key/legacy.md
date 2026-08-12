# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits 0 and disagrees on every one of the six
  outputs, in four distinct ways.
  - `lone.ini` is `[x]` then `y=demo`. The last `root` part is spent as the key text and
    the retained selector name is destroyed, which is the behaviour Section 19.6 rules out
    when it makes `root` parts section-path parts rather than part of the key text.
  - `lone.properties` is `x.y="demo"`. Same replacement, and the value is double-quoted
    rather than bare.
  - `lone.json` is `"demo"` and `lone.yaml` is `demo`. The `x.y` wrap is missing entirely,
    so the two structured formats lose the root that the flat ones at least misapply.
  - No `lone.sh` is written at all. The log says `Overriding output ... lone.properties
    quotednamespace`, so the shell projection was written over the namespace projection at
    the same filename and one of the two requested outputs simply does not exist.
  - The nested selector is written to `leaf.ini`, not `deep.inner.leaf.ini`, and its
    content is `r=8080` — the destination name is derived from the last selector part
    rather than the selector, which collides with any other `leaf` elsewhere in the tree.
- Contract: Section 16.3 `root` wraps and never renames; Sections 19.1, 19.2 and 19.6
  retain the final concrete selector part as the key for a bare scalar, and `root`
  prefixes that key. Section 15 for one file per output instance.
- This case is the bare-scalar counterpart of `root-wraps-uniformly-across-formats`, which
  pins the same rule for a container selection. Read together they say that `root` does not
  acquire a second meaning when the selected view happens to be a scalar.
- The 3.0 outputs are authored from those clauses rather than measured. The baseline's
  `[x]`/`y=demo` was the reading this build shared until issue #54 established that
  Section 19.6 says otherwise.
