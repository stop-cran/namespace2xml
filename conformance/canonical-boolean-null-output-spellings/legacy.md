# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Sections 19.4, 19.5, 19.6 and Section 26 item 96.
- Legacy observation: all ten Appendix C.6 samples exited 134 before publishing any destination;
  an unhandled `ArgumentException` rejected the `attribute` XML type.
- Clean behavior: Boolean and null payloads use the same lowercase spelling in YAML, INI, XML
  element text, and XML attributes.
