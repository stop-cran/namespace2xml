# Legacy differential

- namespace2xml 2.4.0: **agrees** on the tree and the exit code, which are what Appendix C.6
  observes: both builds write no output file and exit 1.
- Contract: Section 7.2, "an empty token supplied to `-i`, `-s`, `-v`, or `-o` is a blocking
  `CLI001` at Section 6.2 option-value validation"; Section 26 items 1 and 86.
- The agreement is the finding, not an absence of one. Everything that changed here is in the
  report, and the report is the thing this case fixes. 2.4.0 carried the empty token to
  `Path.GetFullPath` and printed the resulting `System.ArgumentException` — message, type name and
  a stack trace naming `/home/runner/work/namespace2xml/.../FileStreamFactory.cs:line 25`, a source
  file of the tool on a build agent. The clean build emits `CLI001` with `spec` `§6.2`, naming the
  option that received the empty value.
- Before this change the clean build did the same thing 2.4.0 does, one layer higher: an unhandled
  `ArgumentException` and exit `-532462766`. The tree-and-exit lane could not see that either,
  which is why the case is carried by `expected-diagnostics.json` rather than by this file.
- The overwhelmingly common way an empty token arrives is a shell expanding an unset variable
  inside quotes. By the time the tool is running, the option is the only part of the invocation
  still visible, so it is the only thing a useful report can name.
