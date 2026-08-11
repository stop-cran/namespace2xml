# Legacy differential

- namespace2xml 2.4.0: **differs**, and silently, for the same reason as
  `an-xml-scheme-file-is-not-a-scheme-format`: it exits 0, writes nothing, and reports success.
  It therefore emits no diagnostic at all, so the separator question this case asks could not
  arise there. The differential is inherited rather than new, and is recorded in that case.
- Contract: Section 6.4.3, which says `source` and `destination` are relative paths using `/`
  separators when the location is inside the invocation's input set or output root. A scheme
  file named by `-s` is inside the input set, so its spelling in the diagnostic stream is fixed
  by that rule and not by the separator the invoking shell happened to use.
- Clean behavior: the scheme path is supplied as `schemes\scheme.xml`, with the separator a
  Windows shell produces. The emitted `source` is `schemes/scheme.xml`. Pairing this case with
  `an-xml-scheme-file-is-not-a-scheme-format`, which supplies `schemes/scheme.xml` for the same
  rejection, asserts that one invocation does not yield two different diagnostic byte streams
  according to how the caller spelled a separator.
- Why this is portable. Section 15 rejects a scheme file on its extension alone, before the file
  is opened, so the diagnostic does not depend on the path resolving. On Windows the argument
  names the `schemes/scheme.xml` in this directory; on a platform where `\` is an ordinary
  filename character it names nothing. Both produce this exact stream, which is the point: the
  spelling rule is a property of the encoder, not of the file system.
- The case guards a regression rather than an original defect. The Section 15 rejection reports
  before the read and so does not pass through the loader that normalizes every other source,
  which reintroduced the raw spelling on this one path when that rejection was added. A rule
  applied by a shared helper is only as good as the number of sites that call it.
