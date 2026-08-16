# Legacy differential

- namespace2xml 2.4.0: **differs**. 2.4.0 delegated argument parsing to CommandLineParser, whose
  grammar was never stated in any contract and whose failures carried no stable code and no
  machine-readable stream.
- Contract: Section 6.2 option-token grammar; Section 26 item 86.
- Legacy observation: unspecified. The library's treatment of `-i=value` was never stated, so a
  caller could not know whether it named a file called `value`, a file called `=value`, or
  nothing at all.
- Clean behavior: short options have no inline form, so the whole token is the option name, it
  names no option in the table, and the invocation is `CLI001` with exit 1.
- Why this case exists: the dangerous outcome here is not the error, it is the silent success. A
  parser that quietly accepted `-i=a` would read a file the author never named.