# An input source outside every output selection warns

- namespace2xml 2.4.0: **fails**.
- Contract: Section 14.5; Section 22 `WARN014`; Section 26 item 91.
- Legacy observation: under this case's repeated `-i` arguments, 2.4.0 exits 1 before reading either
  source, reports that the input option is defined multiple times, and writes no output.
- Clean behavior: `base.properties` contains only the overlay's `host` value, and the run emits one
  `WARN014` naming `inputs/base.json` and its first eligible path, `port`.
- Why the divergence is specified: the source document's own root key is `port`, not `base.port`.
  The overlay makes the mistaken `base` selector non-empty, so the resulting file is plausible and
  the existing empty-selection warning cannot identify the omitted source.
