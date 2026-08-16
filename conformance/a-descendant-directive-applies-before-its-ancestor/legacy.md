# Legacy differential

- namespace2xml 2.4.0: **differs**. It produces the same `{"a": [[1, 2], {"m": 3}]}` shape and the
  same interior bytes, and then omits the final newline: 81 bytes ending `\r\n}`, which is 71 bytes
  ending `\n}` once the Section 24 CRLF divergence is normalized away, against 72 ending `\n}\n`.
- Contract: Section 15.1, "within one pass a directive at a deeper path applies before a directive
  at a shallower path"; Section 21 for the terminating newline; Section 26 item 54.
- Legacy observation: the baseline agrees about the *ordering*, which is the rule this case exists
  for. It converts `cfg.a.x` to an array first and then converts `cfg.a`, so the already-converted
  `x` enters its parent as a sequence element and the unconverted `y` enters as a mapping. The
  divergence is confined to the last byte of the file.
- That distinction is the reason the case is here. This ordering was never written down in either
  project, and agreement is not evidence: nothing in the output says which order produced it, and
  outermost-first would consume `cfg.a`'s children before `cfg.a.x.type` was ever consulted, so the
  descendant directive would vanish without a word. The corpus is now what holds the answer.
- The missing terminator is worth recording separately. A file that does not end in a newline is
  not a POSIX text file, and appending to it or concatenating it silently joins two records.
