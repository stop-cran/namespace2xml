"""What the node-side module does to a node's files, and what it refuses to do to them.

Two properties carry this module, and both are here.

The first is convergence. Section 24 of the specification makes the tool's output deterministic
for identical inputs, and the module turns that guarantee into idempotence by rendering into a
scratch directory and comparing bytes. A test that only checked "it wrote a file" would pass
against an implementation that rewrites every file on every run, which is the defect operators
actually feel: a handler that fires forever.

The second is the refusal to write over an input. Section 16.10 defines C(merge=append) as
rebasing a later sequence contribution onto fresh implicit ordering values above the current
high-water mark, so a render that reads back its own output grows a sequence by one copy per
run. That failure is silent and cumulative, which is exactly the class this collection exists to
turn into an error.

A third property arrived with the security review: the module publishes into a directory an
unprivileged user may control, often under C(become). Section 21.1 requires no-follow,
handle-relative publication with symbolic-link containment, so the tests below prove a symlinked
destination is refused rather than followed.

Expectations are authored from the specification -- sections 6.2, 8.3, 15.2, 16.10, 18, 21.1 and
24 -- and never captured from the implementation.
"""

from __future__ import annotations

import os
import pathlib
import stat
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


def _filter_encode_value(value):
    """The controller-side filter's own encoder, loaded the way the filter tests load it.

    Importing it lazily keeps this file's other tests runnable where ansible-core is not
    importable, which is every Windows checkout.
    """
    try:
        from ansible_collections.stop_cran.namespace2xml.plugins.filter import (  # noqa: E501
            render as filter_render)
    except ImportError:
        import importlib.util

        path = _PLUGINS / "filter" / "render.py"
        spec = importlib.util.spec_from_file_location("n2x_filter_for_drift", path)
        filter_render = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(filter_render)

    return filter_render.encode_value(value)


def write(path, text):
    """A file with known bytes, with its parent created."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")

    return path


# Section 21.1 requires publication through no-follow, handle-relative operations. Where the
# platform cannot provide them the module refuses to write at all -- deliberately, since the
# collection declares C(platforms: posix) and a node is POSIX. These tests exercise that path
# and therefore only run where it exists; CI runs them on Linux, which is where the module runs.
_confined = pytest.mark.skipif(
    not render_node._CONFINEMENT,
    reason="section 21.1 publication needs POSIX dir_fd and O_NOFOLLOW")


# --- Section 6.2: the argument vector is the play's order, not a sorted one ---------------------

def test_inputs_and_schemes_keep_the_order_they_were_given():
    # Section 15.2 makes a later directive win over an earlier one, so reordering either list
    # would change the render. A set or a sort here would be a silent semantic change.
    argv = render_node.build_argv(
        ["b.yml", "a.yml"], ["second.scheme", "first.scheme"], None, "/out")

    assert argv == [
        "--input=b.yml", "--input=a.yml",
        "--scheme=second.scheme", "--scheme=first.scheme",
        "--output=/out",
    ]


def test_an_option_value_can_begin_with_a_dash():
    # Section 6.2's inline form binds the value to the option, so a filename the play did not
    # choose -- one beginning with '-' -- cannot be reparsed as an option. With the detached
    # form the tool rejects it with CLI001, which is a render that fails on a legal filename.
    argv = render_node.build_argv(["-weird.yml"], ["s"], None, "/out")

    assert "--input=-weird.yml" in argv


def test_variables_follow_the_schemes_and_precede_the_output_root():
    argv = render_node.build_argv(["a.yml"], ["s"], {"x.y": "1"}, "/out")

    assert argv[-2:] == ["--variables=x.y=1", "--output=/out"]


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
    # Section 18 would infer Python's 'True' as a boolean too, so this is not about type
    # inference. It is about section 24: one canonical spelling for one value, so that two nodes
    # handed the same playbook produce byte-identical output and the filter and the module do
    # not disagree. YAML 1.2 and the filter's encoder both spell it lowercase.
    assert render_node.encode_variable("a.b", True) == "a.b=true"
    assert render_node.encode_variable("a.b", False) == "a.b=false"


# --- Section 8.3: a value is data, and the tool is told so -------------------------------------

@pytest.mark.parametrize("raw,expected", [
    ("C:\\temp\\new", "C:\\\\temp\\\\new"),
    ("${JAVA_HOME}/bin", "\\${JAVA_HOME}/bin"),
    ("a*b", "a\\*b"),
    ("first\nsecond", "first\\nsecond"),
    ("tab\there", "tab\\there"),
    ("{}", "\\{}"),
    ("[]", "\\[]"),
])
def test_a_value_is_escaped_so_the_tool_reads_it_as_data(raw, expected):
    # These are not hypothetical. Section 8.3 gives a value a lexer, so 'C:\temp\new' reaches
    # the tool as a tab and a line feed, and section 8.4 makes '${JAVA_HOME}' a reference that
    # fails the whole render with REFERENCE002 when the name does not resolve. A variable
    # carries a value, never a fragment of syntax.
    assert render_node.encode_variable("a.b", raw) == "a.b=" + expected


@pytest.mark.parametrize("raw", ["a=b", "a.b", "a,b", "a;b", "has#hash", " padded ", "a:b"])
def test_a_character_that_is_not_value_syntax_is_left_alone(raw):
    # Section 8.3 is closed: only '\\', '\*', '\${', '\n', '\r' and '\t' mean anything, and
    # "other backslash sequences preserve the backslash and following character". So escaping
    # '=' or '.' here would not protect them -- it would insert a literal backslash into the
    # operator's value. '=' is safe besides: section 6.2's '--variables=' splits on the first
    # '=' only, and '#' only opens a comment as the first character of a profile line.
    assert render_node.encode_variable("a.b", raw) == "a.b=" + raw


def test_a_name_is_not_escaped_even_though_its_value_is():
    # The asymmetry is the point: the name is a namespace path the play means structurally, the
    # value is opaque data. Escaping both, or neither, breaks one of the two.
    assert render_node.encode_variable("a.b", "${x}") == "a.b=\\${x}"
    assert render_node.encode_variable(
        "configuration.root.@level", "DEBUG") == "configuration.root.@level=DEBUG"


def test_the_module_and_the_filter_escape_a_value_the_same_way():
    # The two encoders are separate copies -- the filter runs on the controller under a
    # different Python -- so nothing but a test keeps them in step. A divergence here means the
    # same playbook value renders differently depending on which plugin the play used.
    corpus = [
        "plain", "C:\\temp\\new", "${JAVA_HOME}", "a=b", "a.b", "a,b", "a;b", "a#b",
        " pad ", "{}", "[]", "a{b}c", "line\nbreak", "tab\there", "", "\\", "*", "$",
        "$notabrace", "\\${already}", "{}x", "x{}",
    ]

    for value in corpus:
        assert n2x.encode_value(value) == _filter_encode_value(value), value


@pytest.mark.parametrize("value", [float("nan"), float("inf"), float("-inf")])
def test_a_non_finite_number_is_refused_rather_than_rendered(value):
    # Python spells these 'nan' and 'inf'. Section 18 has no such scalar, so the tool would take
    # the text as a string and the play would silently get a configuration value it never meant.
    with pytest.raises(n2x.Namespace2XmlError):
        render_node.encode_variable("a.b", value)


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
        render_node.guard_sources(
            [str(source)], [], str(tmp_path / "conf"), ["logback.xml"])

    # The message has to say why, not just that. An operator who is told "refused" will work
    # around it; one who is told the sequence grows by a copy per run will not.
    assert "16.10" in str(failure.value)


def test_writing_over_a_scheme_is_refused(tmp_path):
    # A scheme is read on the next run exactly as an input is, and section 16 gives no reason to
    # treat it more leniently: a render that overwrites its own scheme is a render whose next
    # run means something else.
    scheme = write(tmp_path / "conf" / "app.scheme", "output=logback.xml")

    with pytest.raises(n2x.Namespace2XmlError):
        render_node.guard_sources(
            [str(tmp_path / "in.yml")], [str(scheme)], str(tmp_path / "conf"), ["app.scheme"])


@pytest.mark.skipif(os.name == "nt", reason="hard links need a POSIX filesystem here")
def test_a_hard_link_to_an_input_is_still_that_input(tmp_path):
    # Two names, one inode. Comparing resolved paths as text says these are different files;
    # the filesystem says they are the same one, and the filesystem is right -- writing through
    # either name feeds the next run's read.
    (tmp_path / "conf").mkdir()
    source = write(tmp_path / "conf" / "input.yml", "a: 1")
    os.link(str(source), str(tmp_path / "conf" / "logback.xml"))

    with pytest.raises(n2x.Namespace2XmlError):
        render_node.guard_sources(
            [str(source)], [], str(tmp_path / "conf"), ["logback.xml"])


def test_a_sibling_file_in_the_same_directory_is_allowed(tmp_path):
    source = write(tmp_path / "conf" / "input.yml", "a: 1")

    render_node.guard_sources([str(source)], [], str(tmp_path / "conf"), ["logback.xml"])


def test_the_refusal_survives_a_different_spelling_of_the_same_file(tmp_path):
    # The trap is a property of the files, not of how the path was written, so a relative src
    # against an absolute dest has to be caught just the same.
    source = write(tmp_path / "logback.xml", "<configuration/>")
    here = os.getcwd()
    os.chdir(str(tmp_path))

    try:
        with pytest.raises(n2x.Namespace2XmlError):
            render_node.guard_sources(["logback.xml"], [], str(tmp_path), ["logback.xml"])
    finally:
        os.chdir(here)


# --- Section 24: determinism is what makes a byte comparison an answer -------------------------

@_confined
def test_a_file_that_is_absent_from_the_destination_counts_as_changed(tmp_path):
    write(tmp_path / "scratch" / "out.xml", "<a/>")
    (tmp_path / "dest").mkdir()

    assert render_node.plan(
        str(tmp_path / "scratch"), str(tmp_path / "dest"), ["out.xml"]) == ["out.xml"]


@_confined
def test_identical_content_is_not_changed(tmp_path):
    write(tmp_path / "scratch" / "out.xml", "<a/>")
    write(tmp_path / "dest" / "out.xml", "<a/>")

    # A shallow comparison would consult mtime, and the scratch copy is always newer because it
    # was written by this very run -- so every file would report changed on every run.
    assert render_node.plan(str(tmp_path / "scratch"), str(tmp_path / "dest"), ["out.xml"]) == []


@_confined
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
        out = [arg for arg in argv if arg.startswith("--output=")][0][len("--output="):]

        for relative, text in files.items():
            target = pathlib.Path(out) / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(text, encoding="utf-8")

        return diagnostics

    return run


@_confined
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


@_confined
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


@_confined
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


@_confined
def test_a_diff_carries_both_sides(tmp_path, tool):
    dest = tmp_path / "dest"
    dest.mkdir()
    write(dest / "out.xml", "old")
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    result = render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), check_mode=True, diff_mode=True,
        runner=producing({"out.xml": "new"}))

    assert result["diff"][0]["before"] == "old"
    assert result["diff"][0]["after"] == "new"


@_confined
def test_no_file_content_is_returned_unless_a_diff_was_asked_for(tmp_path, tool):
    # Rendered configuration routinely carries credentials. Returning it unasked sends it to the
    # controller, into any registered variable, and into every result-recording callback --
    # which is where a secret ends up in a log nobody meant to write it to.
    dest = tmp_path / "dest"
    dest.mkdir()
    write(dest / "out.xml", "old")
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    result = render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), check_mode=True, runner=producing({"out.xml": "s3cret"}))

    assert result["changed"] is True
    assert result["diff"] == []


@_confined
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


@_confined
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


@_confined
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


# --- Section 21.1: publication is confined to the output root ----------------------------------

@_confined
def test_a_symlinked_destination_file_is_not_followed(tmp_path, tool):
    # The attack this closes: an unprivileged user who can write inside 'dest' replaces an
    # output file with a link to /etc/shadow, and the next run under 'become' writes rendered
    # configuration straight through it. Section 21.1 requires no-follow publication precisely
    # so the link is replaced instead of traversed.
    dest = tmp_path / "dest"
    dest.mkdir()
    outside = write(tmp_path / "outside" / "secret", "untouched")
    os.symlink(str(outside), str(dest / "out.xml"))
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    result = render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), runner=producing({"out.xml": "<a/>"}))

    assert result["changed"] is True
    assert outside.read_text(encoding="utf-8") == "untouched"
    assert not (dest / "out.xml").is_symlink()
    assert (dest / "out.xml").read_text(encoding="utf-8") == "<a/>"


@_confined
def test_a_symlinked_directory_component_is_refused(tmp_path, tool):
    # A link one level up is the same attack with one more step, and rename cannot save us
    # there: by the time a handle names the wrong directory the write is already outside the
    # root. So the descent refuses rather than repairs.
    dest = tmp_path / "dest"
    dest.mkdir()
    (tmp_path / "elsewhere").mkdir()
    os.symlink(str(tmp_path / "elsewhere"), str(dest / "conf"))
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    with pytest.raises(n2x.Namespace2XmlError, match="21.1"):
        render_node.render(
            src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
            scratch=str(scratch), runner=producing({"conf/out.xml": "<a/>"}))

    assert not (tmp_path / "elsewhere" / "out.xml").exists()


@_confined
def test_a_diff_does_not_read_through_a_symlink(tmp_path, tool):
    # Reading 'before' through a link would put an arbitrary file's contents into the task
    # result and ship them to the controller -- an exfiltration primitive rather than a write
    # one, and just as much a section 21.1 containment failure.
    dest = tmp_path / "dest"
    dest.mkdir()
    outside = write(tmp_path / "outside" / "secret", "root:x:0:0")
    os.symlink(str(outside), str(dest / "out.xml"))
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    result = render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), check_mode=True, diff_mode=True,
        runner=producing({"out.xml": "<a/>"}))

    assert "root:x:0:0" not in result["diff"][0]["before"]


@_confined
def test_a_nested_output_directory_is_created_under_the_root(tmp_path, tool):
    # Section 16's 'output' may name a path, not just a filename, so the common scheme writes
    # into a subdirectory that does not exist yet on a first run.
    dest = tmp_path / "dest"
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    result = render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), runner=producing({"conf/nested/out.xml": "<a/>"}))

    assert result["changed"] is True
    assert (dest / "conf" / "nested" / "out.xml").read_text(encoding="utf-8") == "<a/>"


def test_an_output_root_that_is_a_file_is_refused(tmp_path, tool):
    # Section 21.1: "An existing non-directory output root is PATH001." Silently replacing the
    # operator's file, or failing with a bare OSError traceback, are both worse answers.
    root = write(tmp_path / "dest", "i am a file")
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    with pytest.raises(n2x.Namespace2XmlError, match="21.1"):
        render_node.render(
            src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(root),
            scratch=str(scratch), runner=producing({"out.xml": "<a/>"}))

    assert root.read_text(encoding="utf-8") == "i am a file"


def test_a_refused_render_does_not_leave_an_output_root_behind(tmp_path, tool):
    # Section 21.1 puts the root in the creation plan and creates it "only after the global
    # validation gate". A refusal that still made the directory would leave litter on every
    # node it failed on, and would make check mode a writing operation.
    dest = tmp_path / "dest"
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    with pytest.raises(n2x.Namespace2XmlError):
        render_node.render(
            src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
            scratch=str(scratch), runner=producing({}))

    assert not dest.exists()


@_confined
def test_check_mode_does_not_create_the_output_root(tmp_path, tool):
    dest = tmp_path / "dest"
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    result = render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), check_mode=True, runner=producing({"out.xml": "<a/>"}))

    assert result["changed"] is True
    assert not dest.exists()


@_confined
def test_an_existing_file_keeps_the_mode_it_had(tmp_path, tool):
    # Publication replaces an inode rather than truncating one, so the mode has to be carried
    # across deliberately. Losing it would silently widen a 0600 secrets file to the umask
    # default on the first render that changed its content.
    dest = tmp_path / "dest"
    dest.mkdir()
    target = write(dest / "out.xml", "old")
    os.chmod(str(target), 0o600)
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), runner=producing({"out.xml": "new"}))

    assert stat.S_IMODE(os.stat(str(target)).st_mode) == 0o600
    assert target.read_text(encoding="utf-8") == "new"


@_confined
def test_no_temporary_file_survives_a_successful_publication(tmp_path, tool):
    dest = tmp_path / "dest"
    dest.mkdir()
    scratch = tmp_path / "scratch"
    scratch.mkdir()

    render_node.render(
        src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest=str(dest),
        scratch=str(scratch), runner=producing({"out.xml": "<a/>"}))

    assert sorted(os.listdir(str(dest))) == ["out.xml"]


@_confined
def test_returned_paths_are_absolute_even_when_dest_was_not(tmp_path, tool):
    # RETURN documents 'files' as absolute paths, and 'type: path' does not make them so -- it
    # expands '~' and environment variables and stops there. A relative path in a result is a
    # path the controller cannot resolve, because it was relative to the node's cwd.
    dest = tmp_path / "dest"
    dest.mkdir()
    scratch = tmp_path / "scratch"
    scratch.mkdir()
    here = os.getcwd()
    os.chdir(str(tmp_path))

    try:
        result = render_node.render(
            src=[str(write(tmp_path / "in.yml", "a: 1"))], schemes=["s"], dest="dest",
            scratch=str(scratch), runner=producing({"out.xml": "<a/>"}))
    finally:
        os.chdir(here)

    assert all(os.path.isabs(path) for path in result["files"])
    assert all(os.path.isabs(path) for path in result["changed_files"])


def _filter_encode_scheme_mapping(mapping):
    """The controller-side filter's own copy of the mapping-scheme converter."""
    try:
        from ansible_collections.stop_cran.namespace2xml.plugins.filter import (  # noqa: E501
            render as filter_render)
    except ImportError:
        import importlib.util

        path = _PLUGINS / "filter" / "render.py"
        spec = importlib.util.spec_from_file_location("n2x_filter_for_drift", path)
        filter_render = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(filter_render)

    return filter_render.encode_scheme_mapping(mapping)


def test_the_two_mapping_scheme_converters_agree():
    """The node copy and the controller copy must not drift.

    A filter cannot import module_utils, so this converter exists twice on purpose. The hazard
    of a deliberate duplicate is that one copy is fixed and the other is not, and the symptom is
    a playbook whose scheme means one thing through the module and another through the filter.
    """
    accepted = [
        {"cfg": {"output": "xml"}},
        {"cfg": {"output": "xml", "root": "configuration", "filename": "a.xml"}},
        {"xmlinputoptions": "NormalizeFormattingWhitespace", "cfg": {"output": "json"}},
        {"cfg": {"appender": {"*": {"name": {"type": "ignore"}}, "a": {"type": "element"}}}},
        {"cfg": {"output": "xml,json"}},
        {"cfg": {"hidden": True, "port": 8080}},
        {"cfg": {"name": "a=b"}},
        {"cfg": {"name": "line\nbreak"}},
        {"cfg": {"name": "\u00e9\u4e2d"}},
        {"cfg": {"a": {"b": {"c": {"d": {"output": "ini"}}}}}},
        {"cfg": {"a\\.b": {"output": "xml"}}},
        {"a\\.b": {"output": "xml"}},
        {"cfg": {"a\\.b\\.c": {"type": "element"}}},
        {"cfg": {"Q{urn:example.com}name": {"type": "element"}}},
        {"cfg": {"@Q{urn:p.q}x": {"type": "attribute"}}},
        {"cfg": {"Q{urn:x\\}y.z}n": {"type": "element"}}},
        {"cfg": {"Q{urn:x": {"type": "element"}}},
        {"cfg": {"OUTPUT": " XML "}},
    ]

    for mapping in accepted:
        assert n2x.encode_scheme_mapping(mapping) == _filter_encode_scheme_mapping(mapping), mapping

    refused = [
        {}, [], "cfg.output=xml", None,
        {"cfg.output": "xml"},
        {"cfg": {"a\\.b.c": {"type": "element"}}},
        {"cfg": {"Q{urn:e.g}a.b": {"type": "element"}}},
        {"cfg": {"\\Q{urn:e.g}n": {"type": "element"}}},
        {"cfg": {".": "xml"}},
        {"cfg": {"output": ["xml", "json"]}},
        {"cfg": {"output": None}},
        {"cfg": {"output": ""}},
        {"cfg": {"filename": 3.10}},
        {"cfg": {}},
        {"": "xml"},
        {3: "xml"},
        {"cfg": {"output": {"nested": []}}},
    ]

    for mapping in refused:
        node_message = None
        filter_message = None

        try:
            n2x.encode_scheme_mapping(mapping)
        except n2x.Namespace2XmlError as error:
            node_message = str(error)

        try:
            _filter_encode_scheme_mapping(mapping)
        except Exception as error:  # the filter's error derives from AnsibleFilterError
            filter_message = str(error)

        assert node_message is not None, mapping
        assert node_message == filter_message, mapping
