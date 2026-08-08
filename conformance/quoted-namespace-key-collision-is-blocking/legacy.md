# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Section 16.4; Section 19.2.
- Legacy observation: quoted-namespace output joined path parts without checking that the join was
  injective, so two distinct logical paths could silently become one shell assignment and the
  later one would win.
- Clean behavior: Section 16.4 requires that "two distinct logical paths must never silently
  become one namespace, shell, or INI key". Quoted namespace has no escape for the underscore it
  joins with, so `a_b.c` and `a.b_c` both project to `a_b_c`; the second is a blocking
  `FLAT001` naming the view-relative path, and no file is written.
- The difference is intentional: an output that loses a value is worse than an output that is
  refused, and the collision is a property of the projection rather than of the data.
