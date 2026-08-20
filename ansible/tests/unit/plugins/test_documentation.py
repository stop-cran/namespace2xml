# -*- coding: utf-8 -*-
# Copyright (c) 2024 namespace2xml contributors
# GNU General Public License v3.0+ (see LICENSES/GPL-3.0-or-later.txt)

"""The plugin documentation blocks must be loadable YAML.

Ansible reads ``DOCUMENTATION``, ``EXAMPLES`` and ``RETURN`` with a YAML loader, so a block
that does not parse is not a formatting slip -- it takes ``ansible-doc`` down and empties the
plugin's page on Galaxy. The failure is easy to introduce and invisible while writing, because
the blocks are ordinary Python strings that no interpreter ever looks inside: prose containing
``: `` silently becomes a mapping key, and the loader fails several lines later on a line that
is not the one at fault.

``ansible-test sanity`` catches this, but only on Linux and only in CI. This gate is a
hundredth of a second and runs wherever the developer is, which is where the mistake is made.
"""

import io
import os
import re
import tokenize

import pytest
import yaml

PLUGIN_ROOT = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(
        os.path.dirname(os.path.abspath(__file__))))), "plugins")

DOC_BLOCK = re.compile(
    r"^(DOCUMENTATION|EXAMPLES|RETURN) = (r?)(\"\"\"|''')(.*?)\3", re.M | re.S)


def _plugin_sources():
    for directory, _unused_dirs, names in os.walk(PLUGIN_ROOT):
        if "__pycache__" in directory:
            continue

        for name in sorted(names):
            if name.endswith(".py"):
                yield os.path.join(directory, name)


def _blocks():
    found = []

    for path in sorted(_plugin_sources()):
        with io.open(path, encoding="utf-8") as handle:
            source = handle.read()

        for match in DOC_BLOCK.finditer(source):
            found.append(pytest.param(
                match.group(4), match.group(2),
                id="%s::%s" % (os.path.basename(path), match.group(1))))

    return found


BLOCKS = _blocks()


def test_the_collection_actually_has_documentation_blocks_to_check():
    """Guard the guard: a broken regex here would turn every check below into a silent pass."""
    assert len(BLOCKS) >= 6


@pytest.mark.parametrize("block,raw", BLOCKS)
def test_a_documentation_block_parses_as_yaml(block, raw):
    yaml.safe_load(block)


@pytest.mark.parametrize("block,raw", BLOCKS)
def test_a_documentation_block_is_a_raw_string(block, raw):
    """Section 8 spells a literal dot ``\\.``, and the docs quote it.

    In a non-raw string Python reads ``\\.`` as an unknown escape -- a DeprecationWarning today
    and a SyntaxWarning from 3.12 -- and the surrounding text keeps working, so nothing forces
    the issue until a release makes it an error.
    """
    assert raw == "r"


@pytest.mark.parametrize(
    "path", sorted(_plugin_sources()),
    ids=lambda path: os.path.basename(os.path.dirname(path)) + "/" + os.path.basename(path))
def test_a_plugin_source_compiles_without_an_invalid_escape(path, recwarn):
    with io.open(path, encoding="utf-8") as handle:
        source = handle.read()

    compile(source, path, "exec")

    offenders = [str(warning.message) for warning in recwarn
                 if "escape" in str(warning.message)]

    assert not offenders, offenders


@pytest.mark.parametrize(
    "path", sorted(_plugin_sources()),
    ids=lambda path: os.path.basename(os.path.dirname(path)) + "/" + os.path.basename(path))
def test_a_plugin_source_tokenizes(path):
    """A cheap syntax check that does not import, so it runs on a platform without ``grp``."""
    with io.open(path, "rb") as handle:
        list(tokenize.tokenize(handle.readline))
