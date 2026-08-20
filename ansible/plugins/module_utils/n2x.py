# Copyright (c) 2026 stop-cran
# Apache License 2.0 (see LICENSE)

"""Finding the namespace2xml binary, proving what it is, running it, and encoding a value.

This is the half of the collection that is the same wherever the work happens. The filter runs
on the controller and the module runs on the target node, but both have to answer the same
questions before they can do anything useful: where is the binary, is it a build whose contract
this collection actually documents, what did it say when it ran, and how is a value written so
the tool reads it as data.

The node-side plugins import this module. The controller-side filter, for now, does not: it is
a single self-contained file loaded by ansible-core's filter machinery under a different Python,
and it carries its own copies of the same answers. That duplication is deliberate and temporary
-- see issue #107 -- and it is not left to good intentions: a unit test compares the two
encoders over an adversarial corpus and fails the build if they ever stop agreeing.

Keeping one answer to each of these is not tidiness. The refusal below -- that a binary without
a ``contract-bundle`` is a pre-3.0 build and must not be rendered through -- is the single
load-bearing safety property of this collection, and a copy of it is a copy that can drift out
of agreement with the original while both still pass their own tests.
"""

from __future__ import annotations

import os
import shutil
import subprocess

__all__ = [
    "Namespace2XmlError",
    "encode_value",
    "resolve",
    "tool_identity",
    "support_hint",
    "run_tool",
]


class Namespace2XmlError(Exception):
    """A failure: bad input data, a tool that could not be found, or a run that did not succeed.

    A plain exception, deliberately. This module ships to the managed node, where
    ``ansible.errors`` does not exist -- ``module_utils`` may import ``ansible.module_utils``
    and nothing else from Ansible. Each caller translates: the module turns it into
    ``failed: true`` carrying the message, and a controller-side caller is free to re-raise it
    as ``AnsibleFilterError``.
    """


_IDENTITY_CACHE: dict = {}

_VALUE_CONTAINER_SENTINELS = {"{}": "\\{}", "[]": "\\[]"}


def encode_value(text):
    """Encode a scalar as a section 8.3 interpreted value.

    A namespace-profile value is not literal text. Section 8.3 gives it a lexer -- ``\\\\``
    emits a backslash, ``\\n`` a line feed, ``\\t`` a tab, ``\\*`` a literal asterisk, ``\\${``
    a literal reference opener -- and section 8.4 makes every unescaped ``${`` the start of a
    reference that must resolve, on pain of a blocking ``REFERENCE001``/``REFERENCE002``.

    So a value that arrives as data has to be encoded through the inverse of that lexer before
    it is handed over. Without this, an operator writing the Windows path ``C:\\temp\\new`` gets
    ``C:`` followed by a tab and a line feed, and one writing ``${JAVA_HOME}/bin`` -- meaning
    the literal text, for some other program to resolve later -- gets a failed task.

    One pass, because escaping in stages re-escapes what an earlier stage produced. A value that
    is exactly ``{}`` or ``[]`` is escaped whole: unescaped, those two are the empty-container
    sentinels rather than strings.
    """
    if text in _VALUE_CONTAINER_SENTINELS:
        return _VALUE_CONTAINER_SENTINELS[text]

    out = []
    index = 0
    length = len(text)

    while index < length:
        char = text[index]

        if char == "\\":
            out.append("\\\\")
        elif char == "*":
            out.append("\\*")
        elif char == "$" and index + 1 < length and text[index + 1] == "{":
            out.append("\\${")
            index += 1
        elif char == "\n":
            out.append("\\n")
        elif char == "\r":
            out.append("\\r")
        elif char == "\t":
            out.append("\\t")
        else:
            out.append(char)

        index += 1

    return "".join(out)


def _executable_names():
    """The file names ``dotnet tool install --global`` produces on this platform."""
    if os.name == "nt":
        return ("namespace2xml.exe", "namespace2xml.cmd", "namespace2xml.bat", "namespace2xml")

    return ("namespace2xml",)


def _runnable(path):
    """Whether a path names a file this process could execute."""
    return os.path.isfile(path) and os.access(path, os.X_OK)


def _search(directory):
    """The first runnable candidate in a directory, or ``None``."""
    if not directory or not os.path.isdir(directory):
        return None

    for name in _executable_names():
        candidate = os.path.join(directory, name)

        if _runnable(candidate):
            return candidate

    return None


def _given(value, source):
    """Resolve something the caller supplied, which is authoritative: it resolves or it fails."""
    if os.sep in value or (os.altsep and os.altsep in value):
        if _runnable(value):
            return os.path.abspath(value)

        raise Namespace2XmlError(
            "%s names '%s', which is not an executable file." % (source, value))

    found = shutil.which(value)

    if found:
        return os.path.abspath(found)

    raise Namespace2XmlError(
        "%s names '%s', which was not found on PATH." % (source, value))


def _tool_directories():
    """Where ``dotnet tool install --global`` puts binaries, most specific first.

    ``PATH`` is not enough, and it is not enough for a different reason on each side. A filter
    runs inside ``ansible-playbook``, whose environment was fixed before the play began, so a
    tool installed by an earlier task in the same play is invisible to it. A module runs in a
    non-interactive shell on the node, which never sourced the profile that ``dotnet tool
    install`` told a human to re-source -- so on the node this is not an edge case but the
    ordinary outcome of following the published install instructions.
    """
    directories = [os.environ.get("DOTNET_TOOLS_PATH")]

    for base in (os.environ.get("DOTNET_CLI_HOME"),
                 os.environ.get("USERPROFILE") if os.name == "nt" else None,
                 os.environ.get("HOME"),
                 os.path.expanduser("~")):
        if base:
            directories.append(os.path.join(base, ".dotnet", "tools"))

    return directories


def resolve(tool=None):
    """Find the tool binary, or say precisely what to do about its absence."""
    if tool:
        return _given(tool, "the 'tool' argument")

    override = os.environ.get("NAMESPACE2XML")

    if override:
        return _given(override, "$NAMESPACE2XML")

    found = shutil.which("namespace2xml")

    if found:
        return os.path.abspath(found)

    for directory in _tool_directories():
        found = _search(directory)

        if found:
            return os.path.abspath(found)

    # '--prerelease' is load-bearing while 3.0 is on preview. Without it dotnet resolves the
    # highest stable version, which is 2.4.0 -- the build tool_identity() refuses for having no
    # contract-bundle. Omitting the flag here would send the reader round the loop twice:
    # install, get told the tool is a 2.x build, come back. Say it once, in the message that
    # sends them.
    raise Namespace2XmlError(
        "namespace2xml was not found on PATH or in the dotnet global tools directory. "
        "Install it with 'dotnet tool install --global --prerelease namespace2xml', or set "
        "$NAMESPACE2XML or the 'tool' argument to the binary's path. '--prerelease' is "
        "required while the 3.0 line is on preview: without it dotnet installs the 2.x tool, "
        "which this collection refuses.")


def _identity_key(executable):
    """Identify a binary by path, size and mtime, so an upgrade in place invalidates the entry."""
    try:
        info = os.stat(executable)
    except OSError:
        return None

    return (executable, info.st_mtime_ns, info.st_size)


def support_hint(executable):
    """The report address the binary itself publishes.

    ``--version`` emits a ``report:`` URL, so attaching it here points at the tracker belonging
    to the build that actually ran rather than at a link frozen into this file when it was
    written. A failure therefore carries its own way out, which is the whole point: the reader
    of the message is often an agent, and it has no other way to discover where this goes.
    """
    key = _identity_key(executable)
    entry = _IDENTITY_CACHE.get(key) if key is not None else None
    report = entry[1].get("report") if entry else None

    if not report:
        return ""

    return (
        "\n\nIf this is a defect rather than a mistake in the data, report it at %s. Quote this "
        "message verbatim and include the collection version, the output of 'ansible "
        "--version', and the tool's full '--version' output." % report)


def tool_identity(tool=None):
    """The contract identity of the binary that will do the work.

    Both halves matter. The package version says which build, and the ``contract-bundle``
    revision says which contract that build was compiled against -- two builds of one version
    against different bundle revisions may legitimately render differently.
    """
    executable = resolve(tool)
    key = _identity_key(executable)

    if key is not None and key in _IDENTITY_CACHE:
        return _IDENTITY_CACHE[key][0]

    completed = subprocess.run(
        [executable, "--version"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )

    if completed.returncode != 0:
        raise Namespace2XmlError(
            "'%s --version' exited %d: %s"
            % (executable, completed.returncode, (completed.stderr or "").strip()))

    fields = {}

    for line in completed.stdout.splitlines():
        if ":" in line:
            name, value = line.split(":", 1)
            fields[name.strip()] = value.strip()

    # A missing contract-bundle is the signal that this is a pre-3.0 binary, and it has to be
    # refused rather than recorded as "unknown". A 2.x build accepts the same -i/-s/-o arguments
    # and the same root and output scheme spellings, so it does not fail: it exits 0 and returns
    # a document rendered under 2.x escaping, type inference and XML rules. Every claim this
    # collection makes about its output is a claim about 3.x behaviour, so silently rendering
    # through 2.x would make the documentation untrue with nothing anywhere to say so.
    if "contract-bundle" not in fields:
        raise Namespace2XmlError(
            "'%s' does not look like a namespace2xml 3.x build: its '--version' output declares "
            "no 'contract-bundle', which every 3.x build emits. A 2.x binary takes the same "
            "arguments and scheme spellings, so it would render silently under the older "
            "contract instead of failing. Install a 3.x build with 'dotnet tool install "
            "--global --prerelease namespace2xml'." % executable)

    identity = "%s|%s" % (
        fields.get("version", completed.stdout.strip()),
        fields["contract-bundle"])

    if key is not None:
        _IDENTITY_CACHE[key] = (identity, fields)

    return identity


def run_tool(executable, argv):
    """Run the tool over prepared arguments and hand back whatever it said.

    Diagnostics are returned rather than raised on success, and folded into the exception on
    failure, because the tool's own text is the whole value of a non-zero exit: hiding a
    failure's explanation turns a precise contract error into "it did not work".

    :param executable: the resolved binary path.
    :param argv: arguments to pass after the executable.
    :returns: the tool's stderr, which carries any non-fatal diagnostics.
    """
    # The pipes are decoded as UTF-8 explicitly. ``text=True`` alone decodes with the locale
    # encoding and ``errors='strict'``, and the tool's diagnostics carry the section sign
    # (U+00A7) in every specification citation -- so on a host whose locale resolves to ASCII
    # the decode raises inside subprocess.run, before the returncode is ever examined. A
    # UnicodeDecodeError traceback would then replace the diagnostic exactly when there is one.
    completed = subprocess.run(
        [executable] + list(argv),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )

    if completed.returncode != 0:
        raise Namespace2XmlError(
            "'%s' exited %d\n%s%s"
            % (executable, completed.returncode, (completed.stderr or "").strip(),
               support_hint(executable)))

    return completed.stderr or ""
