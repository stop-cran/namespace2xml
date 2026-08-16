# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 17.5; Section 15.2.
- Legacy observation: 2.4.0 logged `Writing output ... m.json` and then
  `Overriding output ... m.json`, and left `{"b": 2}`. The `p` contribution was not merged into
  the destination at all but overwritten wholesale, so both its value `a` and its `type=string`
  vanished. `filemerge=deep` was declared on both contributions and had no effect. The file also
  carried CRLF line endings.
- Clean behavior: the two contributions deep-merge, and Section 17.5 carries each one's
  Section 15.2 transform table into the fold with it.

  > Where several contributions fold to one destination, that destination's table is the union of theirs, so a `type` bound in a contribution that is not the last one still applies

  So `a` renders as the string `"1"` under the `type` declared on `p`, while `b`, which no
  `type` addresses, keeps its inferred number.
- The difference is intentional: Section 15.2 evaluates `type` in every output instance
  containing the path, and an instance does not stop containing the path by being folded into a
  file with another instance. Taking only the last contribution's directives would make a
  declaration's meaning depend on how many other declarations happen to share its filename.
