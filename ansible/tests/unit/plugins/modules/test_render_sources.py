"""What the module makes of the sources a task hands it, before the tool is ever run.

The module's own share of that work is small -- the marshalling itself is shared code, tested
against the specification in ``test_entries.py`` -- but the small share is where the module's
two obligations live: the deprecated inline spellings have to keep the precedence section 15.2
gave them in 2.x, and inline content has to be written somewhere the discovery pass in section
21 will not mistake for a file the render produced.

The expectations are authored from the specification and from the 2.x documented behaviour they
have to preserve, never captured from the module.
"""

from __future__ import annotations

import json
import pathlib
import sys
import types

import pytest

# The module imports `AnsibleModule` at import time. Under `ansible-test units` that import is
# the real one and this stub is never installed; on a bare checkout it is what lets these cases
# run at all. Nothing here calls `AnsibleModule`, so standing in for it costs no coverage: the
# argument spec is exercised by the integration tests, which run a real play.
try:
    import ansible.module_utils.basic  # noqa: F401  # pylint: disable=unused-import
except ImportError:
    _basic = types.ModuleType("ansible.module_utils.basic")
    _basic.AnsibleModule = object
    _mu = types.ModuleType("ansible.module_utils")
    _mu.basic = _basic
    _ansible = types.ModuleType("ansible")
    _ansible.module_utils = _mu
    sys.modules.setdefault("ansible", _ansible)
    sys.modules.setdefault("ansible.module_utils", _mu)
    sys.modules.setdefault("ansible.module_utils.basic", _basic)

try:
    from ansible_collections.stop_cran.namespace2xml.plugins.modules import render as mod
    from ansible_collections.stop_cran.namespace2xml.plugins.module_utils import n2x
except ImportError:  # a checkout that is not inside an ansible_collections tree
    _ANSIBLE = pathlib.Path(__file__).resolve().parents[4]

    if str(_ANSIBLE) not in sys.path:
        sys.path.insert(0, str(_ANSIBLE))

    from plugins.modules import render as mod  # type: ignore[no-redef]
    from plugins.module_utils import n2x  # type: ignore[no-redef]


class _Module:
    """Just enough of ``AnsibleModule`` to stand where ``_scheme_entries`` reads its params."""

    def __init__(self, **params):
        self.params = dict(scheme=[], scheme_text=None, scheme_yaml=None)
        self.params.update(params)


def _names(entries):
    return [entry.name for entry in entries]


# --- writing content out -------------------------------------------------------------------
#
# Section 21 discovers what a render produced by looking at the output directory. Anything this
# module writes has to land somewhere else, and a file the task named has to stay where it is.


def test_a_file_entry_is_passed_where_it_lies(tmp_path):
    """A path the task named is handed over unchanged, not copied into the run's scratch.

    Copying would double the disk a large template costs on the node, and would put a path into
    the tool's own diagnostics that is deleted before anyone can go and look at it.
    """
    source = tmp_path / "shipped.scheme"
    source.write_text("cfg.output=json\n", encoding="utf-8")
    into = tmp_path / "elsewhere"
    into.mkdir()

    entries = mod._scheme_entries(_Module(scheme=[str(source)]))
    resolved = mod._materialize(entries[0], str(into))

    assert resolved == str(source)
    assert list(into.iterdir()) == []


def test_inline_content_is_written_into_the_directory_it_is_given(tmp_path):
    """Text reaches the tool as a file, under the name the marshaller chose for it."""
    into = tmp_path / "tmp"
    into.mkdir()

    entries = mod._scheme_entries(_Module(scheme=[{"text": "cfg.output=json\n"}]))
    resolved = mod._materialize(entries[0], str(into))

    assert pathlib.Path(resolved).parent == into
    assert pathlib.Path(resolved).read_text(encoding="utf-8") == "cfg.output=json\n"


def test_a_written_scheme_carries_the_extension_its_syntax_needs(tmp_path):
    """Section 15 picks the parser from the extension, so a mapping has to land on ``.json``."""
    into = tmp_path / "tmp"
    into.mkdir()

    entries = mod._scheme_entries(_Module(scheme=[{"data": {"cfg": {"output": "json"}}}]))
    resolved = mod._materialize(entries[0], str(into))

    assert resolved.endswith(".json")
    assert json.loads(pathlib.Path(resolved).read_text(encoding="utf-8")) == {
        "cfg": {"output": "json"}}


# --- the deprecated spellings --------------------------------------------------------------
#
# 2.x documented `scheme` and `scheme_text` as usable together, with the inline one winning.
# Section 15.2 makes the later source win, so preserving that means appending, not refusing.


def test_scheme_text_is_appended_after_every_other_scheme():
    module = _Module(scheme=["/a.scheme", "/b.scheme"], scheme_text="cfg.output=json\n")

    assert _names(mod._scheme_entries(module)) == ["/a.scheme", "/b.scheme", "scheme-3.txt"]


def test_scheme_yaml_is_appended_after_every_other_scheme():
    module = _Module(scheme=["/a.scheme"], scheme_yaml={"cfg": {"output": "json"}})

    assert _names(mod._scheme_entries(module)) == ["/a.scheme", "scheme-2.json"]


def test_an_empty_scheme_yaml_is_refused_rather_than_dropped():
    """An empty mapping is a mistake worth reporting, and the collection is what reports it.

    Skipping it on truthiness would turn a scheme that declares nothing into a task that
    silently rendered with whatever the other schemes said -- or, with no other scheme, into a
    different error about there being none.
    """
    with pytest.raises(n2x.Namespace2XmlError) as caught:
        mod._scheme_entries(_Module(scheme_yaml={}))

    assert "empty" in str(caught.value)


def test_a_scheme_entry_may_be_a_mapping_like_the_filters():
    module = _Module(scheme=[{"file": "/a.scheme"}, {"text": "cfg.output=json\n"}])

    assert _names(mod._scheme_entries(module)) == ["/a.scheme", "scheme-2.txt"]


def test_a_scheme_that_is_not_a_list_is_refused_by_name():
    """The refusal names ``scheme``, not the shared code's default argument name."""
    with pytest.raises(n2x.Namespace2XmlError) as caught:
        mod._scheme_entries(_Module(scheme={"text": "cfg.output=json\n"}))

    assert "'scheme'" in str(caught.value)
