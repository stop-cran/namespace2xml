# Legacy differential

- namespace2xml 2.4.0: **differs**. It reads the case as posed and exits `0`, writing all
  twenty-one entries, but it orders keys alphabetically rather than in document order, writes
  `010` as `10`, and spells every portably typed value and key plain.
- Contract: Section 19.4's portably typed spellings.
- Clean behavior: a scalar that either published YAML schema types is single-quoted, in key
  position and in value position alike; a scalar neither schema types stays plain.
- Why this case exists: a writer that consults only its own reader under-quotes by exactly the
  difference between that reader and the schema a consumer uses. The cost is not a diagnostic but
  a silent one, at exit `0`, and it was measured on the 2.4.0 output rather than argued: a YAML
  1.1 reader abandons that document entirely on the `<<` value, and with the two key tags
  neutralised nineteen of the twenty-one entries survive, because `yes`, `on` and `y` all resolve
  to one key and `no`, `off` and `n` to another.
- Why the keys are half the case: a key carries identity rather than data, so two keys distinct as
  written that resolve to one key lose a member outright. Section 19.3 makes a mapping-key
  collision blocking `FLAT001`; a collision that appears only after the reader resolves the scalar
  would defeat that rule from outside the tool.
- Why `y` and `n` are present although several widely consulted readers leave them strings: the
  YAML 1.1 type repository lists them and at least one broadly deployed reader implements the list
  as published. They are the entries a survey of libraries rather than of schemas would drop.
- Why `hex`, `octal` and `infinity` are separate from `sexagesimal`, `date`, `underscored` and
  `leadingzero`: the first group is typed by YAML 1.2 and the second by YAML 1.1. A rule that
  picked either revision passes half of them and fails the other half, which is what makes the
  union rather than a choice the thing under test.
- Why `plain`, `colonmid`, `dotfirst`, `minuteoverflow`, `bareprefix` and `lonedot` are present:
  they are the negative half. Every numeric production requires at least one digit and a
  sexagesimal group a value below sixty, so an implementation that quotes defensively rather than
  by the productions round-trips perfectly and still fails here. The case has to fail on
  over-quoting as readily as on under-quoting, or it would be satisfied by quoting everything.
