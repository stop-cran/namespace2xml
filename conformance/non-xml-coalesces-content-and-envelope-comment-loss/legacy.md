# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 4.5, 20, and 26 item 99.
- Legacy observation: outer XML comments were discarded silently before projection.
- Clean behavior: the non-XML destination counts two envelope comments and one internal content comment in one destination-scoped `xml-comments` `WARN003`.
