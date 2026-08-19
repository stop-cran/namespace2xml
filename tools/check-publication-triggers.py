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

A second review found that the *detection* half had the same shape of flaw: it matched pattern
text against the raw file, so a publish command split across lines -- by a folded scalar, or by an
ordinary shell backslash continuation -- contained no such substring and the workflow was reported
as not publishing at all. The gate failed open, which is the one direction it must never fail.
Detection now runs over the parsed document's scalars, with continuations joined and whitespace
normalised, so a command is matched the way bash reads it rather than the way it is typed. Only
scalars are scanned: a comment mentioning a registry cannot publish to it, and treating prose as
evidence would make the gate fire on the paragraph you are reading.

Patterns are token groups rather than phrases, and all tokens of a group must appear in the same
scalar. `ansible-galaxy` alone would flag the build and install commands, which are not
publication; `ansible-galaxy` together with `publish` in one command is publication.

A shell comment inside a run: block is part of that block's scalar and is matched like any other
text, so prose about a registry inside a script trips this gate. That is deliberate. Teaching the
matcher to strip comments means deciding where a comment ends inside a shell script -- through
quoting, `$#`, and URL fragments -- and every such decision is a place to hide a command. A
false positive here is a build failure with the matched pattern printed next to it; a false
negative is a permanent publication. The cost is one reworded comment.

Two residual gaps are deliberate rather than overlooked. Publishing to a registry this file has
never heard of is reported as clean, which is why PATTERNS is extended whenever the project gains
a shipped artifact rather than only when a rule is found to be wrong. And a command assembled at
runtime from variables cannot be recognised by any static reader. Both are argued down by the
review requirement, not by this gate.
"""

from __future__ import annotations

import glob
import sys

try:
    import yaml
except ImportError:
    print("::error::PyYAML is required to evaluate publication triggers")
    raise SystemExit(1)

# Assembled from fragments so this file cannot match its own pattern, which matters because the
# matcher now reads run: blocks the way a shell would and would recognise these lines verbatim if
# this script were ever inlined into a workflow.
#
# Every token is lowercase; scalars are casefolded before matching. A group matches when all of its
# tokens appear in one scalar.
#
# NuGet/login is here because acquiring a publishing credential is the act worth noticing: under
# trusted publishing there is no stored key to grep for, and a workflow that exchanges an OIDC
# token for one is a publishing workflow whether or not it has yet reached the push.
#
# The Galaxy patterns are the same rule applied to the second registry. The collection under
# ansible/ ships to galaxy.ansible.com under the same trusted name, and Galaxy is strictly less
# forgiving than NuGet: a published collection version cannot be yanked, unlisted or replaced, so
# an unreviewed push is permanent. Galaxy also has no trusted-publishing exchange, only an
# account-wide non-expiring token, which makes the tag restriction the containment rather than a
# defence in depth. Both the repository's own secret name and the two spellings the ecosystem's
# documentation and community actions use are listed, because a gate that only knew the name we
# happened to choose would pass any workflow that chose the conventional one.
#
# --api-key is the catch-all beneath all of them: it is how a credential is handed to either
# registry's client, and it stays recognisable when the surrounding command does not.
PATTERNS = (
    ("nuget" ".org",),
    ("nuget" "_api_key",),
    ("dotnet nuget", "push"),
    ("nuget/" "login",),
    ("galaxy.ansible" ".com",),
    ("ansible_galaxy" "_api_token",),
    ("galaxy_api" "_key",),
    ("ansible-galaxy", "publish"),
    ("--api" "-key",),
)


def scalars(node: object):
    """Yields every scalar in the parsed document, keys included.

    Keys matter: an environment variable named after a publishing credential appears as a mapping
    key, not as a value.
    """
    if isinstance(node, dict):
        for key, value in node.items():
            yield from scalars(key)
            yield from scalars(value)
    elif isinstance(node, (list, tuple)):
        for item in node:
            yield from scalars(item)
    elif node is not None:
        yield str(node)


def normalise(text: str) -> str:
    """Renders a scalar the way the shell will read it, for matching.

    A backslash continuation and a folded scalar both split one command over several lines without
    changing what runs, so both are joined before whitespace is collapsed. Without this, `publish`
    beginning a line reads as a different command to the gate than it does to bash.
    """
    return " ".join(text.replace("\\\n", " ").split()).casefold()


def publishing_evidence(document: object) -> str | None:
    """Returns the matched pattern group, or None if nothing in the document can publish."""
    for scalar in scalars(document):
        haystack = normalise(scalar)
        for group in PATTERNS:
            if all(token in haystack for token in group):
                return " + ".join(group)
    return None


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

    # Parsing first, and failing on a file that will not parse. Detection reads the parsed
    # document, so an unparseable workflow is not a workflow this gate has cleared -- it is one it
    # could not read, and reporting that as clean would make "make the file unparseable" the
    # cheapest bypass there is.
    try:
        document = yaml.safe_load(text)
    except yaml.YAMLError as error:
        return [f"{path} is not parseable YAML, so its triggers cannot be verified: {error}"]

    evidence = publishing_evidence(document)
    if evidence is None:
        return []

    print(f"{path} can publish packages ({evidence}); checking its triggers.")

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