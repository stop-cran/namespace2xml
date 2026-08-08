# Why this fixture exists

Section 21.2 requires the tool to complete every pipeline step, serialize every planned output into
an in-memory buffer, and validate every final path **before opening or truncating any destination**.
That gate is invisible on a successful run: a correct implementation and one that writes each
destination as soon as it is ready produce byte-identical trees. It becomes observable only when one
destination fails after another has already been serialized.

Two output instances are declared. `ok` projects cleanly. `bad` contains two distinct logical
paths, `a.b.k` and `a:b.k`, that Section 19.6 projects to the same INI section and key, because a
nested path joins its section parts with `:` and a single ordinary component may itself contain a
`:`. Section 16.4 forbids two paths silently becoming one flat key, so this is blocking `FLAT001`.

The collision is detected inside pipeline step 19, while the buffer for `ok.ini` already exists.
An implementation that has lost the gate writes `ok.ini` and then fails; a conforming one writes
nothing. The fixture therefore ships **no** `expected` directory, which Appendix C reads as "no
destination may be created", and the assertion is the absence of `ok.ini` rather than the presence
of anything.

Version 2.4.0 had no equivalent gate: it opened each destination as it produced it, so a run that
failed partway left a mixture of new and stale files in the output root.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 3.2 correction against output files being opened before the complete output plan was validated; Section 21.2 global validation gate; Section 19.6 INI section/key projection and `FLAT001`; Section 26 items 30 and 66.
- Legacy observation: the baseline exits `0` and writes both `ok.ini` and `bad.ini`. The measurement records `exit 0 (expected 1); extra bad.ini; extra ok.ini`. Standard error is empty beyond the banner, so no `FLAT001` is reported and both destinations land as if the collision at `bad.ini` were not a defect.
- Clean behavior: the collision at `bad.ini` -- `bad.a.b.k` and `bad.a:b.k` project to the same INI section and key -- is detected at pipeline step 19, before any destination is opened. The run reports `FLAT001` and exits `1`. Neither `ok.ini` nor `bad.ini` is written.
- Why the difference is intentional: Section 3.2 names this correction directly, "caused by output files being opened before the complete output plan was validated". The specific `ok.ini` byte content the baseline lands is what a run that opens each destination as its serializer completes will write next to a run that then fails on another destination; the specification's rule instead makes the whole plan pass validation together, so a defect in one file scrubs the whole publication. The `bad.ini` bytes are the second half of the same defect -- the collision is not detected at all, and something arbitrarily addressed under the two flat keys is what the file carries. Either byte set is the observable Section 3.2 says the specification does not admit.
