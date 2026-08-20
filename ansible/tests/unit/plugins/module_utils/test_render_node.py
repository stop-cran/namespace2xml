"""What the node-side module does to a node's files, and what it refuses to do to them.

Two properties carry this module, and both are here.

The first is convergence. Section 3 of the specification makes the tool's output deterministic
for identical inputs, and the module turns that guarantee into idempotence by rendering into a
scratch directory and comparing bytes. A test that only checked "it wrote a file" would pass
against an implementation that rewrites every file on every run, which is the defect operators
actually feel: a handler that fires forever.

The second is the refusal to write over an input. Section 16.10 defines C(merge=append) as
rebasing a later sequence contribution onto fresh implicit ordering values above the current
high-water mark, so a render that reads back its own output grows a sequence by one copy per
run. That failure is silent and cumulative, which is exactly the class this collection exists to
turn into an error.

Expectations are authored from the specification -- sections 3, 6.2, 15.2, 16.10 and 18 -- and
never captured from the implementation.
"""

from __future__ import annotations

import os
import pathlib
import sys

import pytest

_PLUGINS = pathlib.Path(__file__).resolve().parents[4] / "plugins"

try:
    from ansible_collections.stop_cran.namespace2xml.plugins.module_utils import (
        n2x, render_node)
except ImportError:  # a checkout that is not inside an ansible_collections tree
    # `module_utils` is an implicit namespace package, so putting `plugins` on the path is
    # enough for the relative import inside render_node to resolve. Loading the file directly
    # would not be: a relative import needs a package, not a lone module.
    if str(_PLUGINS) not in sys.path:
        sys.path.insert(0, str(_PLUGINS))

    from module_utils import n2x, render_node  # type: ignore[no-redef]


def write(path, text):
    """A file with known bytes, with its parent created."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")

    return path


# --- Section 6.2: the argument vector is the play's order, not a sorted one ---------------------

def test_inputs_and_schemes_keep_the_order_they_were_given():
    # Section 15.2 makes a later directive win over an earlier one, so reordering either list
    # would change the render. A set or a sort here would be a silent semantic change.
    argv = render_node.build_argv(
        ["b.yml", "a.yml"], ["second.scheme", "first.scheme"], None, "/out")

    assert argv == [
        "-i", "b.yml", "-i", "a.yml",
        "-s", "second.scheme", "-s", "first.scheme",
        "-o", "/out",
    ]


def test_variables_follow_the_schemes_and_precede_the_output_root():
    argv = render_node.build_argv(["a.yml"], ["s"], {"x.y": "1"}, "/out")

    assert argv[-4:] == ["-v", "x.y=1", "-o", "/out"]


def test_a_render_with_no_input_is_refused():
    with pytest.raises(n2x.Namespace2XmlError, match="src"):
        render_node.build_argv([], ["s"], None, "/out")


def test_a_render_with_no_scheme_is_refused():
    # Section 6.2 marks -s required, and for a reason a message should carry: with no output
    # directive the run succeeds and produces nothing.
    with pytest.raises(n2x.Namespace2XmlError, match="scheme"):
        render_node.build_argv(["a.yml"], [], None, "/out")


# --- Variables are namespace paths, passed through verbatim ------------------------------------

def test_a_variable_name_is_not_escaped():
    # The whole value of this option is that section 11's XML addressing is reachable. Escaping
    # '@' here would turn an attribute reference into a literal element name part.
    assert render_node.encode_variable(
        "configuration.root.@level", "DEBUG") == "configuration.root.@level=DEBUG"


def test_a_boolean_is_spelled_the_way_the_contract_spells_it():
    # YAML 1.2 and section 18 spell these lowercase. Python's str(True) would hand the tool
    # 'True', which is a string scalar rather than the boolean the playbook author wrote.
    assert render_node.encode_variable("a.b", True) == "a.b=true"
    assert render_node.encode_variable("a.b", False) == "a.b=false"


def test_a_null_variable_is_refused_rather_than_guessed():
    with pytest.raises(n2x.Namespace2XmlError, match="null"):
        render_node.encode_variable("a.b", None)


@pytest.mark.parametrize("value", [{"k": "v"}, ["a", "b"]])
def test_a_structured_variable_is_refused_and_points_at_the_filter(value):
    with pytest.raises(n2x.Namespace2XmlError) as failure:
        render_node.encode_variable("a.b", value)

    assert "filter" in str(failure.value)


# --- Section 16.10: an input is never a destination --------------------------------------------

def test_writing_over_an_input_is_refused(tmp_path):
    source = write(tmp_path / "conf" / "logback.xml", "<configuration/>")

    with pytest.raises(n2x.Namespace2XmlError) as failure:
        render_node.guard_sources([str(source)], str(tmp_path / "conf"), ["logback.xml"])

    # The message has to say why, not just that. An operator who is told "refused" will work
    # around it; one who is told the sequence grows by a copy per run will not.
    assert "16.10" in str(failure.value)


def test_a_sibling_file_in_the_same_directory_is_allowed(tmp_path):
    source = write(tmp_path / "conf" / "input.yml", "a: 1")

    render_node.guard_sources([str(source)], str(tmp_path / "conf"), ["logback.xml"])


def test_the_refusal_survives_a_different_spelling_of_the_same_file(tmp_path):
    # The trap is a property of the files, not of how the path was written, so a relative src
    # against an absolute dest has to be caught just the same.
    source = write(tmp_path / "logback.xml", "<configuration/>")
    here = os.getcwd()
    os.chdir(str(tmp_path))

    try:
        with pytest.raises(n2x.Namespace2XmlError):
            render_node.guard_sources(["logback.xml"], str(tmp_path), ["logback.xml"])
    finally:
        os.chdir(here)


# --- Section 3: determinism is what makes a byte comparison an answer --------------------------

def test_a_file_that_is_absent_from_the_destination_counts_as_changed(tmp_path):
    write(tmp_path / "scratch" / "out.xml", "<a/>")
    (tmp_path / "dest").mkdir()

    assert render_node.plan(
        str(tmp_path / "scratch"), str(tmp_path / "dest"), ["out.xml"]) == ["out.xml"]


def test_identical_content_is_not_changed(tmp_path):
    write(tmp_path / "scratch" / "out.xml", "<a/>")
    write(tmp_path / "dest" / "out.xml", "<a/>")

    # A shallow comparison would consult mtime, and the scratch copy is always newer because it
    # was written by this very run -- so every file would report changed on every run.
    assert render_node.plan(str(tmp_path / "scratch"), str(tmp_path / "dest"), ["out.xml"]) == []


def test_content_differing_only_in_trailing_whitespace_is_changed(tmp_path):
    write(tmp_path / "scratch" / "out.xml", "<a/>\n")
    write(tmp_path / "dest" / "out.xml", "<a/>")

    assert render_node.plan(
        str(tmp_path / "scratch"), str(tmp_path / "dest"), ["out.xml"]) == ["out.xml"]


def test_files_are_discovered_beneath_subdirectories_with_stable_order(tmp_path):
    write(tmp_path / "root" / "b.xml", "b")
    write(tmp_path / "root" / "nested" / "a.xml", "a")

    assert render_node.discover(str(tmp_path / "root")) == ["b.xml", "nested/a.xml"]


# --- The whole render, against a stand-in for the binary ---------------------------------------

@pytest.fixture
def tool(monkeypatch):
    """A resolved binary and a proven identity, so a test can exercise the logic around them."""
    monkeypatch.setattr(render_node, "resolve", lambda tool=None: "/stand-in/namespace2xml")
    monkeypatch.setattr(render_node, "tool_identity", lambda tool=None: "3.0.0|r99+aaaa")


def producing(files, diagnostics=""):
    """A runner that writes fixed content into whatever scratch directory it is handed."""
    def run(dummy_executable, argv):
        out = argv[argv.index("-o") + 1]

        for relative, text in files.items():
            target = pathlib.Path(out) / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(text, encoding="utf-8")

        return diagnostics

    return run


def test_a_first_render_writes_and_reports_changed(tmp_path, tool):
    dest = tmp_path / "dest"
    dest.mkdir()
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    result = render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), runner=producing({"out.xml": "<a/>"}))

    assert result["changed"] is True
    assert (dest / "out.xml").read_text(encoding="utf-8") == "<a/>"
    assert result["changed_files"] == [os.path.join(str(dest), "out.xml")]


def test_a_second_identical_render_reports_no_change(tmp_path, tool):
    dest = tmp_path / "dest"
    dest.mkdir()
    write(dest / "out.xml", "<a/>")
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    result = render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), runner=producing({"out.xml": "<a/>"}))

    assert result["changed"] is False
    assert result["changed_files"] == []


def test_check_mode_reports_the_change_without_making_it(tmp_path, tool):
    dest = tmp_path / "dest"
    dest.mkdir()
    write(dest / "out.xml", "old")
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    result = render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), check_mode=True, runner=producing({"out.xml": "new"}))

    assert result["changed"] is True
    assert (dest / "out.xml").read_text(encoding="utf-8") == "old"


def test_a_diff_carries_both_sides(tmp_path, tool):
    dest = tmp_path / "dest"
    dest.mkdir()
    write(dest / "out.xml", "old")
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    result = render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), check_mode=True, runner=producing({"out.xml": "new"}))

    assert result["diff"][0]["before"] == "old"
    assert result["diff"][0]["after"] == "new"


def test_diagnostics_from_a_successful_run_are_passed_through(tmp_path, tool):
    # A successful render that weakened a guarantee still has to say so. WARN007 arrives on
    # every XML round trip that enables NormalizeFormattingWhitespace, and swallowing it would
    # hide the one fact an operator needs about that render.
    dest = tmp_path / "dest"
    dest.mkdir()
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    result = render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), runner=producing({"out.xml": "<a/>"}, "WARN007: weakened\n"))

    assert result["diagnostics"] == "WARN007: weakened"


def test_a_render_that_produced_nothing_is_a_failure(tmp_path, tool):
    # Exit 0 with no files means the scheme named no output. Reporting "ok, unchanged" would be
    # the wrong answer to a play that asked for a file.
    dest = tmp_path / "dest"
    dest.mkdir()
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    with pytest.raises(n2x.Namespace2XmlError, match="no output files"):
        render_node.render(
            src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
            scratch=str(scratch), runner=producing({}))


def test_the_destination_is_never_written_when_an_input_would_be_overwritten(tmp_path, tool):
    dest = tmp_path / "dest"
    dest.mkdir()
    source = write(dest / "logback.xml", "<configuration/>")
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    with pytest.raises(n2x.Namespace2XmlError):
        render_node.render(
            src=[str(source)], schemes=["s"], dest=str(dest), scratch=str(scratch),
            runner=producing({"logback.xml": "<configuration changed=\"yes\"/>"}))

    assert source.read_text(encoding="utf-8") == "<configuration/>"
