# Legacy differential

- namespace2xml 2.4.0: **differs**. Quoted-namespace output and comment placement were not part of
  its contract.
- Contract: Sections 19.1, 19.2, 20, and Section 26 item 97.
- Clean behavior: adjacent entries and comments occupy adjacent physical records, with one final
  LF and no inserted blank record.
