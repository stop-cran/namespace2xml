"""How the filter finds the tool binary.

Every candidate here is a stub this test plants, so nothing needs a real installation and the
whole file runs anywhere Python does.

The case that matters is ``test_the_dotnet_global_tools_directory_is_searched``. ``dotnet tool
install --global`` writes to ``~/.dotnet/tools`` whether or not the login shell names that
directory on ``PATH``, and a filter runs inside ``ansible-playbook``, whose environment was
fixed before the play began. A filter that can only find the tool through ``PATH`` therefore
fails on a host where the tool is installed and working, and fails again when a play installs
the tool in one task and templates with it in a later one. Both were measured against real
Ansible before this code existed; see ``.github/workflows/ansible-topology-spike.yml``.
"""

from __future__ import annotations

import os
import pathlib
import stat
import sys

import pytest

try:
    from ansible_collections.stop_cran.namespace2xml.plugins.filter import render as n2x
except ImportError:  # a checkout that is not inside an ansible_collections tree
    # Through `plugins` -- an implicit namespace package -- rather than by path: the filter
    # reaches module_utils by relative import, and a relative import needs a package. Loading
    # both halves through one package root is also what makes the shared module one object.
    _ANSIBLE = pathlib.Path(__file__).resolve().parents[4]

    if str(_ANSIBLE) not in sys.path:
        sys.path.insert(0, str(_ANSIBLE))

    from plugins.filter import render as n2x  # type: ignore[no-redef]


WINDOWS = os.name == "nt"
BINARY = "namespace2xml.exe" if WINDOWS else "namespace2xml"
HOME_VARIABLE = "USERPROFILE" if WINDOWS else "HOME"

# Every variable the resolver is allowed to read. Each test starts from all of them unset, so a
# value leaking in from the developer's own shell cannot make a test pass.
CONSULTED = (
    "NAMESPACE2XML",
    "PATH",
    "HOME",
    "USERPROFILE",
    "DOTNET_CLI_HOME",
    "DOTNET_TOOLS_PATH",
)


def plant(directory, name=BINARY):
    """Create an executable stub and return its path."""
    os.makedirs(directory, exist_ok=True)
    path = os.path.join(directory, name)

    with open(path, "w", encoding="utf-8") as handle:
        handle.write("" if WINDOWS else "#!/bin/sh\nexit 0\n")

    os.chmod(path, os.stat(path).st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    return path


def same(actual, expected):
    """Compare two paths as the platform's filesystem would.

    ``shutil.which`` on Windows appends the ``PATHEXT`` entry as that variable spells it, so a
    correct answer can come back as ``namespace2xml.EXE``. Comparing byte for byte would fail a
    test about resolution over a difference in case no caller can observe.
    """
    return os.path.normcase(actual) == os.path.normcase(expected)


@pytest.fixture(name="sandbox")
def _sandbox(monkeypatch, tmp_path):
    """Unset everything the resolver reads, including the one thing ``os.environ`` cannot."""
    for name in CONSULTED:
        monkeypatch.delenv(name, raising=False)

    # On Windows ``expanduser('~')`` falls back to the registry when HOME and USERPROFILE are
    # both unset, so a developer with the tool genuinely installed would see a "found nothing"
    # test pass for the wrong reason -- it would have found the real binary. Substituting the
    # function is the only way to hold the last fallback out of scope.
    nowhere = tmp_path / "no-home"
    nowhere.mkdir()
    monkeypatch.setattr(os.path, "expanduser", lambda path: str(nowhere))

    return tmp_path


@pytest.fixture(autouse=True)
def _clean_resolve_cache():
    """The resolver's memo is a module global, so a test that fills it would leak into the next.

    Several tests below set ``PATH`` to the empty string, which makes their cache keys identical.
    They would in practice survive a leak, because each plants into its own ``tmp_path`` and a
    stale entry therefore fails revalidation -- but passing for that reason is an accident, and
    an accident that would stop holding the moment a test reused a directory.
    """
    n2x._RESOLVE_CACHE.clear()
    yield
    n2x._RESOLVE_CACHE.clear()


def test_an_explicit_absolute_path_is_used_as_given(sandbox, monkeypatch):
    planted = plant(str(sandbox / "explicit"))
    monkeypatch.setenv("PATH", "")

    assert n2x._resolve(planted) == planted


def test_an_explicit_bare_name_is_resolved_on_path(sandbox, monkeypatch):
    planted = plant(str(sandbox / "explicit"))
    monkeypatch.setenv("PATH", str(sandbox / "explicit"))

    assert same(n2x._resolve(BINARY), planted)


def test_an_explicit_argument_that_resolves_to_nothing_fails(sandbox, monkeypatch):
    monkeypatch.setenv("PATH", "")

    with pytest.raises(n2x.Namespace2XmlError, match="absent"):
        n2x._resolve(str(sandbox / "absent" / BINARY))


def test_the_environment_variable_outranks_path(sandbox, monkeypatch):
    chosen = plant(str(sandbox / "chosen"))
    ignored = str(sandbox / "ignored")
    plant(ignored)
    monkeypatch.setenv("NAMESPACE2XML", chosen)
    monkeypatch.setenv("PATH", ignored)

    assert n2x._resolve(None) == chosen


def test_an_environment_variable_pointing_nowhere_fails_by_name(sandbox, monkeypatch):
    # It does not fall back. A caller-supplied value that silently loses to a lucky PATH hit
    # turns a typo into a different binary running than the one that was named.
    ignored = str(sandbox / "ignored")
    plant(ignored)
    monkeypatch.setenv("NAMESPACE2XML", str(sandbox / "nowhere" / BINARY))
    monkeypatch.setenv("PATH", ignored)

    with pytest.raises(n2x.Namespace2XmlError, match="NAMESPACE2XML"):
        n2x._resolve(None)


def test_a_directory_on_path_is_searched(sandbox, monkeypatch):
    directory = str(sandbox / "on-path")
    planted = plant(directory)
    monkeypatch.setenv("PATH", directory)

    assert same(n2x._resolve(None), planted)


def test_the_dotnet_global_tools_directory_is_searched(sandbox, monkeypatch):
    home = str(sandbox / "home")
    planted = plant(os.path.join(home, ".dotnet", "tools"))
    monkeypatch.setenv("PATH", "")
    monkeypatch.setenv(HOME_VARIABLE, home)

    assert same(n2x._resolve(None), planted)


def test_dotnet_cli_home_outranks_the_home_directory(sandbox, monkeypatch):
    home = str(sandbox / "home")
    plant(os.path.join(home, ".dotnet", "tools"))
    cli_home = str(sandbox / "cli-home")
    relocated = plant(os.path.join(cli_home, ".dotnet", "tools"))
    monkeypatch.setenv("PATH", "")
    monkeypatch.setenv(HOME_VARIABLE, home)
    monkeypatch.setenv("DOTNET_CLI_HOME", cli_home)

    assert same(n2x._resolve(None), relocated)


def test_dotnet_tools_path_outranks_everything_discovered(sandbox, monkeypatch):
    home = str(sandbox / "home")
    plant(os.path.join(home, ".dotnet", "tools"))
    explicit = str(sandbox / "explicit-tools")
    planted = plant(explicit)
    monkeypatch.setenv("PATH", "")
    monkeypatch.setenv(HOME_VARIABLE, home)
    monkeypatch.setenv("DOTNET_TOOLS_PATH", explicit)

    assert same(n2x._resolve(None), planted)


def test_a_tool_that_is_nowhere_fails_with_an_actionable_message(sandbox, monkeypatch):
    monkeypatch.setenv("PATH", "")

    with pytest.raises(n2x.Namespace2XmlError, match="dotnet tool install"):
        n2x._resolve(None)


def test_the_install_advice_asks_for_a_prerelease(sandbox, monkeypatch):
    # The install command this message hands out has to be the one that produces a tool the
    # filter will accept. Plain 'dotnet tool install --global namespace2xml' resolves the highest
    # stable version, which is 2.4.0, which _identify() then rejects for having no
    # contract-bundle -- so the advice would cost the reader a second round trip to learn
    # something this message already knew. Pinned because the flag stops being necessary the day
    # 3.0 goes stable, and that is a deliberate edit rather than a silent drift.
    monkeypatch.setenv("PATH", "")

    with pytest.raises(n2x.Namespace2XmlError) as caught:
        n2x._resolve(None)

    # Asserted against the command and not merely against the word '--prerelease' appearing
    # somewhere: the message also explains why the flag is needed, so a substring check for the
    # flag alone passes even when the command handed out is the wrong one. This assertion has to
    # fail when the copyable part is wrong, which is the only part a reader runs.
    assert "dotnet tool install --global --prerelease namespace2xml" in str(caught.value)


def test_every_success_path_returns_an_absolute_path(sandbox, monkeypatch):
    # The identity cache is keyed on the resolved path, so two spellings of one binary must not
    # become two entries and two --version spawns.
    directory = str(sandbox / "on-path")
    plant(directory)
    monkeypatch.setenv("PATH", directory)
    monkeypatch.chdir(sandbox)

    assert os.path.isabs(n2x._resolve(None))


def test_a_second_resolution_does_not_search_again(sandbox, monkeypatch):
    """Searching is the expensive part, and ``render`` asks for it twice per call.

    Once through ``tool_identity``, which resolves before it consults its own cache, and once
    for the run itself. ``shutil.which`` walks every ``PATH`` entry against every ``PATHEXT``,
    measured at 7.3 ms against 0.15 ms for a path that needs no searching -- so an uncached
    resolver spent about 14.6 ms per render, roughly four times what all the temporary-file
    marshalling costs, locating a file that had not moved (#112).
    """
    directory = str(sandbox / "on-path")
    planted = plant(directory)
    monkeypatch.setenv("PATH", directory)

    searched = []
    uncached = n2x._search_for_tool

    def counting(tool):
        searched.append(tool)
        return uncached(tool)

    monkeypatch.setattr(n2x, "_search_for_tool", counting)

    assert same(n2x._resolve(None), planted)
    assert same(n2x._resolve(None), planted)
    assert len(searched) == 1


def test_a_failure_is_not_remembered(sandbox, monkeypatch):
    """A play may install the tool in one task and template with it in a later one.

    ``_tool_directories`` exists because ``ansible-playbook``'s environment was fixed before the
    play began, so a lookup that finds nothing now is expected to succeed later. Remembering the
    miss would break the very case that function was written to serve.
    """
    directory = str(sandbox / "arrives-late")
    os.makedirs(directory, exist_ok=True)
    monkeypatch.setenv("PATH", directory)

    with pytest.raises(n2x.Namespace2XmlError, match="dotnet tool install"):
        n2x._resolve(None)

    planted = plant(directory)

    assert same(n2x._resolve(None), planted)


def test_a_remembered_path_that_stops_being_runnable_is_searched_for_again(sandbox, monkeypatch):
    """A memo that outlives its answer is worse than no memo, because it is confidently wrong."""
    first = str(sandbox / "first")
    planted = plant(first)
    second = str(sandbox / "second")
    os.makedirs(second, exist_ok=True)
    monkeypatch.setenv("PATH", os.pathsep.join((first, second)))

    assert same(n2x._resolve(None), planted)

    os.remove(planted)
    relocated = plant(second)

    assert same(n2x._resolve(None), relocated)


def test_a_changed_path_is_answered_afresh(sandbox, monkeypatch):
    """``PATH`` is part of the key because it is part of the question."""
    first = str(sandbox / "first")
    planted = plant(first)
    second = str(sandbox / "second")
    relocated = plant(second)

    monkeypatch.setenv("PATH", first)

    assert same(n2x._resolve(None), planted)

    monkeypatch.setenv("PATH", second)

    assert same(n2x._resolve(None), relocated)


def test_the_error_derives_from_ansibles_filter_error():
    """A failed template must be reported as a filter error, not as an unknown exception.

    Asserted directly rather than by injecting a stand-in ``ansible.errors``: this test runs
    under ``ansible-test units``, where ansible-core is by definition importable, so the real
    class relationship is what gets checked.
    """
    from ansible.errors import AnsibleFilterError

    assert issubclass(n2x.Namespace2XmlError, AnsibleFilterError)
