# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 19.6 — an INI destination that writes a preamble emits `WARN012` once per output
  instance, and a destination with no global key never had a preamble and never warns.
- Legacy observation: 2.4.0 has no diagnostic stream, so it cannot announce the preamble; and it
  writes a blank line before each section header, so `app.ini` diverges on bytes as well. `bare.ini`
  agrees, which is the half of the case that matters: the warning is per destination, not per run.
- The difference is intentional: the preamble is produced by ordinary input rather than by an option
  the author selected, so without the warning nothing in the run would report that the file may be
  unreadable where it is meant to be used.