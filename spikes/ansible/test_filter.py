"""Oracle checks for the prototype filter.

Two sides, because a filter that is checked only against itself proves nothing:

* **Side A** compares :func:`flatten` against profile text authored by hand from Sections 8.2, 8.3
  and 8.7. If the encoder and the specification disagree, this is where it shows.
* **Side B** runs the tool over that hand-authored profile and requires the bytes to equal what
  :func:`render` produced from the data. That is the marshalling being right -- the right files
  written, the right invocation, the right output read back.

Neither side asks the filter what correct means. Run it with the tool on PATH, or point
``NAMESPACE2XML`` at a binary:

    python spikes/ansible/test_filter.py
"""

import os
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "filter_plugins"))

# .gitignore may not exclude anything under spikes/, so a stray __pycache__ would be committed.
sys.dont_write_bytecode = True

import namespace2xml_filter as n2x  # noqa: E402

FAILURES = []


def check(name, actual, expected):
    """Compare two strings and record a failure with both spellings."""
    if actual == expected:
        print("  ok    %s" % name)
        return True

    FAILURES.append(name)
    print("  FAIL  %s" % name)
    print("    expected: %r" % expected)
    print("    actual:   %r" % actual)

    return False


def cli_render(profile, scheme):
    """Run the tool by hand over given profile and scheme text, and return the single output."""
    directory = tempfile.mkdtemp(prefix="n2x-oracle-")

    try:
        input_path = os.path.join(directory, "input.txt")
        scheme_path = os.path.join(directory, "scheme.txt")
        output_dir = os.path.join(directory, "out")
        os.mkdir(output_dir)

        for path, text in ((input_path, profile), (scheme_path, scheme)):
            with open(path, "w", encoding="utf-8", newline="\n") as handle:
                handle.write(text)

        executable = os.environ.get("NAMESPACE2XML", "namespace2xml")
        completed = subprocess.run(
            [executable, "-i", input_path, "-s", scheme_path, "-o", output_dir],
            capture_output=True,
            text=True,
            check=False,
        )

        if completed.returncode != 0:
            raise SystemExit(
                "oracle run failed with exit %d:\n%s"
                % (completed.returncode, (completed.stderr or "").strip()))

        produced = [
            os.path.join(base, name)
            for base, _, names in os.walk(output_dir)
            for name in names]

        with open(produced[0], "r", encoding="utf-8", newline="") as handle:
            return handle.read()
    finally:
        shutil.rmtree(directory, ignore_errors=True)


# Each case is (name, data, hand-authored profile, format, root).
#
# The profile column is written from the specification and is the thing under test. Do not
# regenerate it from flatten(); a captured expectation asserts nothing.
CASES = [
    (
        "ordinary nesting, a sequence, inferred types and an empty mapping",
        {
            "appender": [{"name": "STDOUT", "level": "DEBUG"}, {"name": "FILE"}],
            "enabled": True,
            "retries": 3,
            "empty": {},
        },
        # Section 8.7: the canonical decimal parts 0 and 1 make 'appender' a sequence.
        # Section 18: 'true' is a boolean and '3' an integer, from the value text alone.
        "cfg.appender.0.name=STDOUT\n"
        "cfg.appender.0.level=DEBUG\n"
        "cfg.appender.1.name=FILE\n"
        "cfg.enabled=true\n"
        "cfg.retries=3\n"
        "cfg.empty={}\n",
        "xml",
        "configuration",
    ),
    (
        "name parts that would otherwise be delimiters, wildcards or typed XML components",
        {
            "a.b": "x",
            "with*star": "y",
            "Q{urn}": "z",
            "@attr": "w",
            "#1": "v",
            "tab\tkey": "u",
            "vt\u000bkey": "s",
            "zwsp\u200bkey": "t",
        },
        # Section 8.2: '.' '*' '@' '#' and '}' take their short escapes; a leading 'Q' takes
        # '\\Q' so the part is not read as a Q{...} canonical component; a Cc or Cf scalar has no
        # short form and takes '\\u{HEX}'.
        #
        # Section 8.2 accepts either case on input, so the tool reads '\\u{b}' and '\\u{B}' alike
        # and side B cannot see the difference. The uppercase spelling is what Section 19.1 emits,
        # and it is pinned here, on the side that compares text. U+0009 alone would not pin it:
        # '9' is the same in both cases, which is why U+000B and U+200B are here.
        "cfg.a\\.b=x\n"
        "cfg.with\\*star=y\n"
        "cfg.\\Q{urn\\}=z\n"
        "cfg.\\@attr=w\n"
        "cfg.\\#1=v\n"
        "cfg.tab\\u{9}key=u\n"
        "cfg.vt\\u{B}key=s\n"
        "cfg.zwsp\\u{200B}key=t\n",
        "json",
        None,
    ),
    (
        "values carrying backslashes, wildcards, reference starts, newlines and sentinels",
        {
            "backslash": "C:\\Users\\alice",
            "star": "a*b",
            "ref": "${x}",
            "newline": "l1\nl2",
            "brace": "{}",
            "bracket": "[]",
            "emptylist": [],
            "nil": None,
        },
        # Section 8.3: '\\' '*' and '${' are escaped; a record is a line so LF is written '\\n';
        # a value of exactly '{}' or '[]' is the empty-container sentinel, so the *string* takes
        # '\\{}' and '\\[]'. An empty list is the sentinel itself. None is the null payload.
        "cfg.backslash=C:\\\\Users\\\\alice\n"
        "cfg.star=a\\*b\n"
        "cfg.ref=\\${x}\n"
        "cfg.newline=l1\\nl2\n"
        "cfg.brace=\\{}\n"
        "cfg.bracket=\\[]\n"
        "cfg.emptylist=[]\n"
        "cfg.nil=null\n",
        "json",
        None,
    ),
]


def side_a():
    """The encoder against profile text authored from the specification."""
    print("side A -- flatten() against hand-authored profiles")

    for name, data, profile, _, _ in CASES:
        check(name, n2x.flatten(data, "cfg"), profile)


def side_b():
    """The marshalling against the tool run by hand over the same profile."""
    print("side B -- render() against the tool run by hand")

    for name, data, profile, fmt, root in CASES:
        scheme = n2x.synthesize_scheme(fmt, "cfg", root)
        check(name, n2x.render(data, fmt, root=root, memoize=False), cli_render(profile, scheme))


def cache_behaviour():
    """The memoization is consulted, and can be bypassed.

    Poisoning the cache is a deterministic way to prove the lookup happens. Timing is not: a fast
    second call is also what an un-memoized fast tool looks like.
    """
    print("cache -- the memo is consulted and bypassable")

    data = {"k": "v"}
    first = n2x.render(data, "json", memoize=True)
    key = n2x._cache_key(
        n2x.flatten(data, "cfg"),
        n2x.synthesize_scheme("json", "cfg", None),
        "json",
        n2x.tool_identity())

    if key not in n2x._RENDER_CACHE:
        FAILURES.append("cache key absent after a memoized render")
        print("  FAIL  cache key absent after a memoized render")
        return

    n2x._RENDER_CACHE[key] = "POISONED"

    check("a memoized call returns the cached text", n2x.render(data, "json", memoize=True),
          "POISONED")
    check("memoize=False bypasses the cache", n2x.render(data, "json", memoize=False), first)

    del n2x._RENDER_CACHE[key]


def main():
    print("tool: %s" % os.environ.get("NAMESPACE2XML", "namespace2xml"))
    print("identity: %s\n" % n2x.tool_identity())

    side_a()
    print("")
    side_b()
    print("")
    cache_behaviour()

    print("")

    if FAILURES:
        print("%d check(s) failed" % len(FAILURES))
        return 1

    print("all checks passed")

    return 0


if __name__ == "__main__":
    sys.exit(main())
