# Repeated captures constrain the partition, so matching must be able to reconsider

Section 12.1 fixes the partition: "Captures are assigned left to right, each taking the shortest
text that still permits **the remaining pattern to match**." Section 12.2 makes a reused identifier
a constraint rather than a second free capture -- "the same identifier reused in the name must match
the same text" -- and disposes of the failure case: "inconsistent repeated captures are nonmatches."

Read together, the qualifying clause in Section 12.1 is the whole of the requirement. The shortest
binding is not the shortest binding that lets the *next* token match; it is the shortest that lets
everything after it match. Where an identifier recurs, "everything after it" includes the later
occurrence, whose text is already decided. A matcher that fixes each capture on first sight and never
reconsiders answers a different question, and answers it wrongly in both directions.

`secret` is the direction that costs a secret. `!secret.*[x]*[x]` suppresses a name whose part is
some text written twice. Against `aa`, the first `*[x]` bound to the shortest text that lets the
next token match at all is the empty string; the second `*[x]` is then constrained to the empty
string too, and two empty strings cannot consume `aa`. The empty binding is therefore not viable,
and Section 12.1 asks for the shortest binding that *is*: `a`, leaving `a` for the constrained
second occurrence. The mask matches and `secret.aa` is suppressed. `secret.ab` has no such
partition at any length, so Section 12.2 makes it a nonmatch and it survives. An implementation that
cannot revisit the first binding reports no match for either, and publishes the value it was told to
hide.

`gen` is the direction that costs a match, and it shows that the reconsideration cannot stop at a
name part. Section 12.3 says a wildcard "matches only within one name part", but capture scope is
"one profile or scheme entry", so the constraint an identifier imposes reaches across the
delimiter. In `gen.*[0]x*[1].b.*[0].z`, the binding chosen for `*[0]` in the second component is
rejected by the fourth. Against `gen.pxqxr.b.pxq`, the shortest viable binding *for the second
component alone* is `0=p`, `1=qxr`; the fourth component then demands `0=pxq` and the match fails.
The shortest binding that permits the remaining pattern -- all of it -- is `0=pxq`, `1=r`. Section
12.3 matches a template "through its last wildcard-containing name part", which here is the fourth,
so the literal suffix `z` is appended to `gen.pxqxr.b.pxq` and the generated entry is
`gen.pxqxr.b.pxq.z=hit`.

`gen.pxqxr.b.pxq.v=1` is present so the grafted entry lands beside an existing sibling rather than
creating the node, and `v` precedes `z` under Section 5.4 by both source order and text, so the
expected order does not rest on the tie-breaker.

Neither root emits a diagnostic. Section 12.2 makes an inconsistent repeat a nonmatch and not an
error, so `secret.ab` is silent; both rules match something, and `*[1]` going unused in the
generated value is not a diagnosed condition -- the only wildcard codes are `WILDCARD001` for an
invalid, undefined, mixed or inconsistent *capture declaration* and `WILDCARD002` for a limit.

## Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 12.1's rule that each capture takes "the shortest text that still permits
  **the remaining pattern to match**", together with Section 12.2's constraint that a reused
  identifier "must match the same text". Section 3 does not enumerate these individually; they
  are the substantive contract under test.
- Legacy observation: the baseline writes different bytes at both `secret.properties` and
  `gen.properties`. The measurement records
  `content gen.properties; content secret.properties` at exit `0` with no standard error beyond
  the banner.
- Clean behavior: on the `secret` root, `!secret.*[x]*[x]` must reconsider its first binding
  from the empty string to `a` so that the constrained second occurrence can consume the
  remaining `a` of `aa`, suppressing `secret.aa` and publishing only `ab=keep`. On the `gen`
  root, the fourth component's requirement `0=pxq` forces the second component to bind
  `0=pxq`, `1=r` so that the whole pattern matches `gen.pxqxr.b.pxq`, and the generated entry
  lands beside the existing `v=1` sibling.
- Why the difference is intentional: 2.4.0's wildcard matcher has no stated capture grammar and
  no reconsideration model. Fixing each `*` to its shortest first-fit binding answers a
  different question than Section 12.1 asks, and answering it produces both directions of the
  observable defect visible here — a mask fails to suppress a name it should suppress, and a
  template fails to graft a name it should graft. Whether the baseline's specific bytes come
  from a non-backtracking legacy `*` matcher applied to the explicit-capture syntax, from
  treating `*[x]` as literal text, or from a third reading is not readable from the observed
  files; the fixture pins only that both roots' outputs are wrong.

