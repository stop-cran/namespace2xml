# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `cfg.properties` containing only `b=2`, and exits 0.
  The header comment is gone. The case expects `# describes this file` above `b=2`.
- Contract: Section 8.5; Section 8.6; Section 20.
- Legacy observation: 2.4.0 discarded namespace-profile comments outright on the namespace-input
  path, so the question this case asks could not arise: there was no comment left to be suppressed
  along with the entry it preceded.
- Clean behavior: Section 8.6 states that "comments bound to suppressed paths are suppressed with
  them", so if `# describes this file` were bound to `cfg.a` the ignore mask would take it. It is
  not bound, because Section 8.5 excepts the opening run: "comments preceding the first entry of a
  source are document-leading, as Section 20 classifies the first position for every format". A
  document-leading comment has, in Section 4.5's words, "no value owner", so no mask can reach it,
  and Section 20 emits it before the source's "first surviving contribution" — here `cfg.b`.
- The mask is what makes the two readings distinguishable. Without it both readings emit the
  comment above `b=2`; with it, binding to the first entry deletes the comment and the exception
  keeps it. The destination is the namespace format itself, so the case also asserts that a profile
  round-tripped through this tool keeps its header rather than losing it to an unrelated mask.
- The difference is intentional: an ignore mask is written to remove a setting, and a person adding
  one to a file does not expect the sentence explaining what the file is to disappear with it.
