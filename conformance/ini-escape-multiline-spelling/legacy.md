# Legacy differential

- namespace2xml 2.4.0: **agrees**. It exits 0 and publishes the exact expected escaped INI bytes.
- Contract: Section 19.6 and Section 26 item 96.
- Clean behavior: backslashes double before CR, LF, and TAB receive their named escapes.
