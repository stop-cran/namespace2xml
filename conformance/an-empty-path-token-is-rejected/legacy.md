# Legacy differential

- namespace2xml 2.4.0: **differs**. It accepted the empty token as a path, carried it to
  `Path.GetFullPath`, and printed the resulting `System.ArgumentException` — message, type name and
  a stack trace naming `/home/runner/work/namespace2xml/.../FileStreamFactory.cs:line 25` — before
  exiting nonzero.
- Contract: Section 7.2, "an empty token supplied to `-i`, `-s`, `-v`, or `-o` is a blocking
  `CLI001` at Section 6.2 option-value validation"; Section 26 item 1.
- Legacy observation: exit status 1, with the failure rendered as a .NET exception dump on the log
  stream. No stable code, no machine-readable stream, and the file named in the report is a source
  file of the tool on a build agent rather than anything the caller wrote.
- Clean behavior: `CLI001` with `spec` `§6.2` and exit 1, naming the option that received the
  empty value. The overwhelmingly common way an empty token arrives is a shell expanding an unset
  variable inside quotes, so the report has to point at the option, which is the only part of the
  invocation still visible by the time the tool sees it.
