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
