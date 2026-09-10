# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 3.3 and 19.5; Section 26 item 98.
- Legacy observation: the baseline silently omitted the explicit empty sequence.
- Clean behavior: XML still omits the unrepresentable repeated-sibling sequence, but reports `WARN015`.
