# Legacy differential

- namespace2xml 2.4.0: **differs**. It renders the same value, then diverges on bytes: `cfg.json`
  ends at `}` with no final newline. It also writes `Environment.NewLine` rather than LF, so the
  same input produces different bytes on Windows and on Linux; the missing final newline is the
  divergence that survives on every platform. It drops the `k` child and the comment without a
  word. Exit 0. **verified** — measured against the Appendix C.6 pinned 2.4.0 package.
- Contract: Section 4.4 step 4, under which "the losing shape is omitted from that output and
  produces one shape-conflict warning"; Section 4.4's discard-before-step-2 rule and the boundary it
  draws; Section 24's trailing newline.
- Legacy observation: 2.4.0 silently discarded a child element that carried a value. The output is
  well-formed and the loss is invisible, which is the failure mode this corpus exists to remove.
- Clean behavior: the node holds a comment *and* a `k` child. The discard removes the comment, but
  the mapping still has a member afterwards, so it remains a container contribution and does enter
  the contest. The scalar arrives later and wins, and step 4 makes the losing mapping produce one
  `TYPE002` per output instance.
- This is the boundary of the rule the sibling case
  `a-comment-only-container-does-not-contest-a-later-scalar` pins. Without this case an
  implementation could suppress `TYPE002` for any losing mapping that merely *contains* a discarded
  comment, and nothing in the corpus would object. The condition is that every member goes, not
  that some member goes.
- No `WARN003` accompanies the warning. The comment is not discarded for being a comment here; it
  is omitted along with the whole mapping that lost, and Section 3.3 summarizes concepts discarded
  *during rendering*. Nothing under this node is rendered at these destinations, and Section 4.4
  step 4 already reports the loss once. A second warning would tell the author the comment could
  not be represented, which is not why it is missing from this file.
