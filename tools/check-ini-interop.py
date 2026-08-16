"""Verify the PortableIni1 dialect against the parser docs/format-ini.md names.

Specification Section 19.6 requires that conformance tests cover every parser an
implementation's compatibility documentation names, and that naming a parser means naming
the reader configuration and the envelope the claim holds within. This is that test for
Python's `configparser`.

The oracle is the emitted file itself. For every expected `.ini` output inside the envelope
this reads the file with the documented configuration, re-serializes what `configparser`
returned under Section 19.6's layout rules, and compares that to the file's own key and
section lines. Anything the parser drops, folds, splits, reorders, or rewrites shows up as a
difference, so the check establishes agreement rather than mere acceptance -- which is the
distinction Section 19.6 draws, because a reader that silently folds a key reports success
and returns a different document.

Nothing here imports the implementation. The comparison is between a published file and a
third-party parser, so there is no way for a defect in the writer to define its own oracle.

Usage:  python tools/check-ini-interop.py [repo-root]
"""

import configparser
import pathlib
import re
import sys

# Section 19.6, as documented in docs/format-ini.md. Changing any of these changes the
# published compatibility claim, so they are stated once, here, and quoted in that document.
#
#   interpolation=None    -- the dialect has no interpolation; `%` is ordinary value text.
#   delimiters=('=',)     -- Section 19.6 permits `:` in a key name and uses it as the
#                            section separator, so it cannot also separate key from value.
#   comment_prefixes      -- both markers the dialect can emit.
#   optionxform = str     -- Section 19.6 emits the key text; it does not case-fold.
#
# `default_section` is set to a name the dialect cannot produce (it is not in the
# `[A-Za-z0-9_.:-]+` grammar) so that no emitted section is ever silently treated as a
# source of inherited defaults.
DEFAULT_SECTION_SENTINEL = "\x00 no default section \x00"

# Dialect options with no expression in configparser. A file produced under either one is
# outside the published envelope; Section 19.6 makes that a limit on the pairing rather than
# a defect in either side.
OUT_OF_ENVELOPE_OPTIONS = ("QuoteValues", "EscapeMultiline")

# A `[selector.]inioutputoptions=` declaration, wherever it appears in a scheme file.
OPTION_LINE = re.compile(r"^\s*(?:(.*)\.)?inioutputoptions\s*=\s*(.*?)\s*$", re.IGNORECASE)

# The corpus cases whose emitted bytes carry the shapes parsers actually disagree about.
# The lane is nearly vacuous without them -- every other in-envelope corpus file is lowercase
# ASCII with no `%` and no `:` -- so their absence from the checked set is a failure rather
# than a skip. A check that stops exercising the thing it was built for should say so.
REQUIRED_CASES = (
    "ini-a-colon-inside-a-key-is-key-text",
    "ini-a-key-keeps-the-letter-case-it-was-given",
    "ini-a-percent-sign-in-a-value-is-ordinary-text",
)


def reader():
    """A parser configured exactly as docs/format-ini.md states."""
    parser = configparser.ConfigParser(
        interpolation=None,
        delimiters=("=",),
        comment_prefixes=(";", "#"),
        default_section=DEFAULT_SECTION_SENTINEL,
        strict=True,
    )
    parser.optionxform = str
    return parser


def significant(text):
    """The file's own section and key lines, in order, ignoring comments and blank lines."""
    lines = []
    for line in text.split("\n"):
        if not line.strip():
            continue
        if line.lstrip().startswith((";", "#")):
            continue
        lines.append(line)
    return lines


def reserialize(parser):
    """What configparser recovered, laid out under the Section 19.6 rules."""
    lines = []
    for section in parser.sections():
        lines.append(f"[{section}]")
        for key, value in parser.items(section):
            lines.append(f"{key}={value}")
    return lines


def selected_options(case, stem):
    """The dialect options in force at one destination.

    Attributed per destination rather than per case, because one case can write two INI files
    under different options and the corpus contains one that does. The destination's path is
    the file name without its extension, so a selector prefix applies when it names that path
    or an ancestor of it, and the last applicable line wins under Section 16.1's rule that a
    later option set replaces the earlier one completely.
    """
    chosen = ""
    schemes = case / "schemes"
    for scheme in sorted(schemes.glob("*.txt")) if schemes.is_dir() else []:
        for line in scheme.read_text(encoding="utf-8").split("\n"):
            match = OPTION_LINE.match(line)
            if match is None:
                continue
            selector = match.group(1) or ""
            if selector == "" or stem == selector or stem.startswith(selector + "."):
                chosen = match.group(2)
    return [flag.strip().lower() for flag in chosen.split(",")]


def envelope_exclusion(case, stem, text):
    """Why this file is outside the published envelope, or None if it is inside."""
    flags = selected_options(case, stem)
    for option in OUT_OF_ENVELOPE_OPTIONS:
        if option.lower() in flags:
            return f"selects {option}"

    for line in significant(text):
        if line.startswith("["):
            break
        return "writes a preamble"

    return None


def main():
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    corpus = root / "conformance"
    if not corpus.is_dir():
        print(f"no corpus at {corpus}", file=sys.stderr)
        return 2

    checked, skipped, failures = [], [], []

    for path in sorted(corpus.glob("*/expected/**/*.ini")):
        case = path.parent
        while case.name != "expected":
            case = case.parent
        case = case.parent
        label = f"{case.name}/{path.relative_to(case / 'expected').as_posix()}"

        text = path.read_text(encoding="utf-8")

        reason = envelope_exclusion(case, path.stem, text)
        if reason is not None:
            skipped.append((label, reason))
            print(f"skip  {label}  ({reason})")
            continue

        parser = reader()
        try:
            parser.read_string(text)
            got = reserialize(parser)
        except configparser.Error as exc:
            # Read-back errors surface here, not only parse errors: an interpolation fault is
            # raised when the value is fetched, well after read_string has returned. Catching
            # both around one block is what keeps a disagreeing parser a reported failure
            # naming the file rather than a stack trace naming configparser's internals.
            failures.append((label, f"{type(exc).__name__}: {str(exc).splitlines()[0]}"))
            print(f"FAIL  {label}  {type(exc).__name__}")
            continue

        want = significant(text)
        checked.append((label, case.name))
        if want == got:
            print(f"ok    {label}")
            continue

        detail = next(
            (f"line {i + 1}: file {w!r} != parser {g!r}"
             for i, (w, g) in enumerate(zip(want, got)) if w != g),
            f"{len(want)} lines emitted, {len(got)} recovered",
        )
        failures.append((label, detail))
        print(f"FAIL  {label}  {detail}")

    exercised = {case for _, case in checked}
    missing = [case for case in REQUIRED_CASES if case not in exercised]
    for case in missing:
        print(f"FAIL  {case} is not being checked -- the lane has gone vacuous")

    print(f"\nchecked={len(checked)} skipped={len(skipped)} "
          f"failures={len(failures) + len(missing)}")

    if failures or missing:
        print(
            "\nThe emitted file and the named parser disagree. Either the writer has "
            "changed, or\ndocs/format-ini.md now overstates the claim; Section 19.6 requires "
            "the two to match.",
            file=sys.stderr,
        )
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
