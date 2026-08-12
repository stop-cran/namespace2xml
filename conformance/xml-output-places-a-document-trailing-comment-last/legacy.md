# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 4.5; Section 11.5; Section 20.
- Legacy observation: the comment is absent. The output is `<cfg a="1" />`; the attribute
  projection of the scalar is a separate 2.4.0 divergence and is not what this case asserts.
- Clean behavior: no entry follows the comment, so Section 4.5 leaves it without a value owner and
  Section 20 places it after the source's "final surviving contribution". Section 11.5 is what
  makes that position expressible in XML at all: comments "are retained as ordered comment nodes"
  rather than being forced into a "leading comment for the next value" representation, and the
  reason it gives is that a comment may occur "after the final child".

  Emitting it ahead of the first child instead would read as a comment about `a`, which is not
  where the author put it, and is the reassignment Section 11.5 exists to prevent.
