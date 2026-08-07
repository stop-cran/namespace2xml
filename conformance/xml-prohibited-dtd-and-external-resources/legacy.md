# Legacy differential

- namespace2xml 2.4.0: **differs**.
- Contract: Sections 11.1, 22, and 24.
- Legacy observation: XML was read with the host library's defaults, which resolve a document type
  definition. An internal subset therefore defined entities that were expanded into values, a
  `SYSTEM` identifier could cause the process to retrieve a resource named by the input document,
  and a nested-entity document consumed memory proportional to the expansion rather than to the
  file. A configuration file could thus decide what the tool read and how much of it.
- Clean behavior: Section 11.1 prohibits a document type definition outright — "rejected, not
  partially processed" — so the internal subset is never read and its entities are never defined,
  and external entity resolution and network retrieval are refused with it. Each failing document is
  `XML001` at the Section 22 one-based line and column of the `<!DOCTYPE` token, so a declaration
  that follows an XML declaration and a comment reports at line 3 rather than line 1, and every
  failing source reports in the Section 7.3 command-line order. Nothing is published: a blocking
  input diagnostic decides the run before any output exists.
- The difference is intentional: an input file that can name a resource, or expand to an
  unbounded size, makes the tool's behavior a function of the data it is given rather than of the
  invocation, and Section 11.1 removes that entirely rather than bounding it.
