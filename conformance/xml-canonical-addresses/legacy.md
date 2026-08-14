# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 5.2, 5.4, and 19.1.
- Legacy observation: XML input carried no address for an attribute, no address for a namespace URI,
  and no address for a text run in mixed content. Element text was discarded outright — measured on
  the baseline, `<cfg><app><name>svc</name></app></cfg>` reads as `app.name=` with an empty value
  even though that leaf has neither attributes nor children, while `<cfg><b x="1"/></cfg>` reads as
  `b.x=1`, so attributes survived and text did not. An element and its attributes therefore
  collapsed into one name, and `<b x="1">two</b>` could express neither `two` nor both together;
  two children with the same name overwrote one another rather than becoming an ordered pair; and a
  prefix was kept as written, so the same element read under two prefix spellings produced two
  different names for one identity.
- Clean behavior: Section 11.4 gives every XML component one canonical address. An attribute is
  `@name`; a name in a namespace is `Q{uri}local`, resolved from the URI rather than the prefix, so
  `p:rev` addresses as `@Q{urn:p}rev` and the reserved `xml` prefix addresses through its fixed
  `http://www.w3.org/XML/1998/namespace` URI. Repeated same-name children become a Section 5.4
  sequence with generated zero-based parts, while a single child keeps its name. An element that
  owns both text and attributes exposes the text at the element path and the attributes beneath it,
  which Section 19.1 emits in pre-order — own scalar first, then children. In mixed content every
  content position takes a `#n` content token, and Section 11.5 retains comments as ordered nodes
  among that content, so `gapped` addresses as `#0` and `#2` with the comment at `#1`. Only Section
  19.5 renders a comment node, so this namespace output drops it and Section 3 summarizes that as
  one `WARN003` for the file. Section 11.6 coalesces adjacent CDATA into one run, so two
  segments read as `ab`. Section 11.7 makes `PreserveWhitespace` the default, which retains every
  text node: an indented document is therefore mixed content, and `pretty` addresses its two
  formatting runs as `#0` and `#2` with the element between them at `#1`. An element with no
  content is an empty mapping, which Section 19.1 spells as the bare `{}` sentinel — measured on
  the baseline, `<cfg><empty/><k>1</k></cfg>` reads as `cfg.empty=` and `cfg.k=`, so an element
  that held nothing and an element whose text was thrown away produced the same line.
- The difference is intentional: Section 11.4 exists so that one XML component has exactly one
  address regardless of how the document spells its prefixes, and every legacy behavior above either
  loses a component the document contained or lets the document's spelling decide its identity.
