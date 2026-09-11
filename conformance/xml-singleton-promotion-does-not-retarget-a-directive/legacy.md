# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Sections 11.4 and 15.2; Section 26 item 78.
- Legacy observation: all ten Appendix C.6 samples exited 134 with an unhandled `XmlException`
  while trying to use an ordering value beginning with `0` as an XML name; `r.xml` was empty.
- Clean behavior: promotion replaces singleton path `r.b` with stable item paths, so the former
  directive is not retargeted, all three elements render, and `WARN009` names the unbound
  directive.
- Intentional correction: Sections 3.1 and 3.2 preserve directive addressing while rejecting
  mutable array-index behavior and unhandled input exceptions; promotion cannot silently retarget
  a directive to a different canonical path.
