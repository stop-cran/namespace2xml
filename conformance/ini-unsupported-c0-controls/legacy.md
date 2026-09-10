# Legacy differential

- namespace2xml 2.4.0: **differs**. It had no INI output or C0-control contract.
- Contract: Section 19.6 and Section 26 item 96.
- Clean behavior: unsupported non-NUL C0 controls are `INI001` under each multiline and quoting
  mode.
