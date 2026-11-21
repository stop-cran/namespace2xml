"""Consumer behavior against the closed publication-report v1 contract."""

import hashlib
import json
import os
import pathlib
import sys

import pytest

_ANSIBLE = pathlib.Path(__file__).resolve().parents[4]
if str(_ANSIBLE) not in sys.path:
    sys.path.insert(0, str(_ANSIBLE))

from plugins.filter import render as filt
from plugins.module_utils import n2x, render_node


SPEC = "a" * 64
REGISTRY = "b" * 64
CONTRACT_HASH = hashlib.sha256((SPEC + "\n" + REGISTRY).encode()).hexdigest()
REVISION = "r119+" + CONTRACT_HASH[:12]
IDENTITY = "3.1.0|" + REVISION
PAYLOAD = b'{"a":1}\n'


def report(destination="nested/app.conf", fmt="json"):
    return {
        "schemaVersion": 1,
        "productVersion": "3.1.0",
        "contract": {"revision": REVISION, "hash": CONTRACT_HASH,
                     "specificationSha256": SPEC, "registrySha256": REGISTRY},
        "exitCode": 0, "outcome": "success", "planComplete": True,
        "publicationOccurred": True, "sideEffectsPossible": True,
        "outputs": [{
            "destination": destination, "format": fmt,
            "contributions": [{"source": "scheme.txt", "sourceIndex": 1, "line": 1,
                               "declaration": "cfg.output", "formatIndex": 0,
                               "matchIndex": 0, "selector": "cfg", "root": "",
                               "format": fmt}],
            "byteLength": len(PAYLOAD), "sha256": hashlib.sha256(PAYLOAD).hexdigest(),
            "existedBefore": False, "bytesDiffered": True, "status": "published",
            "failureCode": None, "unattemptedReason": None,
        }],
    }


def make_runner(payload=None, mutate=None, fail=False):
    calls = []

    def run(executable, argv):
        calls.append(list(argv))
        if fail:
            raise n2x.Namespace2XmlError("REPORT001 report write failed")
        report_path = next(arg.split("=", 1)[1] for arg in argv
                           if arg.startswith("--report-file="))
        if payload is None:
            output_dir = next(arg.split("=", 1)[1] for arg in argv
                              if arg.startswith("--output=")) if any(
                                  arg.startswith("--output=") for arg in argv) else argv[
                                      argv.index("-o") + 1]
            output = pathlib.Path(output_dir) / "nested" / "app.conf"
            output.parent.mkdir()
            output.write_bytes(PAYLOAD)
            data = report()
            if mutate:
                mutate(data)
            pathlib.Path(report_path).write_bytes(
                (json.dumps(data, ensure_ascii=False, separators=(",", ":")) + "\n")
                .encode("utf-8"))
        else:
            pathlib.Path(report_path).write_text(payload, encoding="utf-8")
        return "warning from render\n"

    return run, calls


def test_capability_is_help_not_version(monkeypatch, tmp_path):
    binary = tmp_path / "tool"
    binary.write_text("tool", encoding="utf-8")
    responses = iter(["Usage: namespace2xml\n", "  --report-file <path>\n"])

    class Completed:
        returncode = 0
        stderr = ""

    def run(*args, **kwargs):
        result = Completed()
        result.stdout = next(responses)
        return result

    monkeypatch.setattr(n2x.subprocess, "run", run)
    n2x._REPORT_CAPABILITY_CACHE.clear()
    assert n2x.supports_report(str(binary)) is False
    n2x._REPORT_CAPABILITY_CACHE.clear()
    assert n2x.supports_report(str(binary)) is True
    n2x._REPORT_CAPABILITY_CACHE.clear()


def test_help_failure_is_not_assumed_to_be_legacy(monkeypatch):
    class Completed:
        returncode = 1
        stdout = ""
        stderr = "broken help"

    monkeypatch.setattr(n2x.subprocess, "run", lambda *args, **kwargs: Completed())
    with pytest.raises(n2x.Namespace2XmlError, match="broken help"):
        n2x.supports_report("missing-stand-in")


def test_filter_uses_one_authoritative_render_and_effective_format(monkeypatch, tmp_path):
    runner, calls = make_runner()
    monkeypatch.setattr(n2x, "supports_report", lambda executable: True)
    monkeypatch.setattr(n2x, "tool_identity", lambda tool=None: IDENTITY)
    monkeypatch.setattr(n2x, "run_tool", runner)
    warnings = []
    monkeypatch.setattr(filt, "_warn", warnings.append)
    result = filt._marshal_and_run(
        [filt.Entry(path=None, text="cfg.a=1\n", name="input.txt")],
        [filt.Entry(path=None, text="cfg.output=json\n", name="scheme.txt")],
        "tool", str(tmp_path), "cfg.output=json\n", "json")
    assert result == PAYLOAD.decode()
    assert len(calls) == 1
    assert warnings == ["warning from render\n"]


def test_filter_rejects_mismatched_effective_format(monkeypatch, tmp_path):
    runner, calls = make_runner()
    monkeypatch.setattr(n2x, "supports_report", lambda executable: True)
    monkeypatch.setattr(n2x, "tool_identity", lambda tool=None: IDENTITY)
    monkeypatch.setattr(n2x, "run_tool", runner)
    with pytest.raises(filt.Namespace2XmlError, match="effective output format is 'json'"):
        filt._marshal_and_run(
            [filt.Entry(path=None, text="cfg.a=1\n", name="input.txt")],
            [filt.Entry(path=None, text="cfg.output=xml\n", name="scheme.txt")],
            "tool", str(tmp_path), "cfg.output=xml\n", "xml")
    assert len(calls) == 1


@pytest.mark.parametrize("mutate", [
    lambda r: r.update(schemaVersion=2),
    lambda r: r.update(schemaVersion=True),
    lambda r: r.update(extra=True),
    lambda r: r.update(exitCode=1),
    lambda r: r.update(exitCode=False),
    lambda r: r.update(outcome="warningPolicyRefused"),
    lambda r: r["contract"].update(hash="0" * 64),
    lambda r: r["outputs"][0].update(destination="../escape"),
    lambda r: r["outputs"][0].update(sha256="0" * 64),
    lambda r: r["outputs"][0].update(status="failed"),
    lambda r: r["outputs"][0].update(byteLength=True),
    lambda r: r["outputs"][0].update(existedBefore=0),
    lambda r: r["outputs"][0].update(bytesDiffered=1),
    lambda r: r["outputs"][0]["contributions"][0].update(line=True),
    lambda r: r["outputs"][0].update(unexpected="field"),
])
def test_invalid_reports_are_refused_without_legacy_retry(monkeypatch, tmp_path, mutate):
    runner, calls = make_runner(mutate=mutate)
    monkeypatch.setattr(n2x, "supports_report", lambda executable: True)
    monkeypatch.setattr(n2x, "tool_identity", lambda tool=None: IDENTITY)
    monkeypatch.setattr(n2x, "run_tool", runner)
    with pytest.raises(n2x.Namespace2XmlError, match="publication report"):
        filt._marshal_and_run(
            [filt.Entry(path=None, text="cfg.a=1\n", name="input.txt")],
            [filt.Entry(path=None, text="cfg.output=json\n", name="scheme.txt")],
            "tool", str(tmp_path), "cfg.output=json\n", "json")
    assert len(calls) == 1


@pytest.mark.parametrize("payload", ["", '{"schemaVersion":1,"schemaVersion":1}', "{"])
def test_partial_or_duplicate_report_is_refused(monkeypatch, tmp_path, payload):
    runner, calls = make_runner(payload=payload)
    monkeypatch.setattr(n2x, "supports_report", lambda executable: True)
    monkeypatch.setattr(n2x, "run_tool", runner)
    monkeypatch.setattr(n2x, "tool_identity", lambda tool=None: IDENTITY)
    with pytest.raises(n2x.Namespace2XmlError, match="publication report"):
        filt._marshal_and_run(
            [filt.Entry(path=None, text="x=1\n", name="input.txt")],
            [filt.Entry(path=None, text="x.output=json\n", name="scheme.txt")],
            "tool", str(tmp_path), "x.output=json\n", "json")
    assert len(calls) == 1


def test_report_capable_failure_never_falls_back(monkeypatch, tmp_path):
    runner, calls = make_runner(fail=True)
    monkeypatch.setattr(n2x, "supports_report", lambda executable: True)
    monkeypatch.setattr(n2x, "run_tool", runner)
    with pytest.raises(n2x.Namespace2XmlError, match="REPORT001"):
        filt._marshal_and_run(
            [filt.Entry(path=None, text="x=1\n", name="input.txt")],
            [filt.Entry(path=None, text="x.output=json\n", name="scheme.txt")],
            "tool", str(tmp_path), "x.output=json\n", "json")
    assert len(calls) == 1


def test_missing_report_is_refused(tmp_path):
    with pytest.raises(n2x.Namespace2XmlError, match="publication report"):
        n2x.read_success_report(str(tmp_path / "missing.json"), str(tmp_path), IDENTITY)


def test_successful_empty_report_is_not_a_failure(tmp_path):
    data = report()
    data.update(publicationOccurred=False, sideEffectsPossible=False, outputs=[])
    path = tmp_path / "report.json"
    path.write_bytes((json.dumps(data, separators=(",", ":")) + "\n").encode())
    assert n2x.read_success_report(str(path), str(tmp_path), IDENTITY) == []


@pytest.mark.skipif(not render_node._CONFINEMENT, reason="POSIX confinement required")
def test_module_compares_real_destination_and_never_discovers_scratch(monkeypatch, tmp_path):
    monkeypatch.setattr(render_node, "resolve", lambda tool=None: "tool")
    monkeypatch.setattr(render_node, "tool_identity", lambda tool=None: IDENTITY)
    monkeypatch.setattr(render_node, "supports_report", lambda executable: True)
    monkeypatch.setattr(render_node, "discover", lambda root: pytest.fail("unexpected scan"))
    dest = tmp_path / "dest"
    (dest / "nested").mkdir(parents=True)
    target = dest / "nested" / "app.conf"
    target.write_bytes(b"old\n")
    scratch = tmp_path / "scratch"
    scratch.mkdir()
    runner, calls = make_runner()
    result = render_node.render(["input"], ["scheme"], str(dest), str(scratch),
                                check_mode=True, diff_mode=True, runner=runner)
    assert result["changed"] is True
    assert result["changed_files"] == [str(target)]
    assert result["diff"][0]["before"] == "old\n"
    assert result["diff"][0]["after"] == PAYLOAD.decode()
    assert target.read_bytes() == b"old\n"
    assert len(calls) == 1
    assert not any(name.startswith("n2x-report-") for name in os.listdir(tmp_path))


@pytest.mark.skipif(not render_node._CONFINEMENT, reason="POSIX confinement required")
def test_module_report_path_publishes_once_and_converges(monkeypatch, tmp_path):
    monkeypatch.setattr(render_node, "resolve", lambda tool=None: "tool")
    monkeypatch.setattr(render_node, "tool_identity", lambda tool=None: IDENTITY)
    monkeypatch.setattr(render_node, "supports_report", lambda executable: True)
    dest = tmp_path / "dest"
    runner, calls = make_runner()
    first_scratch = tmp_path / "first"
    first_scratch.mkdir()
    first = render_node.render(["input"], ["scheme"], str(dest), str(first_scratch),
                               runner=runner)
    target = dest / "nested" / "app.conf"
    assert first["changed"] is True
    assert target.read_bytes() == PAYLOAD
    before = target.stat().st_mtime_ns
    second_scratch = tmp_path / "second"
    second_scratch.mkdir()
    second = render_node.render(["input"], ["scheme"], str(dest), str(second_scratch),
                                runner=runner)
    assert second["changed"] is False
    assert target.stat().st_mtime_ns == before
    assert len(calls) == 2


@pytest.mark.skipif(not render_node._CONFINEMENT, reason="POSIX confinement required")
def test_module_report_path_cannot_overwrite_its_input(monkeypatch, tmp_path):
    monkeypatch.setattr(render_node, "resolve", lambda tool=None: "tool")
    monkeypatch.setattr(render_node, "tool_identity", lambda tool=None: IDENTITY)
    monkeypatch.setattr(render_node, "supports_report", lambda executable: True)
    dest = tmp_path / "dest"
    source = dest / "nested" / "app.conf"
    source.parent.mkdir(parents=True)
    source.write_bytes(b"input\n")
    scratch = tmp_path / "scratch"
    scratch.mkdir()
    runner, calls = make_runner()
    with pytest.raises(n2x.Namespace2XmlError, match="16.10"):
        render_node.render([str(source)], ["scheme"], str(dest), str(scratch), runner=runner)
    assert source.read_bytes() == b"input\n"
    assert len(calls) == 1
