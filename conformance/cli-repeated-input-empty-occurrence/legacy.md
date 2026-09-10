# Legacy differential

- namespace2xml 2.4.0: **differs**. It did not specify per-occurrence list arity.
- Contract: Section 6.2 and Section 26 item 93.
- Clean behavior: a later empty `input` occurrence cannot borrow a value from an earlier one.
