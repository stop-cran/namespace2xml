# Legacy differential

- namespace2xml 2.4.0: **fails**.
- Contract: Sections 6.4.3, 15.1, and 15.4 put these declaration checks in the
  scheme phase and require independent checks to finish before that phase aborts.
  Sections 16.7, 16.8, 16.10, and 22 require a `SCHEME001` for each invalid declaration.
- Legacy observation: 2.4.0 terminates with an unhandled `ArgumentException`
  for the unsupported substitute type `Invalid` and publishes no output.
- Clean behavior: all three invalid declarations are reported in source order before
  the later planning phase can attempt the scalar-to-array conversion. The run
  exits `1` without publishing anything.
