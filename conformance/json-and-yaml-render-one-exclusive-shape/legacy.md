# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 4.4; Section 19.3; Section 19.4.
- Legacy observation: 2.4.0 emitted both JSON and YAML, and its last-contribution-wins behaviour
  happened to agree with Section 4.4 on this input: `mapwins.x` came out as an object containing
  `z` and `scalarwins.x` as `1`, in both files. Two things were missing rather than wrong. Neither
  omission was reported, so a user could not tell that a shape had been dropped; and `cfg.json`
  ended at `}` with no final newline, while `cfg.yaml` had one. 2.4.0 also wrote
  `Environment.NewLine`, so the same input produced different bytes on Windows and on Linux.
- Clean behavior: Section 4.4 makes JSON and YAML exclusive-shape destinations. Its own example
  says a namespace emitting both `a.x=1` and `a.x.z=3` renders `x` as an object containing
  `z`, omits the scalar, and warns; reversing source order makes the later scalar win. Each loss
  is one `TYPE002`, counted once per path and output instance, so the same path warns separately
  for the JSON and the YAML destination.
- The difference is intentional: a structured document node holds one shape, and Section 24
  requires the choice to be a function of contribution order rather than of a format preference.
