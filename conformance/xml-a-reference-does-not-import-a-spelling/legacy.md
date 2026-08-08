# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 11.4's rule that "the element-path scalar and its sole text/CDATA
  content-token scalar are two canonical addresses for one scalar identity, not two candidates
  in the simple-alias ambiguity index", and Section 11.6's `<![CDATA[...]]>` preservation as a
  distinct node kind of the origin element rather than of a referring element. Section 3 does
  not enumerate this specifically; the substantive contract is Sections 11.4 and 11.6.
- Legacy observation: the baseline writes different bytes at `r.xml`. The measurement records
  `content r.xml` at exit `0` with no standard error beyond the banner.
- Clean behavior: `${r.a}` resolves to the scalar `7` at each of its three referring sites, and
  the destination element's own spelling decides whether that `7` is written as CDATA or as
  text. `r.d` was written as `<![CDATA[${r.a}]]>` so its resolved value is emitted as CDATA;
  `r.e` was written as `${r.a}` in ordinary text so its resolved value is emitted as text; the
  namespace-supplied `r.c=${r.a}` from `extra.txt` becomes an ordinary text child. No CDATA
  identity travels along the reference edge.
- Why the difference is intentional: a scalar reference carries a value, not a spelling. Having
  `${r.a}` import CDATA-ness into every element it appears in would make one node's node kind
  a property of the elements pointing at it, which contradicts Section 11.4's "two canonical
  addresses for one scalar identity" model and would make round-trip stability of every
  referring element a function of the referent's spelling. 2.4.0 has no stated typed-XML model
  for references at all, so whether the baseline's specific bytes come from importing the
  CDATA spelling into `r.e` and `r.c`, from stripping CDATA everywhere, from a different
  handling of the identity `<a>${r.a}</a>` at `r.a` itself, or from some combination is not
  readable from one divergent file. The fixture pins only that the bytes are wrong.
