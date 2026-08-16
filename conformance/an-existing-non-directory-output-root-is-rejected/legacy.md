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

## Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 3.2 correction against unhandled user-input exceptions; Section 21.1 output-root
  rejection; Section 26 item 29.
- Legacy observation: the baseline terminates with an unhandled `System.IO.IOException` reading
  `Cannot create '…\out' because a file or directory with the same name already exists.` and exits
  `-532462766`. The measurement records `exit -532462766 (expected 1)`. The pre-existing `out`
  file is preserved because the exception is raised before anything is written.
- Clean behavior: the plan is rejected before any directory creation with one `PATH001`
  diagnostic anchored at `§21.1`, exit `1`, and the output root untouched.
- Why the difference is intentional: 2.4.0 forwarded the CLI's output path directly to
  `Directory.CreateDirectory`, which raises `IOException` when the path already exists as a file
  and the process has no handler for that exception. Section 3.2 lists "caused by unhandled
  user-input exceptions" among the behaviours the replacement must not preserve, so the correction
  is not merely to change the exit code but to detect the condition and report it as a stable
  diagnostic. The crash exit code carries no code, no phase, and no spec anchor; an automated
  caller cannot tell it apart from a runtime crash.