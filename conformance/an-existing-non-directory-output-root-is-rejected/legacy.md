# Why this fixture exists

Section 21.1 states plainly that "an existing non-directory output root is `PATH001`". The
condition is easy to reach by accident — a user who once ran with `--output config.ini` and later
adds a scheme that plans several files arrives here — and the diagnostic is the only thing that
tells them which of the two things they got wrong.

The distinction the fixture pins is between the two path diagnostics. `PATH002` says a destination
could not be written, which invites the user to check permissions on that one file. `PATH001` says
the root itself cannot hold destinations, which is a different repair. An implementation that
simply attempts the directory creation and reports whatever the filesystem raises produces the
former, because that is what the operating system reports; the specification asks for the latter,
which requires recognizing the condition before creating anything.

The pre-existing file is a fixture-owned input that also appears under `expected`, so the fixture
additionally asserts that a rejected root is left exactly as it was found. Section 21.1 places the
failure "before creating directories or opening destinations", and a run that truncated the file it
was complaining about would satisfy the diagnostic while destroying the user's data.

Version 2.4.0 had no output-root concept at all: `filename` values were resolved against the
process working directory, so this situation could not arise and no equivalent diagnostic existed.