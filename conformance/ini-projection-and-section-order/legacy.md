# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 19.6; resolved legacy issues 203 and 205.
- Legacy observation: INI projection and the placement of global keys relative to sections were
  not stated, so neither the preamble nor section order was a contract.
- Clean behavior: a one-part path is a global key and all global keys are hoisted into one
  preamble; a longer path splits into a delimiter-joined section and a final key; a section takes
  the position of its first key in the Section 19.1 emission order, so `[p:x]` precedes `[p]` when
  the container child was declared first.
- The difference is intentional: hoisting is a format projection that does not change precedence,
  and ordering sections by the emission stream keeps every INI rule a function of that stream
  alone.
