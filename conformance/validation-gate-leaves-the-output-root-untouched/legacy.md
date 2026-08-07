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
