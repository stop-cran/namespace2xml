# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 8.7 and 19.3; Section 26 item 68.
- Legacy observation: all ten Appendix C.6 samples exited 0 and emitted the same JSON array bytes
  except for omitting the required final LF: 16 bytes instead of 17.
- Clean behavior: the inferred sequence is emitted with the Section 24 final LF and one
  output-instance-scoped `WARN010` names the contributing YAML mapping.
- Intentional correction: Section 24's platform-independent byte contract requires the final LF,
  while `WARN010` makes the deliberate Section 8.7 structural normalization visible rather than
  silently changing the source mapping's projected shape.
