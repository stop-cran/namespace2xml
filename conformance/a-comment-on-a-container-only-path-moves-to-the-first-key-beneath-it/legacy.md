# Legacy differential

- namespace2xml 2.4.0: **differs**. It drops all three comments from all three outputs, writes CRLF
  line endings, and exits 0 with nothing said about the loss. It also diverges here in three ways
  this case is not about: `cfg.ini` separates sections with blank lines, `cfg.yaml` loses the `cfg`
  root, and `cfg.yaml` reorders the top-level keys alphabetically to `a`, `d`, `x` even though
  `cfg.properties` from the same run keeps the source order `x`, `a`, `d`.
- Contract: Section 19.1, which converts a comment whose association a flat format cannot represent
  "to the nearest position the format does represent rather than discarded"; Section 20, which
  places such a comment in INI; Section 19.6, which gives the section/key projection and the rule
  that container-only paths emit no key.
- Legacy observation: a comment bound to a container-only path had nowhere to go in the flat
  formats and was dropped without a diagnostic. The clean implementation initially did the same:
  its flat projection attached comments only to nodes that carried a payload, so a comment on
  `a` or `a.b` — paths that spell no key — was discarded silently, and even the summarized
  `WARN003` never fired because nothing counted the loss.
- Clean behavior: the comment rides down to the first key the projection emits beneath its path.
  `# about a` and `# about a.b` both surface above `cfg.a.b.c=1`, in the order their paths were
  visited, and `# about d` above `cfg.d.e=2`. Nothing is reported, because Section 19.1 makes this
  a normalization rather than a discard.
- The case is deliberately three-deep. A comment on `a` must cross two container-only levels to
  reach a key, and a second comment on `a.b` must land after it rather than before, so the fixture
  fails if the carried comments are emitted in reverse or if only the nearest ancestor is carried.
  `d` is present so that the carry is proven to reset: a comment held for one subtree must not leak
  onto the next entry after that subtree has taken it.
- INI is included because it is the one flat format that does emit a line for some container-only
  paths — the section header. Section 20 does not hoist the comment above that header, so the
  expected bytes place `# about a` after `[cfg:a:b]`. Pinning it here means a later decision to
  hoist has to change a stated expectation rather than drift into the output. Note also that
  `[cfg:a]` gets no header at all: it has no direct keys, and the comment bound to it still
  survives.