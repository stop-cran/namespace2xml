# Legacy differential

- namespace2xml 2.4.0: **agrees**. It writes `b.properties` with `k=2`, writes nothing for `a`, and
  emits no warning -- but it has no `WARN009` and no diagnostic stream at all, so its silence is the
  silence of a tool that cannot say this. Measured, not assumed: exit 0 and byte-identical content.
- Contract: Section 15.2; Section 16.1; Section 22.
- Clean behavior: `a.filename=out.ini` names the instance `a`, which `a.output=ignore` suppresses.
  The instance still exists -- Section 16.1 keeps it so that "a later non-ignore `output`
  declaration" can restore it -- so the directive has bound and no `WARN009` is owed. Section 15.2
  now states the test as existence rather than effect.
- This is the case that separates the two phrasings the contract used to carry. Under Section 22's
  former "binds to no *effective* output or path" the directive has bound to nothing effective and a
  warning was owed; under Section 15.2's "binds to no concrete output instance" it had bound and none
  was. Nothing pinned the difference, so either could have been implemented without a test noticing.
- The reason to settle it this way is what `output=ignore` is for. Configuring an output fully and
  then disabling it with one line is the workflow, and warning on every directive of a disabled
  output makes that line noisy in proportion to how completely the output was configured -- the
  better the configuration, the louder the complaint. The instance is also one declaration away from
  being live, so the warning would describe a configuration that is about to be correct.
- The companion is `type-ignore-removes-a-subtree-and-strands-its-directives`, which **does** warn.
  The distinction is what the directive configures: `a.filename` and `a.output=ignore` configure one
  instance between them, while `cfg.a.p.type` and `cfg.a.type=ignore` are separate declarations about
  separate paths, the second of which silently voided the first. Together the two cases pin both
  sides, and either one alone would leave the rule looking arbitrary.
- `b` is present so the run has an output at all. Without it the case would also be asserting the
  Section 14.1 empty-instance warning, and a case that can fail for two reasons pins neither.