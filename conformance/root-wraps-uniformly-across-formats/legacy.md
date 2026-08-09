# Legacy differential

- namespace2xml 2.4.0: **differs**. It exits 0 but produces a non-uniform mixture across
  the six outputs the case exercises. `cfg.ini` writes `[x:y]` correctly and `cfg.xml`
  writes `<x><y ... /></x>` correctly, but `cfg.json` is `{"name":"demo","port":8080}` and
  `cfg.yaml` is `name: demo` / `port: 8080` — both with the `x.y` root wrap missing
  entirely. `cfg.properties` is `x.y.name="demo"` with the value double-quoted rather than
  bare, and **no `cfg.sh` file is produced at all**. On standard output the baseline logs
  `Overriding output ... cfg.properties quotednamespace`, meaning it wrote the
  quotednamespace projection over the namespace projection at the same filename.
- Contract: Section 16.3 `root` uniformity across formats. Section 3.2 as a correction of
  behaviour caused by "a synthetic internal root leaking into user-visible file names" —
  the same class of defect makes the `.sh` extension go missing here.
- Legacy observation: 2.4.0 applied `root` per-format rather than uniformly, and its
  filename resolution treated `quotednamespace` and `namespace` as the same format for the
  purpose of choosing the extension. The XML and INI writers implemented `root` because
  their document models can wrap content in a labelled container without touching the
  serialization of individual keys; the JSON and YAML writers were written before the
  uniform-`root` rule was added and did not receive it. The quotednamespace output was
  routed to `cfg.properties`, and its later contribution overrode the namespace output at
  the same filename — which is what "Overriding output" means in the log — so the `.sh`
  file was never opened and the namespace projection was replaced by a quoted-value
  namespace file. Under the 3.0 rule these are independent output instances at independent
  filenames, and none of them can override any of the others.
- Clean behavior: §16.3 states that "the root path wraps the selected content uniformly"
  and enumerates every format's rendering for `root=x.y`: "namespace output prefixes keys
  with `x.y`; JSON emits `{"x":{"y":...}}`; YAML emits `x: { y: ... }` in normalized YAML
  form; XML emits `<x><y>...</y></x>`; INI prefixes the section/key path with `x` and `y`."
  §16.2's default-filename table gives each format its own extension, so six formats
  produce six distinct destinations and no override occurs.
- The difference is intentional: an author writing `root=x.y` is expressing one wrapping
  choice for the whole invocation, and an implementation that applies it in three formats
  and omits it in three has published outputs that no longer name the same logical
  hierarchy. Downstream consumers that overlay the six files back on top of one another —
  the whole reason cross-format conversion exists — will read four different roots. And a
  writer that silently overwrites one destination with another at the same filename hides
  a scheme mistake the caller could otherwise repair.
