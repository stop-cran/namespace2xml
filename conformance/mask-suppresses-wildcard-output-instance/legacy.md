# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 8.6 and 14.4; Section 26 item 34.
- Legacy observation: all ten Appendix C.6 samples exited 0 but published `x.properties` and
  `y.properties`, including an instance for the masked path, instead of only `a.y.properties`.
- Clean behavior: permanent masks apply before concrete wildcard-output expansion, so `a.x`
  creates no output instance and only `a.y.properties` is published.
- Intentional correction: Section 3.1 preserves ignores and wildcard templates; allowing a masked
  path to instantiate an output would make the permanent ignore ineffective.
