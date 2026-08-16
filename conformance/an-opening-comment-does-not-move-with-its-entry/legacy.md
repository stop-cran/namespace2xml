# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `app.yaml` containing `name: second` followed by
  `tail: end`, and exits 0. Both comments are gone and the overridden key has not moved. The case
  expects `# describes first.txt`, then `tail: end`, then `# renamed`, then `name: second`.
- Contract: Section 8.5; Section 5.2; Section 20.
- Legacy observation: 2.4.0 discarded namespace-profile comments outright on the namespace-input
  path, so no comment classification of any kind was observable, and it kept a mapping key at the
  position of its first contribution rather than its winning one.
- Clean behavior: this case pins the cost Section 8.5 names when it excepts a source's opening run
  — "an opening comment is bound to no path, so Section 5.2 does not move it when the first entry
  is overridden and Section 16.5 does not carry it into a generated record". `# describes
  first.txt` precedes the first entry of `first.txt`, so it is document-leading and Section 20
  places it before "that source's first surviving contribution", at the head of the document.
  `# renamed` follows an entry, so it binds to `app.name` and travels with it under Section 5.2 to
  `second.txt`'s position mark.
- The two comments differ only in whether an entry precedes them in their own file, and they end up
  in different places, which is exactly the asymmetry the exception creates. A reader who expects
  the first comment to behave like the second should read Section 8.5's closing sentence: "A source
  whose first entry needs a comment of its own must be written with that entry second."
- The case fails an implementation that binds the opening run to the first entry, which would carry
  `# describes first.txt` down to `name: second`, and it fails one that treats every comment as
  document-leading, which would hoist `# renamed` to the top.
- The difference is intentional: a comment at the head of a file usually describes the file, and a
  tool that lets an override drag the file's header down to wherever the first setting ended up has
  destroyed the only cue the reader had about what the file is for.
