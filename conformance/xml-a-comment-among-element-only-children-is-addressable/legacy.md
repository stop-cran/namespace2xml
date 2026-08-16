# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 11.5 comment retention as ordered comment nodes; Section 17.4's rule that
  comments alone do not make a parent mixed-content; Section 11.4's assignment of content-token
  ordering values by every parent "including element-only parents"; Section 16.6 `ignore`;
  Section 26 item 16.
- Legacy observation: the baseline exits `0` and writes `r.xml` as the single element
  `<r b="" d="" />` after the XML declaration, with no trailing newline. It writes the same bytes
  with the `r.#1.type=ignore` directive removed, so the directive changes nothing for it.
- Clean behavior: `<r>` holds two element children and a comment. Section 17.4 keeps it
  element-only, so `b` and `d` keep element-name addressing; Section 11.4 still assigns content
  tokens across that parent, so the comment sits at `r.#1` between them. `r.#1.type=ignore` selects
  it and Section 16.6 removes it from this output instance, leaving `<b>` and `<d>` in place.
- Why the difference is intentional: two separate corrections meet here.

  The first is that the baseline **discards element text content on XML import unconditionally**.
  Measured on the same baseline: `<cfg><app><name>svc</name></app></cfg>` reads as `app.name=` with
  an empty value, and `<r>hello</r>` as `=`, while attributes survive — `<cfg><b x="1"/></cfg>`
  reads as `b.x=1`. This is broader than the condition the divergence list records for it, which
  names the case where attributes or children are also present; a leaf element with neither loses
  its text just the same. So the baseline's model here is `r.b=` and `r.d=` with no values, and its
  XML writer emits two valueless names as empty attributes on one element. Section 11.2 and
  Section 11.4 make text a first-class addressable component, which is why `<b>1</b>` survives.

  The second is the comment. The baseline discarded comments at read time, so there was nothing at
  `r.#1` for a directive to select and nothing to remove; Section 11.5 retains the comment, which
  is what makes the address exist and the `ignore` meaningful.

  Both differences are therefore additive: this build carries components the baseline dropped.

## Not asserted

- That `r.#2` does not address the `<d>` element. Section 11.4 says an element-only child carries a
  content-token ordering value "for deterministic placement" without saying whether that token is
  also an address, so the negative is under-determined and is recorded in `KNOWN-LIMITS.md` §1.3
  rather than pinned here.
