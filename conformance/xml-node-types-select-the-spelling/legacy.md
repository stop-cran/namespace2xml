# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Section 3.2 correction against unhandled user-input exceptions, together with
  Section 15's rule that "unknown directives are blocking errors" and Section 16.6's
  enumeration of the recognized XML `type` values (`element`, `attribute`, `cdata`, `text`).
- Legacy observation: the baseline exits with an unhandled-exception status
  (`System.AggregateException: One or more errors occurred. (Requested value 'attribute' was
  not found.)`, exit `-532462766`) and produces no `cfg.xml`. The measurement records
  `exit -532462766 (expected 0); missing cfg.xml` with that stderr.
- Clean behavior: each of the four `type` values classifies the corresponding path — `@id` as
  an element under the item, `name` as an attribute, `note` as CDATA, `tail` as text — and
  the resulting `<item name="widget"><id>7</id><![CDATA[x < y]]>trailing</item>` is emitted
  under `<doc>`.
- Why the difference is intentional: 2.4.0 has neither `type=attribute` nor `type=cdata` nor
  `type=text` in its recognized set, so the scheme value fails an enum parse rather than
  being classified as an unknown-directive-value error at the scheme phase. `SCHEME001`
  reports a nonempty scalar value that names an illegal option/type combination cleanly and
  exits `1`; letting the enum parser propagate its `ArgumentException` all the way to the
  process boundary is the unhandled-exception class Section 3.2 removes.
