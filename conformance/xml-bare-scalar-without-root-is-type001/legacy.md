# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Sections 14.1 and 19.5; Section 26 item 56.
- Legacy observation: all ten Appendix C.6 samples wrote an unexpected `lone.xml` and exited 134
  with an unhandled `InvalidCastException` from `XAttribute` to `XElement`.
- Clean behavior: XML cannot invent an element identity for the selected bare scalar, so one
  blocking `TYPE001` is reported before publication and the run exits 1.
- Intentional correction: Section 3.2 forbids unhandled user-input exceptions and publication
  before validation; the explicit type error is the deterministic contract for an unrepresentable
  XML view.
