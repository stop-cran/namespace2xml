# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 8.5, 19.2, and 20; Section 26 item 13.
- Legacy observation: all ten Appendix C.6 samples exited 0 but published `p.properties` and
  `q.properties` instead of the configured quoted-namespace files `p.sh` and `q.sh`.
- Clean behavior: each quoted-namespace output instance receives the complete ownerless leading
  and trailing comment runs around its selected shell assignment.
- Intentional correction: the Section 3.3 supported-feature guarantee preserves ownerless comments
  independently for every concrete output; dropping them or the configured output format would
  lose source information.
