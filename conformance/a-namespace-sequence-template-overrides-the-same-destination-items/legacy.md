# Legacy differential

- namespace2xml 2.4.0: **agrees** on content and on order, modulo CRLF line endings under the
  Section 24 divergence.
- Contract: Section 10.4 for the equivalence, Section 5.4 for ordering-value provenance.
- This is the namespace spelling of the template in
  `a-native-sequence-template-overrides-the-destination-items-it-addresses`, over the same
  destination, expecting the same bytes. Section 10.4 says a sequence beneath a wildcard key "is the
  same rule as those two entries written in namespace form"; that sentence is a claim about two
  spellings producing one behaviour, and a claim of equality needs both sides measured.
- Written directly, `a.*.tags.0=red` and `a.*.tags.1=blue` are canonical numeric mapping children
  and carry **explicit** Section 5.4 provenance with no extraction step involved. The destination's
  `green` is a native item at implicit `0`. Section 5.4's override rule gives `0=red` to the later
  source and `1=blue` above the high-water mark.
- Alone this case asserts only Section 5.4, which was never in doubt. Its value is the pairing: if
  extraction were ever changed to carry a native sequence whole and preserve implicit provenance,
  this case would keep passing while its sibling failed, and the failure would name the extraction
  rule rather than the merge. Raised as
  [#75](https://github.com/stop-cran/namespace2xml/issues/75).
