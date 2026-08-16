"""Verify Section 19.4's emitted YAML against an independent YAML 1.1 reader.

Section 19.4 quotes a scalar that either published YAML schema would type, so that a reader
returns the string that was written whichever revision its library implements. This is that
test for PyYAML, whose resolver is YAML 1.1 -- the stricter of the two, and the one that
types `yes`, `12:30`, `2001-12-14`, `1_000` and the two key tags.

The oracle is the corpus, not the implementation. For a case whose input is JSON and whose
scheme only names an output format, the emitted YAML must carry exactly the document the
JSON carried: `json.load` fixes the types with no YAML resolution involved, so comparing the
two establishes that a third-party reader gives the values back rather than merely accepting
the file. That is the distinction the M5 review turned on -- the tool's own reader shares the
tool's own resolutions, so a same-format round trip through Section 3.3 succeeds while every
other reader disagrees, and the failure is invisible from inside.

Nothing here imports the implementation, and nothing here re-implements Section 19.4's
spelling rules. A defect in the writer therefore cannot define its own oracle.

The broad lane adds a weaker check over every emitted YAML file in the corpus: the document
must load at all, and every mapping key must come back as a string. Both catch losses that
change no byte the corpus compares -- a document a reader abandons on an unresolvable tag,
and two keys distinct as written that resolve to one.

Usage:  python tools/check-yaml-interop.py [repo-root]
"""

import json
import pathlib
import sys

try:
    import yaml
except ImportError:  # pragma: no cover - the lane installs it
    print("check-yaml-interop: PyYAML is not installed", file=sys.stderr)
    raise SystemExit(2)

# Cases whose emitted bytes carry the spellings the two YAML revisions disagree about. The
# lane is nearly vacuous without them -- every other emitted YAML file in the corpus is
# lowercase ASCII words -- so their absence from the checked set is a failure rather than a
# skip. A check that stops exercising the thing it was built for should say so.
REQUIRED_CASES = (
    "yaml-quotes-every-portably-typed-spelling",
    "yaml-scalar-style-selection",
)


def scheme_prefix(scheme_text):
    """The single selector an output-only scheme names, or None when it names more.

    A scheme that filters, transforms values, or spreads its directives over more than one
    selector makes the emitted document a function of rules this lane does not model, so the
    strong comparison does not apply. One shared selector is modelled: the emitted document
    is then the subtree the JSON carries at that path.

    :param scheme_text: The scheme file's text.
    :returns: The shared selector as a list of components, or None when ineligible.
    """
    prefixes = set()
    for raw in scheme_text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        selector = line.split("=", 1)[0].strip()
        head, _, name = selector.rpartition(".")
        if name.lower() not in ("output", "filename"):
            return None
        prefixes.add(head)
    if len(prefixes) != 1:
        return None
    only = prefixes.pop()
    return only.split(".") if only else []


def strong_lane(root, failures):
    """Compare each eligible case's emitted YAML to the JSON it was built from.

    :param root: The repository root.
    :param failures: A list collecting failure descriptions.
    :returns: The set of case names actually compared.
    """
    checked = set()
    for case in sorted((root / "conformance").iterdir()):
        if not case.is_dir():
            continue
        inputs = sorted((case / "inputs").glob("*.json")) if (case / "inputs").is_dir() else []
        emitted = sorted((case / "expected").glob("*.yaml")) if (case / "expected").is_dir() else []
        schemes = sorted((case / "schemes").glob("*.txt")) if (case / "schemes").is_dir() else []
        if len(inputs) != 1 or len(emitted) != 1 or len(schemes) != 1:
            continue
        prefix = scheme_prefix(schemes[0].read_text(encoding="utf-8"))
        if prefix is None:
            continue

        expected = json.loads(inputs[0].read_text(encoding="utf-8"))
        for component in prefix:
            if not isinstance(expected, dict) or component not in expected:
                expected = None
                break
            expected = expected[component]
        if expected is None:
            continue

        try:
            actual = yaml.safe_load(emitted[0].read_text(encoding="utf-8"))
        except Exception as error:  # noqa: BLE001 - any refusal is the finding
            failures.append(
                "%s: a YAML 1.1 reader abandons '%s' -- %s: %s"
                % (case.name, emitted[0].name, type(error).__name__, str(error).splitlines()[0])
            )
            checked.add(case.name)
            continue

        if actual != expected:
            failures.append(
                "%s: '%s' does not read back as the JSON it was built from\n"
                "      json: %r\n      yaml: %r" % (case.name, emitted[0].name, expected, actual)
            )
        checked.add(case.name)
    return checked


def broad_lane(root, failures):
    """Load every emitted YAML file and check that no key resolves away from a string.

    :param root: The repository root.
    :param failures: A list collecting failure descriptions.
    :returns: The number of files loaded.
    """
    count = 0
    for path in sorted((root / "conformance").glob("*/expected/*.yaml")):
        count += 1
        try:
            document = yaml.safe_load(path.read_text(encoding="utf-8"))
        except Exception as error:  # noqa: BLE001 - any refusal is the finding
            failures.append(
                "%s: a YAML 1.1 reader abandons the document -- %s: %s"
                % (path.relative_to(root), type(error).__name__, str(error).splitlines()[0])
            )
            continue
        for where, key in non_string_keys(document, path.relative_to(root)):
            failures.append("%s: key %r came back as %s, not a string" % (where, key, type(key).__name__))
    return count


def non_string_keys(node, where):
    """Yield every mapping key that did not come back as a string.

    A component name is text, so a key that resolves to a Boolean, a number, or a date is a
    key two distinct spellings can collide on -- the loss Section 19.3's `FLAT001` prevents
    from inside the tool and quoting prevents from outside it.

    :param node: The loaded document or a node within it.
    :param where: The file the node came from, for the message.
    :returns: Pairs of location and offending key.
    """
    if isinstance(node, dict):
        for key, value in node.items():
            if not isinstance(key, str):
                yield where, key
            yield from non_string_keys(value, where)
    elif isinstance(node, list):
        for item in node:
            yield from non_string_keys(item, where)


def main(argv):
    """Run both lanes and report.

    :param argv: Command line arguments; an optional repository root.
    :returns: The process exit code.
    """
    root = pathlib.Path(argv[1] if len(argv) > 1 else ".").resolve()
    failures = []

    checked = strong_lane(root, failures)
    loaded = broad_lane(root, failures)

    missing = [case for case in REQUIRED_CASES if case not in checked]
    if missing:
        failures.append(
            "the cases this lane exists for were not compared: " + ", ".join(sorted(missing))
        )

    if failures:
        print("check-yaml-interop: FAILED", file=sys.stderr)
        for failure in failures:
            print("  - " + failure, file=sys.stderr)
        return 1

    print(
        "check-yaml-interop: %d emitted YAML files load under PyYAML; "
        "%d compared to the JSON they were built from" % (loaded, len(checked))
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
