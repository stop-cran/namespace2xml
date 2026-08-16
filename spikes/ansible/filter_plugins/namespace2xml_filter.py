"""Prototype Ansible filter plugin for namespace2xml.

**Status: spike. Not shipped, not installed, not a supported interface.** It exists to answer the
questions in issue #37 by measurement rather than by argument, and to be thrown away once a real
``namespace2xml.core`` collection exists in its own repository.

The shape under test is a *filter*, not a module and not an action plugin. A filter is a pure
function from data to text, which is exactly what the tool is, and it gives away every hard
problem: ``ansible.builtin.copy`` already owns idempotence, check mode, diff mode, backup,
ownership and SELinux context, and has for a decade.

    - ansible.builtin.copy:
        content: "{{ logback_config | namespace2xml_render('xml', root='configuration') }}"
        dest: /opt/app/logback.xml

Nothing here imports Ansible. The module is importable and callable on any platform with Python and
the tool on PATH, so the encoder and the marshalling can be exercised where an Ansible controller
cannot run. ``FilterModule`` at the bottom is a thin wrapper that Ansible discovers.

The encoding rules are read from the specification, not from the tool's output:

* Section 8.2 -- name-part escapes ``\\.`` ``\\*`` ``\\=`` ``\\#`` ``\\!`` ``\\$`` ``\\@`` ``\\}``
  ``\\Q`` ``\\\\`` and ``\\u{HEX}``; an empty name part is a parse error; an unescaped ``@``,
  ``#N`` or ``Q{`` at the start of a part commits to a typed XML component.
* Section 8.3 -- value escapes ``\\\\`` ``\\*`` ``\\${`` ``\\n`` ``\\r`` ``\\t``; every other
  backslash sequence preserves itself; a value of exactly ``{}`` or ``[]`` is an empty container
  rather than a string.
* Section 8.7 -- a name part that is a canonical decimal integer makes its parent a sequence.
* Section 18 -- payload types are inferred from the value text, so ``true`` is a boolean and ``3``
  is an integer no matter what the source data meant.
"""

from __future__ import annotations

import hashlib
import os
import shutil
import subprocess
import sys
import tempfile
import unicodedata

__all__ = ["render", "flatten", "encode_name_part", "encode_value", "tool_identity"]

DEFAULT_SELECTOR = "cfg"

_NAME_SHORT_ESCAPES = frozenset(".*=#!$@}")
_VALUE_CONTAINER_SENTINELS = {"{}": "\\{}", "[]": "\\[]"}
_FORCED_HEX = frozenset("\u0085\u2028\u2029")

_IDENTITY_CACHE: dict = {}
_RENDER_CACHE: dict = {}


class Namespace2XmlError(Exception):
    """A prototype-level failure: bad input data, or a tool run that did not succeed."""


def _needs_hex(char):
    """Whether a scalar has to be written as ``\\u{HEX}`` rather than as itself."""
    if char in _FORCED_HEX:
        return True

    # Cc, Cf and Cs are the Section 19.1 forbidden set. A record is a line, so CR and LF would end
    # it early; they are Cc and so already covered, but the intent is worth stating.
    return unicodedata.category(char) in ("Cc", "Cf", "Cs")


def encode_name_part(part):
    """Encode one qualified-name part so the Section 8.2 lexer reads back the text given.

    The encoding is total: every scalar has a spelling, either a short escape or ``\\u{HEX}``.
    ``Q`` is escaped only in first position, which is the only place ``Q{`` can introduce an XML
    canonical component; ``@`` and ``#`` are escaped everywhere, because the short forms exist and
    are cheaper to reason about than a positional rule.
    """
    if part == "":
        raise Namespace2XmlError("an empty name part is a parse error (section 8.2)")

    out = []

    for index, char in enumerate(part):
        if char == "\\":
            out.append("\\\\")
        elif char in _NAME_SHORT_ESCAPES:
            out.append("\\" + char)
        elif char == "Q" and index == 0:
            out.append("\\Q")
        elif _needs_hex(char):
            out.append("\\u{%X}" % ord(char))
        else:
            out.append(char)

    return "".join(out)


def encode_value(text):
    """Encode a scalar as a Section 8.3 interpreted value.

    One pass, because escaping in stages re-escapes what an earlier stage produced. A value that is
    exactly ``{}`` or ``[]`` is escaped whole: unescaped, those two are the empty-container
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


def encode_scalar(value):
    """Encode a leaf as namespace value text.

    Section 18 infers the payload type from this text, so the mapping is lossy in one direction
    that matters: the string ``"true"`` and the boolean ``True`` produce the same record, and so do
    the string ``"3"`` and the integer ``3``. FINDINGS.md records this; a ``type`` scheme rule is
    the only way to force a string.
    """
    if value is None:
        return "null"

    if isinstance(value, bool):
        return "true" if value else "false"

    if isinstance(value, float):
        return encode_value(repr(value))

    if isinstance(value, int):
        return encode_value(str(value))

    if isinstance(value, (bytes, bytearray)):
        raise Namespace2XmlError(
            "binary data has no namespace value spelling; decode it to text first")

    return encode_value(value if isinstance(value, str) else str(value))


def flatten(config, selector=DEFAULT_SELECTOR):
    """Flatten a dictionary into a namespace profile rooted at ``selector``.

    An empty mapping or sequence becomes the Section 8.3 sentinel rather than disappearing, which
    is the difference between an emitted ``<empty />`` and no element at all.
    """
    records = []
    _walk(config, [encode_name_part(selector)], records)

    return "".join(record + "\n" for record in records)


def _walk(node, path, records):
    name = ".".join(path)

    if isinstance(node, dict):
        if not node:
            records.append(name + "={}")
            return

        for key, value in node.items():
            _walk(value, path + [encode_name_part(str(key))], records)

        return

    if isinstance(node, (list, tuple)):
        if not node:
            records.append(name + "=[]")
            return

        for index, value in enumerate(node):
            # Section 8.7: a canonical decimal integer part makes the parent a sequence. str(int)
            # has no leading zero, and a leading zero would disable inference for the whole parent.
            _walk(value, path + [str(index)], records)

        return

    records.append(name + "=" + encode_scalar(node))


def synthesize_scheme(fmt, selector=DEFAULT_SELECTOR, root=None, delimiter=None):
    """Build the minimal scheme a render needs.

    Every run needs an ``output`` declaration. XML additionally needs a ``root`` whenever the
    selected view has more than one top-level member, because XML has one document element; the
    caller supplies it rather than the filter guessing, because the element name is a fact about
    the target document and not about the data.
    """
    lines = ["%s.output=%s" % (encode_name_part(selector), fmt)]

    if root is not None:
        lines.append("%s.root=%s" % (encode_name_part(selector), root))

    if delimiter is not None:
        lines.append("%s.delimiter=%s" % (encode_name_part(selector), delimiter))

    return "".join(line + "\n" for line in lines)


def tool_identity(tool=None):
    """The contract identity of the binary that will do the work.

    Both halves matter to a cache key. The package version says which build, and the
    ``contract-bundle`` revision says which contract that build was compiled against -- two builds
    of one version against different bundle revisions may legitimately render differently.
    """
    executable = _resolve(tool)

    if executable in _IDENTITY_CACHE:
        return _IDENTITY_CACHE[executable]

    completed = subprocess.run(
        [executable, "--version"],
        capture_output=True,
        text=True,
        check=False,
    )

    if completed.returncode != 0:
        raise Namespace2XmlError(
            "'%s --version' exited %d: %s"
            % (executable, completed.returncode, (completed.stderr or "").strip()))

    fields = {}

    for line in completed.stdout.splitlines():
        if ":" in line:
            key, _, value = line.partition(":")
            fields[key.strip()] = value.strip()

    identity = "%s|%s" % (
        fields.get("version", completed.stdout.strip()),
        fields.get("contract-bundle", "unknown"))

    _IDENTITY_CACHE[executable] = identity

    return identity


def _resolve(tool):
    if tool:
        return tool

    return os.environ.get("NAMESPACE2XML", "namespace2xml")


def render(
    config,
    fmt,
    scheme=None,
    root=None,
    selector=DEFAULT_SELECTOR,
    delimiter=None,
    tool=None,
    memoize=True,
    workdir=None,
):
    """Render a dictionary as configuration text in ``fmt``.

    :param config: the data to render.
    :param fmt: one of the Section 16.1 output formats -- ``xml``, ``json``, ``yaml``, ``ini``,
        ``namespace``, ``quotednamespace``.
    :param scheme: explicit scheme text, used instead of the synthesized minimal one. The selector
        it declares must be ``selector``.
    :param root: the Section 16.3 root, which XML needs for a multi-member view.
    :param selector: the top-level name the data is written under.
    :param delimiter: the Section 16.4 output delimiter, for the flat formats.
    :param tool: path to the binary; defaults to ``$NAMESPACE2XML`` then ``namespace2xml``.
    :param memoize: reuse a previous identical render. The key is the whole marshalled input plus
        the tool's contract identity, so it cannot survive a tool or contract change.
    :param workdir: parent directory for the temporary marshalling directory.
    :returns: the rendered text.
    """
    profile = flatten(config, selector)
    scheme_text = scheme if scheme is not None else synthesize_scheme(
        fmt, selector, root, delimiter)
    identity = tool_identity(tool)
    key = None

    if memoize:
        key = _cache_key(profile, scheme_text, fmt, identity)

        if key in _RENDER_CACHE:
            return _RENDER_CACHE[key]

    text = _marshal_and_run(profile, scheme_text, _resolve(tool), workdir)

    if key is not None:
        _RENDER_CACHE[key] = text

    return text


def _cache_key(profile, scheme_text, fmt, identity):
    digest = hashlib.sha256()

    for part in (profile, scheme_text, fmt, identity):
        digest.update(part.encode("utf-8"))
        digest.update(b"\x00")

    return digest.hexdigest()


def _marshal_and_run(profile, scheme_text, executable, workdir):
    """Write the inputs, run the tool, read the single output back, and clean up.

    This is the cost the issue asks to have measured: a filter has data in memory and the CLI is
    file-in, directory-out, so every call pays a directory, two writes, a process, a read and a
    delete. ``--stdout`` (G4) would remove most of it; ``measure-invocation.py`` is what says
    whether that is worth reopening a deferred decision for.
    """
    directory = tempfile.mkdtemp(prefix="n2x-", dir=workdir)

    try:
        input_path = os.path.join(directory, "input.txt")
        scheme_path = os.path.join(directory, "scheme.txt")
        output_dir = os.path.join(directory, "out")

        _write(input_path, profile)
        _write(scheme_path, scheme_text)
        os.mkdir(output_dir)

        return _run_and_read(executable, input_path, scheme_path, output_dir)
    finally:
        shutil.rmtree(directory, ignore_errors=True)


def _run_and_read(executable, input_path, scheme_path, output_dir):
    """Spawn the tool over prepared files and read the single output back.

    Split out of :func:`_marshal_and_run` so the harness can time the spawn-and-read alone. The
    difference between the two is the marshalling overhead, which is the number the deferred
    ``--stdout`` decision turns on.
    """
    completed = subprocess.run(
        [executable, "-i", input_path, "-s", scheme_path, "-o", output_dir],
        capture_output=True,
        text=True,
        check=False,
    )

    if completed.returncode != 0:
        # The native-aot spike recorded the cost of hiding a failure's own explanation. The
        # diagnostic text is the whole value of a non-zero exit here.
        raise Namespace2XmlError(
            "'%s' exited %d\n%s"
            % (executable, completed.returncode, (completed.stderr or "").strip()))

    produced = sorted(
        os.path.join(base, name)
        for base, _, names in os.walk(output_dir)
        for name in names)

    if len(produced) != 1:
        raise Namespace2XmlError(
            "expected exactly one output file, got %d: %s"
            % (len(produced), ", ".join(os.path.basename(path) for path in produced)))

    with open(produced[0], "r", encoding="utf-8", newline="") as handle:
        return handle.read()


def _write(path, text):
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


class FilterModule:
    """Ansible discovers this class and calls :meth:`filters`."""

    def filters(self):
        """Expose the prototype filters.

        In a published ``namespace2xml.core`` collection these would be reached as
        ``namespace2xml.core.render``. The unqualified names exist because a bare
        ``filter_plugins/`` directory beside a playbook is the smallest thing that runs.
        """
        return {
            "namespace2xml_render": render,
            "namespace2xml_profile": flatten,
        }


if __name__ == "__main__":
    sys.stderr.write(__doc__ or "")
    sys.exit(0)
