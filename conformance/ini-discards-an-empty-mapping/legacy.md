# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 3.3 and 19.6; Section 26 item 98.
- Legacy observation: the baseline silently omitted the explicit empty mapping.
- Clean behavior: INI still omits the unrepresentable mapping, but reports `WARN015`.
