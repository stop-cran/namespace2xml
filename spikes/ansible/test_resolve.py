"""Checks for how the filter finds the tool binary.

Separate from ``test_filter.py`` because these need no real tool: every candidate is a stub file
this script creates, so the checks run anywhere Python does, including a Windows workstation with
no Ansible controller.

The case that matters is the last one. ``dotnet tool install --global`` puts the binary in
``~/.dotnet/tools``, and whether that directory is on ``PATH`` is decided by the login shell, not
by the install. An Ansible filter runs inside ``ansible-playbook``, which inherited its environment
before the play began, so a filter that can only find the tool through ``PATH`` fails on a host
where the tool is installed and working -- and fails again, for the same reason, when a play
installs the tool in an earlier task and templates with it in a later one. That is arm A of
``ansible-topology-spike.yml``, reproduced here as a unit check:

    python spikes/ansible/test_resolve.py
"""

import os
import stat
import sys
import tempfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "filter_plugins"))

# .gitignore may not exclude anything under spikes/, so a stray __pycache__ would be committed.
sys.dont_write_bytecode = True

import namespace2xml_filter as n2x  # noqa: E402

FAILURES = []

WINDOWS = os.name == "nt"
BINARY = "namespace2xml.exe" if WINDOWS else "namespace2xml"

# Every variable the resolver is allowed to read. Each check starts from all of them unset, so a
# value leaking in from the developer's own shell cannot make a check pass.
CONSULTED = ("NAMESPACE2XML", "PATH", "HOME", "USERPROFILE", "DOTNET_CLI_HOME", "DOTNET_TOOLS_PATH")


def check(name, actual, expected):
    """Compare two values and record a failure with both spellings."""
    if actual == expected:
        print("  ok    %s" % name)
        return True

    FAILURES.append(name)
    print("  FAIL  %s" % name)
    print("    expected: %r" % expected)
    print("    actual:   %r" % actual)

    return False


def check_path(name, actual, expected):
    """Compare two paths as the platform's filesystem would.

    ``shutil.which`` on Windows appends the ``PATHEXT`` entry as that variable spells it, so a
    correct answer can come back as ``namespace2xml.EXE``. Comparing those byte for byte would
    fail a check about resolution over a difference in case that no caller can observe.
    """
    return check(name, os.path.normcase(actual), os.path.normcase(expected))


def check_raises(name, thunk, needle):
    """Require a call to fail, and its message to name the remedy."""
    try:
        actual = thunk()
    except Exception as error:  # noqa: BLE001 -- the type under test is what is being asserted
        text = str(error)
        if needle in text:
            print("  ok    %s" % name)
            return True

        FAILURES.append(name)
        print("  FAIL  %s" % name)
        print("    message did not mention %r" % needle)
        print("    message: %s" % text)

        return False

    FAILURES.append(name)
    print("  FAIL  %s" % name)
    print("    expected a failure, got: %r" % actual)

    return False


def plant(directory, name=BINARY):
    """Create an executable stub and return its path."""
    os.makedirs(directory, exist_ok=True)
    path = os.path.join(directory, name)

    with open(path, "w", encoding="utf-8") as handle:
        handle.write("" if WINDOWS else "#!/bin/sh\nexit 0\n")

    os.chmod(path, os.stat(path).st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    return path


class Environment(object):
    """Run a check with exactly the variables it declares, and nothing else."""

    def __init__(self, **values):
        self.values = values
        self.saved = {}

    def __enter__(self):
        for name in CONSULTED:
            self.saved[name] = os.environ.get(name)
            os.environ.pop(name, None)

        for name, value in self.values.items():
            os.environ[name] = value

        return self

    def __exit__(self, *_):
        for name, value in self.saved.items():
            if value is None:
                os.environ.pop(name, None)
            else:
                os.environ[name] = value

        return False


class NoHome(object):
    """Neutralise ``os.path.expanduser``, which no environment variable can fully control.

    On Windows ``expanduser('~')`` falls back to the registry when ``HOME`` and ``USERPROFILE``
    are both unset, so a developer with the tool genuinely installed would see the "found nothing"
    check pass for the wrong reason -- it would have found the real binary. Swapping the function
    is the only way to state that the last fallback is out of scope for a check.
    """

    def __init__(self, directory):
        self.directory = directory
        self.saved = None

    def __enter__(self):
        self.saved = os.path.expanduser
        os.path.expanduser = lambda path: self.directory

        return self

    def __exit__(self, *_):
        os.path.expanduser = self.saved

        return False


def explicit_argument(root):
    """An explicit ``tool=`` argument is authoritative."""
    print("explicit argument")

    planted = plant(os.path.join(root, "explicit"))

    with Environment(PATH=""):
        check("an absolute path is used as given", n2x._resolve(planted), planted)

    with Environment(PATH=os.path.join(root, "explicit")):
        check_path("a bare name is resolved on PATH", n2x._resolve(BINARY), planted)

    with Environment(PATH=""):
        check_raises("an argument that resolves to nothing fails",
                     lambda: n2x._resolve(os.path.join(root, "absent", BINARY)),
                     "absent")


def environment_variable(root):
    """``NAMESPACE2XML`` outranks any discovery."""
    print("NAMESPACE2XML")

    chosen = plant(os.path.join(root, "chosen"))
    ignored = os.path.join(root, "ignored")
    plant(ignored)

    with Environment(NAMESPACE2XML=chosen, PATH=ignored):
        check("the variable outranks PATH", n2x._resolve(None), chosen)

    with Environment(NAMESPACE2XML=os.path.join(root, "nowhere", BINARY), PATH=ignored):
        check_raises("a variable pointing nowhere fails by name, not by falling back",
                     lambda: n2x._resolve(None),
                     "NAMESPACE2XML")


def search_path(root):
    """``PATH`` is consulted when nothing more specific was given."""
    print("PATH")

    directory = os.path.join(root, "on-path")
    planted = plant(directory)

    with Environment(PATH=directory):
        check_path("a directory on PATH is searched", n2x._resolve(None), planted)


def dotnet_tools(root):
    """The case arm A measures: installed, working, and not on PATH."""
    print("dotnet global tools")

    home = os.path.join(root, "home")
    tools = os.path.join(home, ".dotnet", "tools")
    planted = plant(tools)

    # PATH is empty, so this is only findable by knowing where dotnet puts global tools.
    home_variable = "USERPROFILE" if WINDOWS else "HOME"

    with Environment(PATH="", **{home_variable: home}):
        check_path("the default global-tools directory is searched", n2x._resolve(None), planted)

    cli_home = os.path.join(root, "cli-home")
    relocated = plant(os.path.join(cli_home, ".dotnet", "tools"))

    with Environment(PATH="", DOTNET_CLI_HOME=cli_home, **{home_variable: home}):
        check_path("DOTNET_CLI_HOME outranks the home directory", n2x._resolve(None), relocated)

    empty = os.path.join(root, "empty")
    os.makedirs(empty, exist_ok=True)

    with Environment(PATH=""), NoHome(empty):
        check_raises("a tool that is nowhere fails with an actionable message",
                     lambda: n2x._resolve(None),
                     "dotnet tool install")


def error_type(root):
    """The error type must derive from Ansible's when Ansible is importable, and not require it.

    ansible-core has no Windows controller, so on this workstation only the fallback branch can
    run for real. Injecting a stand-in ``ansible.errors`` proves the mechanism -- that the base
    class is chosen at import and that the raised error carries it -- while CI on Linux, with
    ansible-core actually installed, is what proves the real class is picked up.
    """
    print("error type")

    import importlib
    import types

    check("without Ansible the error is a plain exception",
          issubclass(n2x.Namespace2XmlError, Exception), True)

    class Stand_In(Exception):
        pass

    ansible = types.ModuleType("ansible")
    errors = types.ModuleType("ansible.errors")
    errors.AnsibleFilterError = Stand_In
    ansible.errors = errors

    saved = {name: sys.modules.get(name) for name in ("ansible", "ansible.errors")}
    sys.modules["ansible"] = ansible
    sys.modules["ansible.errors"] = errors

    try:
        reloaded = importlib.reload(n2x)

        check("with Ansible importable the error derives from AnsibleFilterError",
              issubclass(reloaded.Namespace2XmlError, Stand_In), True)

        empty = os.path.join(root, "empty")
        os.makedirs(empty, exist_ok=True)
        raised = []

        with Environment(PATH=""), NoHome(empty):
            try:
                reloaded._resolve(None)
            except Stand_In as error:
                raised.append(error)

        check("a real failure is catchable as an Ansible filter error", len(raised), 1)
    finally:
        for name, module in saved.items():
            if module is None:
                sys.modules.pop(name, None)
            else:
                sys.modules[name] = module

        importlib.reload(n2x)


def main():
    print("platform: %s, binary: %s\n" % (sys.platform, BINARY))

    with tempfile.TemporaryDirectory(prefix="n2x-resolve-") as root:
        explicit_argument(root)
        print("")
        environment_variable(root)
        print("")
        search_path(root)
        print("")
        dotnet_tools(root)
        print("")
        error_type(root)

    print("")

    if FAILURES:
        print("%d check(s) failed" % len(FAILURES))
        return 1

    print("all checks passed")

    return 0


if __name__ == "__main__":
    sys.exit(main())
