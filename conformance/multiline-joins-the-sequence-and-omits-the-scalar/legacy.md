# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 16.6; Section 4.4; Section 24.
- Legacy observation: 2.4.0 emitted `cfg.json` containing `"m": "direct"`. The `type=multiline`
  directive had no visible effect: the sequence items `one` and `two` were discarded, the scalar
  was rendered alone, and nothing was reported. A user who wrote `type=multiline` in order to join
  a sequence therefore received the one value the directive was meant to replace, silently. The
  file also carried CRLF line endings, so the same input produced different bytes per platform.
- Clean behavior: Section 16.6 gives the sequence precedence here and requires the loss to be
  reported.

  > Where the node supplies both a sequence projection and a scalar payload, the sequence is the operand.

  The omitted scalar is one `TYPE002` for that path and output instance, carrying both `path` and
  `destination`. Section 15.1 resolves destinations at step 17, after the transformation pass, but
  the stream is emitted once at the end of the run, so the destination is known by the time the
  record is written.
- The difference is intentional: without the directive Section 4.4 resolves the same pair the
  other way and warns. A directive whose whole purpose is to consume a sequence must not lose to
  a scalar, and reversing the Section 4.4 outcome is exactly why the discarded scalar has to be
  named rather than dropped quietly.
