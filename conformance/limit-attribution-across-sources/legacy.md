# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 7.3, 11.1, 15.4, 22, 23, and 24.
- Legacy observation: there were no configurable resource bounds at all. A document deep enough,
  wide enough, or numerous enough exhausted memory and the process died without a diagnostic, so
  the failure mode of an oversized input was a stack overflow or an allocation failure rather than
  a report naming the file that caused it.
- Clean behavior: Section 23 makes every bound an option, and a crossing is `LIMIT001`. Section 22
  scopes that code to once per invocation, so when several sources cross bounds together exactly
  one occurrence is reported and Section 11.1 fixes which one: "the earliest under CLI source order
  as defined in Section 7.3, then document order within that source, then element order, then the
  bound name compared as unsigned UTF-8 bytes".
- This case crosses two different kinds of bound in two sources at once. `inputs/many.xml` crosses
  the global `--max-nodes` total, which Section 23 accumulates "at the parse-phase join in CLI
  source order as specified in Section 7.3" — that is, after every source has been parsed.
  `inputs/wide.xml` crosses the per-element `--max-xml-attributes`, which Section 23 checks "per
  element within each source" — that is, while that source is still being parsed. The reported
  occurrence is therefore the one that is decided *later* in time and *earlier* in command-line
  order, and Section 11.1 is explicit that command-line order is what governs: "attribution is
  therefore independent of parser worker scheduling."
- Nothing is published and no output tree is expected. Section 15.4 aborts before the next phase
  when a phase holds a blocking diagnostic, so planning never runs and the run cannot report that
  its output plan is empty.
- The difference is intentional: a tool whose response to an oversized input is to die tells its
  caller nothing, and a tool whose answer depends on which parser finished first cannot be
  compared across runs. Appendix C.7 runs this case under varied worker counts for that reason.
