"""What the filter does with what the tool says.

A non-zero exit already carried its own explanation into the raised error. A zero exit with
warnings did not: the tool reports section 15.2 diagnostics on stderr for things it can see but
must not decide -- a scheme rule matching no path, an output instance selecting nothing -- and
those exist precisely so a mistyped rule is not silent. Capturing stderr and discarding it on
success restores the silence, and the author gets a plausible document in which the rule they
wrote did nothing.
"""

from __future__ import annotations

import pathlib
import sys

import pytest

# `filt` is the filter under test; `n2x` is the module_utils it calls into, named for the
# file it is. Discovery, the contract-bundle gate and the section 8.3 encoder live in `n2x`,
# so a test that stands in for one of those patches it there -- patching the filter would
# leave the code under test calling the real thing.
try:
    from ansible_collections.stop_cran.namespace2xml.plugins.filter import render as filt
    from ansible_collections.stop_cran.namespace2xml.plugins.module_utils import n2x
except ImportError:  # a checkout that is not inside an ansible_collections tree
    # Through `plugins` -- an implicit namespace package -- rather than by path: the filter
    # reaches module_utils by relative import, and a relative import needs a package. Loading
    # both halves through one package root is also what makes the shared module one object.
    _ANSIBLE = pathlib.Path(__file__).resolve().parents[4]

    if str(_ANSIBLE) not in sys.path:
        sys.path.insert(0, str(_ANSIBLE))

    from plugins.filter import render as filt  # type: ignore[no-redef]
    from plugins.module_utils import n2x  # type: ignore[no-redef]


WARNING = ("scheme.txt(2): warning WARN009 \u00a715.2: 'cfg.*.version.type' matches no path in "
           "any output instance, so it has no effect.")


class _Recorder:
    def __init__(self):
        self.messages = []

    def warning(self, message):
        self.messages.append(message)


def test_a_tool_warning_is_reported_through_ansibles_display(monkeypatch):
    recorder = _Recorder()
    monkeypatch.setattr(filt, "_DISPLAY", recorder)

    filt._warn(WARNING + "\n")

    assert recorder.messages == [WARNING]


def test_blank_lines_do_not_become_empty_warnings(monkeypatch):
    recorder = _Recorder()
    monkeypatch.setattr(filt, "_DISPLAY", recorder)

    filt._warn("\n  \nfirst\n\nsecond\n")

    assert recorder.messages == ["first", "second"]


def test_without_a_controller_warnings_fall_back_to_stderr(monkeypatch, capsys):
    monkeypatch.setattr(filt, "_DISPLAY", None)

    filt._warn(WARNING)

    assert capsys.readouterr().err == "namespace2xml: %s\n" % WARNING


def test_a_real_controller_supplies_a_display():
    """Where the plugin actually runs, the warning path must be Display, not the fallback.

    ``ansible.utils.display`` cannot import on Windows, which is not a supported controller
    platform, so the fallback is what a workstation exercises. This asserts the other branch
    wherever the import succeeds, which is every controller and all of CI.
    """
    pytest.importorskip("ansible.utils.display")

    assert filt._DISPLAY is not None


def test_a_successful_run_forwards_the_tools_diagnostics(monkeypatch, tmp_path):
    seen = []
    monkeypatch.setattr(filt, "_warn", seen.append)

    class _Completed:
        returncode = 0
        stdout = ""
        stderr = WARNING

    monkeypatch.setattr(n2x.subprocess, "run", lambda *args, **kwargs: _Completed())
    output = tmp_path / "out"
    output.mkdir()
    (output / "cfg.json").write_text("{}", encoding="utf-8")

    text = filt._run_and_read("stand-in", "input.txt", "scheme.txt", str(output))

    assert text == "{}"
    assert seen == [WARNING]


def test_a_failed_run_still_raises_with_the_diagnostic_attached(monkeypatch, tmp_path):
    class _Completed:
        returncode = 1
        stdout = ""
        stderr = "error XML002 \u00a711.2: '\\@id' is not usable as an XML element name"

    monkeypatch.setattr(n2x.subprocess, "run", lambda *args, **kwargs: _Completed())
    output = tmp_path / "out"
    output.mkdir()

    with pytest.raises(n2x.Namespace2XmlError, match="XML002"):
        filt._run_and_read("stand-in", "input.txt", "scheme.txt", str(output))


def test_a_refusal_from_the_shared_code_reaches_a_play_as_a_filter_error(monkeypatch):
    """The shared refusals have to be restated as this filter's error before a play sees them.

    ``module_utils`` ships to the node and so cannot import ansible; its ``Namespace2XmlError``
    is a plain exception. ``render`` is the collection's only filter, and therefore its only
    boundary: a shared refusal escaping it unconverted would reach a play as an unrecognised
    exception type -- a traceback where the operator should have been handed a message. This
    pins the conversion on the refusal an operator meets first, a tool that is not installed.
    """
    from ansible.errors import AnsibleFilterError

    def refuse(tool=None):
        raise n2x.Namespace2XmlError("no binary here")

    monkeypatch.setattr(n2x, "tool_identity", refuse)

    with pytest.raises(AnsibleFilterError, match="no binary here"):
        filt.render({"a": "1"}, "xml")


def test_a_run_producing_no_single_output_is_a_failure(monkeypatch, tmp_path):
    class _Completed:
        returncode = 0
        stdout = ""
        stderr = ""

    monkeypatch.setattr(n2x.subprocess, "run", lambda *args, **kwargs: _Completed())
    output = tmp_path / "out"
    output.mkdir()

    with pytest.raises(filt.Namespace2XmlError, match="exactly one output file"):
        filt._run_and_read("stand-in", "input.txt", "scheme.txt", str(output))


# --- Section 15.2: is the document being returned the format that was asked for? ----------------
#
# The scheme's own precedence decides the format, and a set of the formats it mentions cannot
# express precedence. So the question is put to the tool: render again with the caller's format
# appended as a final declaration -- which section 15.2 makes authoritative -- and compare. Same
# result, same format. Different result, the caller was about to be handed something else. See
# issue #111.

def _stand_in_run(name, payload, probe_name=None, probe_payload=None, probe_fails=False):
    """A ``run_tool`` that writes a named file into whichever ``-o`` directory it is handed.

    The second call is the probe, so it can be given a different answer -- or a failure -- to
    stand for a scheme whose precedence lands somewhere other than the caller asked.
    """
    calls = []

    def run_tool(executable, argv):
        calls.append(list(argv))
        target = pathlib.Path(argv[argv.index("-o") + 1])

        if len(calls) > 1:
            if probe_fails:
                raise n2x.Namespace2XmlError("error TYPE001 \u00a719.5: the probe's own problem")

            target.joinpath(probe_name or name).write_text(
                probe_payload if probe_payload is not None else payload, encoding="utf-8")
            return ""

        target.joinpath(name).write_text(payload, encoding="utf-8")
        return ""

    return run_tool, calls


def _confirm(monkeypatch, tmp_path, run_tool):
    """Run the caller's render and then the probe, the way ``_marshal_and_run`` does."""
    monkeypatch.setattr(n2x, "run_tool", run_tool)
    monkeypatch.setattr(n2x, "support_hint", lambda executable: "")
    output = tmp_path / "out"
    output.mkdir()

    filt._run_and_read("stand-in", "input.txt", "scheme.txt", str(output))
    filt._confirm_the_format_asked_for("stand-in", "input.txt", "scheme.txt", str(output),
                                       str(tmp_path), "cfg.output=xml\n", "xml")


def test_a_render_that_already_produces_the_format_asked_for_is_left_alone(monkeypatch,
                                                                           tmp_path):
    """Appending a declaration that agrees changes nothing, so the results match and nothing is
    said. This is the whole reason the probe is sound: a redundant declaration is a no-op."""
    run_tool, calls = _stand_in_run("cfg.xml", "<cfg/>")

    _confirm(monkeypatch, tmp_path, run_tool)

    assert len(calls) == 2
    assert calls[1].count("-s") == 2, "the probe scheme must be passed after the caller's own"


def test_a_render_whose_precedence_answers_another_format_is_refused(monkeypatch, tmp_path):
    """The defect in #111. The scheme mentions XML, so the old membership check accepted the
    call, and section 15.2 then let a later declaration write JSON instead. Exit code zero, a
    well-formed document, and the wrong format in the caller's hands."""
    run_tool, dummy = _stand_in_run("cfg.json", "{}", probe_name="cfg.xml",
                                    probe_payload="<cfg/>")

    with pytest.raises(filt.Namespace2XmlError, match="does not produce 'xml'"):
        _confirm(monkeypatch, tmp_path, run_tool)


def test_a_format_difference_hiding_behind_one_file_name_is_still_caught(monkeypatch, tmp_path):
    """A scheme may declare 'filename: app.conf' and keep that name whichever format it renders,
    so comparing names -- or extensions -- would see two identical runs. Only the bytes differ,
    and the bytes are what is compared."""
    run_tool, dummy = _stand_in_run("app.conf", "a = 1\n", probe_name="app.conf",
                                    probe_payload="<cfg a=\"1\"/>")

    with pytest.raises(filt.Namespace2XmlError, match="does not produce 'xml'"):
        _confirm(monkeypatch, tmp_path, run_tool)


def test_a_probe_that_fails_is_a_disagreement_and_its_diagnostic_is_not_relayed(monkeypatch,
                                                                                tmp_path):
    """Asking for the format outright and having that fail is evidence about the format: a
    declaration that agreed could not have broken a render that already agreed with it. But the
    failure belongs to a render nobody wrote, so its own diagnostic must not be quoted back --
    that would send the reader off to fix a scheme they never saw."""
    run_tool, dummy = _stand_in_run("cfg.json", "{}", probe_fails=True)

    with pytest.raises(filt.Namespace2XmlError, match="does not produce 'xml'") as raised:
        _confirm(monkeypatch, tmp_path, run_tool)

    assert "TYPE001" not in str(raised.value)
