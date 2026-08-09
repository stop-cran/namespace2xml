# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits `0` and writes `a.yaml` containing four lines —
  `- b: 1` / `  c: XXX` / `- b: 2` / `  c: XXX`. The case expects exit `70` and no output.
- **The baseline output is correct, and this preview is the one that is wrong.** Those four
  lines are Section 10.4's own worked example: the template `a.'*'.c=XXX` expanded against the
  two sibling records under `a:`, each generated `c` merged into the record it belongs to. `a`
  itself is absent from the document because the scheme sets `a.output=yaml`, which makes `a`
  the output root rather than a key inside it. 2.4.0 implements the Section 10.4 enrichment on
  this input; the 3.0 preview declines it. This is the only case in the corpus where the
  baseline satisfies the specification and the clean implementation does not, and it is
  recorded here rather than smoothed over because a differential lane that only ever flattered
  the new implementation would not be evidence of anything.
- Contract: Section 10.4, wildcard templates supplied as YAML. This is not a Section 3.1
  preservation case or a Section 3.2 correction — it is a preview refusal, recorded in
  `KNOWN-LIMITS.md` §1.1 for JSON and repeated for YAML: "**A wildcard in a key is declined**,
  with exit `70` and no output, exactly as for JSON, and for the same §12.3 reason." The
  missing machinery is §12.3's requirement that "template-bearing JSON or YAML branches are
  extracted entry-by-entry", which the preview's structured reader cannot express because the
  entry it emits carries an interpreted value.
- Legacy observation: 2.4.0 extracted the wildcard entry from the mapping key `'*'` and expanded
  it during its equivalent of the §12.4 fixed point, producing exactly the specified result. The
  behaviour is not accidental for this shape of input; what 2.4.0 lacks is the surrounding
  typed-payload and mapping-presence discipline that §10.4's "carrier ancestors created only to
  contain an extracted template do not contribute mapping-presence marks" depends on, which is
  why the capability is being rebuilt rather than ported.
- Clean behavior once §10.4 lands: §10.4 states that "unescaped `*` and `*[identifier]` tokens
  use the wildcard-template grammar" and that "wildcard template entries are extracted before
  structural input merging and expanded during the fixed point in Section 12.4". The expected
  document becomes the four lines above, and this fixture's `expected/` and
  `expected-exit-code.txt` are rewritten to that at the same commit — which is the point of
  pinning it now.
- Why the fixture pins the refusal rather than the answer: `KNOWN-LIMITS.md` explains that "a
  preview must never return either for work it did not do. It is a **refusal**, not a diagnostic
  — the run decides no outcome at all, publishes nothing, and says on standard error which
  capability it lacked." Exit `70` is deliberately outside the normative `0` and `1`. Pinning it
  catches the two regressions that would otherwise be silent: a preview that starts guessing at
  wildcard keys and emits plausible-but-unverified output, and a preview that returns `0` or `1`
  for a capability it does not have.
- The difference is **not** an improvement, and the migration notes should be read that way. For
  this input a 2.x user gets the right file today and gets nothing from the preview. The
  refusal is the honest interim behaviour, not the desired one, and closing it is a release
  blocker for 3.0 final rather than a deferred nicety.