#!/usr/bin/env python3
"""Synchronizes generated targets in Ansible collection documentation links."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    import yaml
    from yaml.nodes import MappingNode, ScalarNode
except ImportError:
    print("error: PyYAML is required to synchronize Ansible documentation links")
    raise SystemExit(1)

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
GALAXY_MANIFEST = REPOSITORY_ROOT / "ansible" / "galaxy.yml"
SPECIFICATION_NAVIGATION = (
    REPOSITORY_ROOT / "spec" / "specification-navigation.json"
)
SYNC_COMMAND = "python tools/sync-ansible-doc-links.py"
LINK_REF = re.compile(
    rb"(?P<prefix>https://github\.com/stop-cran/namespace2xml/(?:blob|tree)/)"
    rb"(?P<ref>[^/\s<>\"]+)"
    rb"(?P<suffix>/)"
)
SPECIFICATION_CITATION = re.compile(
    rb"\[(?P<label>`?(?:(?:\xc2\xa7)|(?i:section[ ]|appendix[ ]))"
    rb"(?P<section>(?:[0-9]+(?:\.[0-9]+)*|[A-Z](?:\.[0-9]+)*))`?)\]"
    rb"\((?P<base>https://github\.com/stop-cran/namespace2xml/blob/"
    rb"[^/\s<>\"]+/docs/specification\.md)"
    rb"(?P<fragment>#[^)\s<>\"]+)?\)"
)
README_COLLECTION_VERSION = re.compile(
    rb"^This collection is at (?P<version>[^\s\r\n]+) while the tool is at 3\.x\.",
    re.MULTILINE,
)
SAFE_VERSION = re.compile(r"[0-9A-Za-z][0-9A-Za-z.+-]*")
SAFE_SECTION = re.compile(r"(?:[0-9]+(?:\.[0-9]+)*|[A-Z](?:\.[0-9]+)*)")
SAFE_SPECIFICATION_ANCHOR = re.compile(
    r"spec-(?:[0-9]+(?:-[0-9]+)*|[a-z](?:-[0-9]+)*)"
)
URL_TERMINATORS = b" \t\r\n<>\"')"


@dataclass(frozen=True)
class StaleLink:
    """One generated documentation link target that needs normalization."""

    path: str
    line: int
    found: str
    expected: str
    kind: str = "release ref"


def specification_anchors(
    path: Path = SPECIFICATION_NAVIGATION,
) -> dict[str, str]:
    """Loads the generated clause-to-anchor map without reparsing Markdown."""
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{path} is not valid navigation JSON: {error}") from error

    if (
        not isinstance(document, dict)
        or document.get("generatedBy")
        != "tools/sync-specification-navigation.ps1"
        or not isinstance(document.get("clauses"), list)
    ):
        raise ValueError(
            f"{path} is not a generated specification-navigation manifest"
        )

    anchors: dict[str, str] = {}
    used_anchors: set[str] = set()
    for clause in document["clauses"]:
        if not isinstance(clause, dict):
            raise ValueError(f"{path} contains a non-object clause")

        section = clause.get("section")
        anchor = clause.get("anchor")
        if (
            not isinstance(section, str)
            or SAFE_SECTION.fullmatch(section) is None
            or not isinstance(anchor, str)
            or SAFE_SPECIFICATION_ANCHOR.fullmatch(anchor) is None
        ):
            raise ValueError(f"{path} contains an invalid clause")
        if section in anchors:
            raise ValueError(f"{path} contains duplicate clause {section!r}")
        if anchor in used_anchors:
            raise ValueError(f"{path} contains duplicate anchor {anchor!r}")

        anchors[section] = anchor
        used_anchors.add(anchor)

    if not anchors:
        raise ValueError(f"{path} contains no clauses")
    return anchors


def collection_version(path: Path = GALAXY_MANIFEST) -> str:
    """Reads exactly one scalar top-level version from galaxy.yml."""
    text = path.read_text(encoding="utf-8")

    try:
        root = yaml.compose(text)
    except yaml.YAMLError as error:
        raise ValueError(f"{path} is not valid YAML: {error}") from error

    if not isinstance(root, MappingNode):
        raise ValueError(f"{path} must contain one top-level YAML mapping")

    versions = [
        value
        for key, value in root.value
        if isinstance(key, ScalarNode) and key.value == "version"
    ]
    if len(versions) != 1:
        raise ValueError(
            f"{path} must declare exactly one top-level version; found {len(versions)}"
        )

    version_node = versions[0]
    if not isinstance(version_node, ScalarNode):
        raise ValueError(f"{path}'s top-level version must be a scalar")

    version = version_node.value
    if not SAFE_VERSION.fullmatch(version):
        raise ValueError(
            f"{path}'s top-level version {version!r} is not safe in an ansible-v* tag"
        )

    return version


def collection_ref(path: Path = GALAXY_MANIFEST) -> str:
    """Returns the release ref covering the collection's source commit."""
    return f"ansible-v{collection_version(path)}"


def normalize_readme_collection_version(
    relative_path: str,
    data: bytes,
    expected_version: str,
) -> tuple[bytes, list[StaleLink]]:
    """Keeps the README's current collection version tied to galaxy.yml."""
    matches = list(README_COLLECTION_VERSION.finditer(data))
    if len(matches) != 1:
        raise ValueError(
            "ansible/README.md must contain exactly one current collection-version sentence"
        )

    match = matches[0]
    found = match.group("version").decode("ascii")
    if found == expected_version:
        return data, []

    normalized = (
        data[: match.start("version")]
        + expected_version.encode("ascii")
        + data[match.end("version") :]
    )
    return normalized, [
        StaleLink(
            path=relative_path,
            line=data.count(b"\n", 0, match.start()) + 1,
            found=found,
            expected=expected_version,
            kind="collection version",
        )
    ]


def tracked_ansible_files() -> list[tuple[str, Path]]:
    """Returns every tracked file under ansible/, in Git's stable path order."""
    result = subprocess.run(
        ["git", "-C", str(REPOSITORY_ROOT), "ls-files", "-z", "--", "ansible"],
        check=True,
        capture_output=True,
    )
    relative_paths = [
        raw.decode("utf-8")
        for raw in result.stdout.split(b"\0")
        if raw
    ]
    return [(relative, REPOSITORY_ROOT / relative) for relative in relative_paths]


def normalize_links(
    relative_path: str,
    data: bytes,
    release_ref: str,
) -> tuple[bytes, list[StaleLink], int]:
    """Returns normalized bytes, stale-link details, and the total matching link count."""
    expected_ref = release_ref.encode("ascii")
    stale: list[StaleLink] = []

    for match in LINK_REF.finditer(data):
        if match.group("ref") == expected_ref:
            continue

        url_end = match.end()
        while url_end < len(data) and data[url_end] not in URL_TERMINATORS:
            url_end += 1

        found_bytes = data[match.start() : url_end]
        expected_bytes = (
            match.group("prefix")
            + expected_ref
            + match.group("suffix")
            + data[match.end() : url_end]
        )
        stale.append(
            StaleLink(
                relative_path,
                data.count(b"\n", 0, match.start()) + 1,
                found_bytes.decode("utf-8"),
                expected_bytes.decode("utf-8"),
            )
        )

    normalized, count = LINK_REF.subn(
        lambda match: match.group("prefix") + expected_ref + match.group("suffix"),
        data,
    )
    return normalized, stale, count


def normalize_specification_citations(
    relative_path: str,
    data: bytes,
    anchors: dict[str, str],
) -> tuple[bytes, list[StaleLink], int]:
    """Targets visible clause citations at their generated stable anchors."""
    stale: list[StaleLink] = []

    def replace(match: re.Match[bytes]) -> bytes:
        section = match.group("section").decode("ascii")
        anchor = anchors.get(section)
        if anchor is None:
            line = data.count(b"\n", 0, match.start()) + 1
            raise ValueError(
                f"{relative_path}:{line}: specification citation names "
                f"unknown clause {section!r}"
            )

        expected = (
            b"["
            + match.group("label")
            + b"]("
            + match.group("base")
            + b"#"
            + anchor.encode("ascii")
            + b")"
        )
        found = match.group(0)
        if found != expected:
            stale.append(
                StaleLink(
                    relative_path,
                    data.count(b"\n", 0, match.start()) + 1,
                    found.decode("utf-8"),
                    expected.decode("utf-8"),
                    "specification citation target",
                )
            )
        return expected

    normalized, count = SPECIFICATION_CITATION.subn(replace, data)
    return normalized, stale, count


def check_ansible_doc_json(path: Path, release_ref: str, surface: str) -> int:
    """Checks every same-repository link in one ansible-doc JSON rendering."""
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        print(f"error: cannot read {surface} JSON from {path}: {error}")
        return 1

    pending: list[object] = [document]
    links: list[tuple[str, str]] = []
    while pending:
        value = pending.pop()
        if isinstance(value, str):
            data = value.encode("utf-8")
            for match in LINK_REF.finditer(data):
                url_end = match.end()
                while url_end < len(data) and data[url_end] not in URL_TERMINATORS:
                    url_end += 1
                links.append(
                    (
                        data[match.start() : url_end].decode("utf-8"),
                        match.group("ref").decode("utf-8"),
                    )
                )
        elif isinstance(value, dict):
            pending.extend(value.values())
        elif isinstance(value, list):
            pending.extend(value)

    if not links:
        print(
            f"error: {surface} contains no same-repository blob/tree "
            "documentation links"
        )
        return 1

    unexpected = [url for url, found_ref in links if found_ref != release_ref]
    if unexpected:
        print(f"error: {surface} contains links not pinned to {release_ref}:")
        for url in unexpected:
            print(f"  {url}")
        return 1

    print(f"{surface}: all {len(links)} same-repository link(s) use {release_ref}.")
    return 0


def normalize_diagnostic_doc_links(data: bytes, release_ref: str) -> bytes:
    """Converts approved immutable collection URLs to repository-local doc links."""
    absolute_base = (
        f"https://github.com/stop-cran/namespace2xml/blob/{release_ref}/".encode("ascii")
    )
    replacements = (
        (absolute_base + b"docs/specification.md", b"specification.md"),
        (
            absolute_base + b"spec/diagnostic-stream.schema.json",
            b"../spec/diagnostic-stream.schema.json",
        ),
        (absolute_base + b"CONTRIBUTING.md", b"../CONTRIBUTING.md"),
    )

    normalized = data
    for absolute, local in replacements:
        if absolute not in normalized:
            raise ValueError(
                "Ansible diagnostic documentation is missing approved link base "
                f"{absolute.decode('ascii')}"
            )
        normalized = normalized.replace(absolute, local)

    return normalized


def check_diagnostic_docs(
    root_path: Path,
    ansible_path: Path,
    release_ref: str,
) -> int:
    """Checks semantic equivalence after normalizing only approved link bases."""
    try:
        root_data = root_path.read_bytes()
        ansible_data = ansible_path.read_bytes()
        normalized = normalize_diagnostic_doc_links(ansible_data, release_ref)
    except (OSError, ValueError) as error:
        print(f"error: {error}")
        return 1

    if root_data != normalized:
        print(
            "error: diagnostic references differ after approved link-base "
            "normalization"
        )
        return 1

    print(
        "docs/diagnostics.md and ansible/docs/diagnostics.md are equivalent "
        "apart from approved immutable link bases."
    )
    return 0


def synchronize(check: bool) -> int:
    """Checks or updates every tracked Ansible text file."""
    try:
        release_ref = collection_ref()
        expected_collection_version = collection_version()
        anchors = specification_anchors()
        files = tracked_ansible_files()
    except (OSError, subprocess.CalledProcessError, UnicodeError, ValueError) as error:
        print(f"error: {error}")
        return 1

    stale_refs: list[StaleLink] = []
    stale_citations: list[StaleLink] = []
    stale_metadata: list[StaleLink] = []
    updates: list[tuple[str, Path, bytes]] = []
    link_count = 0
    citation_count = 0

    try:
        for relative_path, path in files:
            data = path.read_bytes()
            if b"\0" in data:
                continue

            normalized = data
            if relative_path == "ansible/README.md":
                normalized, stale = normalize_readme_collection_version(
                    relative_path,
                    normalized,
                    expected_collection_version,
                )
                stale_metadata.extend(stale)

            normalized, stale, matched = normalize_links(
                relative_path,
                normalized,
                release_ref,
            )
            link_count += matched
            stale_refs.extend(stale)

            normalized, stale, matched = normalize_specification_citations(
                relative_path,
                normalized,
                anchors,
            )
            citation_count += matched
            stale_citations.extend(stale)

            if normalized != data:
                updates.append((relative_path, path, normalized))
    except (OSError, UnicodeError, ValueError) as error:
        print(f"error: {error}")
        return 1

    all_stale = [*stale_refs, *stale_citations, *stale_metadata]
    if check and all_stale:
        for stale in all_stale:
            print(f"{stale.path}:{stale.line}: stale {stale.kind}")
            print(f"  found:    {stale.found}")
            print(f"  expected: {stale.expected}")
        print(f"error: run '{SYNC_COMMAND}' and commit the regenerated links")
        return 1

    if updates:
        try:
            for _, path, normalized in updates:
                path.write_bytes(normalized)
        except OSError as error:
            print(f"error: {error}")
            return 1

        print(
            f"Normalized {len(stale_refs)} release ref(s) and "
            f"{len(stale_citations)} specification citation target(s) "
            f"and {len(stale_metadata)} collection metadata value(s) "
            f"in {len(updates)} file(s)."
        )
    else:
        print(
            f"All {link_count} same-repository blob/tree link(s) under ansible/ "
            f"use {release_ref}; all {citation_count} linked specification "
            "citation(s) use generated stable anchors; the README collection "
            f"version is {expected_collection_version}."
        )

    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Pin tracked Ansible links to the collection version and generated "
            "specification anchors."
        )
    )
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument(
        "--check",
        action="store_true",
        help="report every stale link without writing files",
    )
    mode.add_argument(
        "--print-ref",
        action="store_true",
        help="print the derived ansible-v<collection-version> ref",
    )
    mode.add_argument(
        "--check-ansible-doc-json",
        type=Path,
        metavar="PATH",
        help="check same-repository links in an ansible-doc --json rendering",
    )
    mode.add_argument(
        "--check-diagnostic-docs",
        type=Path,
        nargs=2,
        metavar=("ROOT_PATH", "ANSIBLE_PATH"),
        help=(
            "check root and Ansible diagnostic references for equivalence "
            "after approved link-base normalization"
        ),
    )
    parser.add_argument(
        "--surface",
        default="ansible-doc",
        help=argparse.SUPPRESS,
    )
    arguments = parser.parse_args()

    if (
        arguments.print_ref
        or arguments.check_ansible_doc_json
        or arguments.check_diagnostic_docs
    ):
        try:
            release_ref = collection_ref()
        except (OSError, UnicodeError, ValueError) as error:
            print(f"error: {error}", file=sys.stderr)
            return 1

        if arguments.print_ref:
            print(release_ref)
            return 0
        if arguments.check_diagnostic_docs:
            return check_diagnostic_docs(
                arguments.check_diagnostic_docs[0],
                arguments.check_diagnostic_docs[1],
                release_ref,
            )
        return check_ansible_doc_json(
            arguments.check_ansible_doc_json,
            release_ref,
            arguments.surface,
        )

    if arguments.surface != "ansible-doc":
        parser.error("--surface requires --check-ansible-doc-json")

    return synchronize(arguments.check)


if __name__ == "__main__":
    sys.exit(main())
