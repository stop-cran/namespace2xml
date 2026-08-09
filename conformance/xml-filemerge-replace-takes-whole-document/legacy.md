# Legacy differential

- namespace2xml 2.4.0: **differs**. It writes `out.xml` as
  `<?xml version="1.0" encoding="utf-8"?>` on one line then `<doc r="3" s="4" />` on the
  next, logs `Overriding output ... out.xml xml` on standard output, and exits 0. The case
  expects `<doc><r>3</r><s>4</s></doc>` — the later contribution's whole document — with
  one `WARN005` diagnostic reporting the destination collision. What the baseline emits
  is neither the earlier plan nor the later plan: the two contributions have been
  attribute-flattened onto one root element and the earlier document's `p` and `q` are
  gone.
- Contract: Section 16.11 `filemerge=replace` and Section 17.5 file-level collisions;
  Section 3.2 correction against behaviour "caused by relying on `merge` to control
  collisions between output instances; such schemes must use `filemerge`, while `merge`
  remains recognized with input/common-model scope".
- Legacy observation: 2.4.0 had no `filemerge` directive. The `b.filemerge=replace` line
  was unrecognized and inert, so the two same-format contributions folded under the
  baseline's default XML strategy — which flattens elements into attributes of one root,
  the same defect visible in `xml-comments-are-invisible-to-alias-resolution` and
  `contradictory-output-option-flags-are-scheme001`. The `Overriding output` log line is
  the baseline's operational message for a destination collision; it names no code, no
  phase, and no anchor and is written to standard output rather than a structured
  diagnostic stream.
- Clean behavior: §16.11 states that under `filemerge=replace` for same-format contributions
  "the later same-format output contribution replaces the complete earlier visible document
  model while retaining destination high-water state as specified in Section 17.5". §17.5
  then says that "when the effective destination `filemerge` strategy is `replace`, the
  later element's complete value — attributes, content tokens, comments, and children —
  replaces the earlier element. Singleton/sequence classification and recursive child
  merging are not applied to the replaced earlier element." The later contribution's
  `<doc>` with `<r>` and `<s>` is therefore what `out.xml` contains; §22 emits one
  `WARN005` for the folded contribution pair; the run succeeds with exit 0.
- The difference is intentional: `filemerge` is the whole point of §3.2's correction
  against "relying on `merge` to control collisions between output instances". Under
  2.4.0's model, an author who wanted a later document to *replace* an earlier one at the
  same destination had no directive to say so; the baseline chose a strategy for them,
  and it chose a strategy that in this case corrupted both documents. The 3.0 rule gives
  the author an explicit vocabulary — `deep`, `replace`, `append`, or `error` — and the
  `WARN005` diagnostic tells them that a collision occurred and which way it was resolved.
