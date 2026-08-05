#!/usr/bin/env python3
"""Fails the build if any workflow can publish a package from anything but a tag push.

namespace2xml 2.x pushed to the registry on every push to master, so an unreviewed artifact could
appear under a trusted package name. This gate exists so that cannot happen again.

It lives outside .github/workflows on purpose: inlined in a workflow, the check's own pattern text
matches the file it is written in, and the gate reports a violation against itself.

The previous version grepped for an indented "tags:" and rejected an indented "branches:". A
dual-model review demonstrated three bypasses: a second trigger such as pull_request or
workflow_dispatch sits at a different indent and was never seen; a .yaml extension was not scanned
at all; and an unmatched glob made the gate exit 0 having scanned nothing. Parsing the document and
allowlisting the trigger set is the only form of this check that answers the actual question.
"""

from __future__ import annotations

import glob
import sys

try:
    import yaml
except ImportError:
    print("::error::PyYAML is required to evaluate publication triggers")
    raise SystemExit(1)

# Assembled from fragments so this file cannot match its own pattern.
PATTERNS = ("nuget" ".org", "NUGET" "_API_KEY", "dotnet nuget " "push")


def triggers(document: object) -> object:
    """Returns the 'on' mapping.

    YAML 1.1 resolves a bare 'on' key to the boolean True, so a loader returns True rather than
    'on'. Reading only one of the two spellings would make this gate blind to a real workflow.
    """
    if not isinstance(document, dict):
        return None
    for key in ("on", True):
        if key in document:
            return document[key]
    return None


def check(path: str) -> list[str]:
    with open(path, encoding="utf-8") as handle:
        text = handle.read()

    if not any(pattern in text for pattern in PATTERNS):
        return []

    print(f"{path} can publish packages; checking its triggers.")

    try:
        document = yaml.safe_load(text)
    except yaml.YAMLError as error:
        return [f"{path} is not parseable YAML, so its triggers cannot be verified: {error}"]

    on = triggers(document)

    if on is None:
        return [f"{path} publishes packages but declares no triggers"]

    if isinstance(on, str):
        on = {on: None}
    if isinstance(on, list):
        on = dict.fromkeys(on)
    if not isinstance(on, dict):
        return [f"{path} publishes packages with an unrecognised trigger form"]

    extra = sorted(str(key) for key in on if key != "push")
    if extra:
        return [
            f"{path} publishes packages and also triggers on: {', '.join(extra)}. "
            "A tag push must be the only way to reach a publishing job."
        ]

    push = on.get("push")
    if not isinstance(push, dict):
        return [f"{path} publishes packages on every push, not only on tags"]

    filters = sorted(str(key) for key in push if key != "tags")
    if filters:
        return [
            f"{path} publishes packages with non-tag push filters: {', '.join(filters)}"
        ]

    if not push.get("tags"):
        return [f"{path} publishes packages with an empty tag filter"]

    return []


def main() -> int:
    workflows = sorted(
        glob.glob(".github/workflows/*.yml") + glob.glob(".github/workflows/*.yaml")
    )

    if not workflows:
        print("::error::no workflow files were found; this gate must never pass by scanning nothing")
        return 1

    failures = [failure for path in workflows for failure in check(path)]

    for failure in failures:
        print(f"::error::{failure}")

    if failures:
        return 1

    print(f"Publication triggers are tag-only across {len(workflows)} workflows.")
    return 0


if __name__ == "__main__":
    sys.exit(main())