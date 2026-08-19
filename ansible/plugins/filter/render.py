# Copyright (c) 2026 stop-cran
# Apache License 2.0 (see LICENSE)

from __future__ import annotations

DOCUMENTATION = r"""
name: render
short_description: Render a data structure as XML, JSON, YAML, INI or namespace text
version_added: 1.0.0
author:
  - stop-cran (@stop-cran)
description:
  - Flattens a mapping into a C(namespace2xml) profile and runs the C(namespace2xml)
    transformer over it, returning the rendered text.
  - The filter evaluates on the controller, where templating happens. Target nodes need
    neither .NET nor the tool; pair this filter with M(ansible.builtin.copy) to place the
    result, which already owns idempotence, check mode, diff, backup, ownership and SELinux
    context.
  - Encoding follows the normative specification rather than the tool's observed output.
    Name parts are escaped per section 8.2, values per section 8.3, and a name part that is a
    canonical decimal integer makes its parent a sequence per section 8.7.
positional: _input, fmt
options:
  _input:
    description:
      - The data to render. Normally a mapping; any JSON-shaped structure of mappings,
        sequences, strings, numbers, booleans and nulls is accepted.
      - An empty mapping or sequence is preserved as an empty container rather than dropped,
        so it renders as an empty element instead of disappearing.
    type: raw
    required: true
  fmt:
    description:
      - The output format, from section 16.1 of the specification.
    type: str
    required: true
    choices:
      - xml
      - json
      - yaml
      - ini
      - namespace
      - quotednamespace
  root:
    description:
      - The section 16.3 root, which names the XML document element.
      - XML has exactly one document element, so this is required whenever the selected view
        has more than one top-level member. It is not guessed, because the element name is a
        fact about the target document rather than about the data.
      - Mutually exclusive with O(scheme). It is read only while synthesizing a scheme, so
        supplying both is refused rather than silently ignored.
    type: str
  selector:
    description:
      - The top-level name the data is written under in the generated profile.
      - Change it only when supplying O(scheme), whose declared selector must match.
    type: str
    default: cfg
  scheme:
    description:
      - Explicit scheme text, used instead of the minimal scheme the filter would synthesize.
      - Needed for anything the synthesized scheme does not cover, such as C(type),
        C(substitute) or C(hidden) rules. The selector it declares must equal O(selector).
      - O(root) and O(delimiter) are refused alongside it, because they are read only while
        synthesizing a scheme and would otherwise be discarded without a word. Declare them in
        the scheme instead.
      - O(fmt) is still required, and is cross-checked against the C(output) the scheme
        declares. A disagreement is an error rather than an override, because the scheme wins
        and the argument would otherwise be a value the caller was compelled to supply and the
        filter then ignored.
    type: str
  delimiter:
    description:
      - The section 16.4 output delimiter, for the flat formats.
      - Mutually exclusive with O(scheme), for the reason given there.
    type: str
  tool:
    description:
      - Path to the C(namespace2xml) binary, or a bare name to look up on C(PATH).
      - When unset the filter looks at E(NAMESPACE2XML), then C(PATH), then the dotnet
        global-tools directory. A value given here is authoritative - it resolves or the filter
        fails, so a typo is never masked by a lucky C(PATH) hit.
    type: path
  memoize:
    description:
      - Reuse the result of an identical earlier render within the same run.
      - The cache key covers the whole marshalled input plus the tool's version and contract
        revision, so it cannot survive a tool or contract change. Set to V(false) to force
        every call to spawn the tool.
      - A memoized call does not re-run the tool, so any warning the tool reports is shown
        once for a given input rather than once per call.
    type: bool
    default: true
  workdir:
    description:
      - Parent directory for the temporary directory each render marshals its input through.
      - Defaults to the platform temporary directory. Useful when that directory is
        C(noexec), small, or not shared with the tool.
    type: path
requirements:
  - The C(namespace2xml) .NET tool, version 3.0 or later, on the controller.
  - 3.0 is in preview at the time of writing and the newest stable release is 2.4.0, so a plain
    C(dotnet tool install --global namespace2xml) installs a 2.x build. Ask for the preview
    explicitly with C(dotnet tool install --global --prerelease namespace2xml).
  - The filter refuses a pre-3.0 binary rather than rendering through it. A 2.x build accepts
    the same arguments and the same scheme spellings, so it would otherwise exit successfully
    and return a document rendered under the older contract, with nothing to say so.
notes:
  - Payload types are inferred from value text (section 18), so the string V("true") and the
    boolean V(true) produce the same record, as do V("3") and V(3). A C(type) scheme rule is
    the only way to force a string.
  - Keys that are canonical decimal integers make their parent a sequence (section 8.7). A
    leading zero disables that inference for the whole parent, which is a sharp edge for
    zero-padded keys.
  - Binary data has no value spelling. The filter refuses C(bytes) rather than guessing an
    encoding.
  - XML attributes, content tokens and namespace-qualified element names cannot be addressed.
    Every key is escaped so that it reads back as itself, so a C(@name) key becomes a literal
    element named C(\@name), which is not an C(NCName) and is reported as a blocking C(XML002)
    rather than silently producing a wrong document. See the collection README.
  - Diagnostics are the tool's own and reach you unchanged. Warnings arrive on a successful
    render and are shown through Ansible's display; errors carry the tool's text and the
    address to report it to. Every code is listed in the diagnostic registry linked below.
seealso:
  - name: Contract summary, shipped in this collection
    description: >-
      The rules that decide what this filter does to your data, quoted verbatim from the
      specification and checked against it in CI. It is installed alongside this plugin at
      C(docs/specification-summary.md) inside the collection, so it is readable with no network
      access. Start here; the full specification is 300 KB.
    link: https://github.com/stop-cran/namespace2xml/blob/master/ansible/docs/specification-summary.md
  - name: namespace2xml specification
    description: The normative contract this filter encodes against.
    link: https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md
  - name: Diagnostic code registry
    description: >-
      Every code the tool emits, what it means, and the specification clause it enforces. A
      code arriving from this filter, such as C(XML002) or C(WARN009), is looked up here.
    link: https://github.com/stop-cran/namespace2xml/blob/master/docs/diagnostics.md
  - name: Reporting a problem
    description: >-
      The report form, the four destinations a report can take, and the rules for
      agent-authored reports. Read this before filing.
    link: https://github.com/stop-cran/namespace2xml/blob/master/CONTRIBUTING.md#4-the-feedback-channel-binding
  - name: Issue tracker
    description: >-
      The four issue forms. Select the component "Ansible filter" for a fault reached through
      this plugin.
    link: https://github.com/stop-cran/namespace2xml/issues/new/choose
  - name: Guide for automated agents
    description: Read order, repository map, and the rules an agent follows when reporting here.
    link: https://github.com/stop-cran/namespace2xml/blob/master/AGENTS.md
  - name: namespace2xml on NuGet
    description: The transformer this filter runs.
    link: https://www.nuget.org/packages/namespace2xml
  - module: ansible.builtin.copy
"""

EXAMPLES = r"""
- name: Render a logback configuration and place it on the target
  ansible.builtin.copy:
    content: "{{ logback | stop_cran.namespace2xml.render('xml', root='configuration') }}"
    dest: /opt/app/logback.xml
    mode: "0644"
  vars:
    logback:
      appender:
        name: STDOUT
        encoder:
          pattern: "%d{HH:mm:ss} %-5level %msg%n"
      root:
        level: info

- name: Render the same data as JSON
  ansible.builtin.debug:
    msg: "{{ service | stop_cran.namespace2xml.render('json') }}"
  vars:
    service:
      port: 8080
      hosts:
        - alpha
        - beta

- name: Render INI with an explicit scheme that forces a string
  ansible.builtin.copy:
    content: "{{ settings | stop_cran.namespace2xml.render('ini', scheme=scheme) }}"
    dest: /etc/app/app.ini
    mode: "0644"
  vars:
    settings:
      section:
        version: "3"
    scheme: |
      cfg.output=ini
      cfg.*.version.type=string

- name: Render with a tool built somewhere the default search would not find
  ansible.builtin.debug:
    msg: "{{ data | stop_cran.namespace2xml.render('yaml', tool='/opt/n2x/namespace2xml') }}"
"""

RETURN = r"""
_value:
  description:
    - The rendered configuration text, exactly as the tool wrote it, including its trailing
      newline if it wrote one.
  type: str
"""

import hashlib
import os
import shutil
import subprocess
import sys
import tempfile
import unicodedata

__all__ = ["render", "flatten", "encode_name_part", "encode_value", "tool_identity"]

DEFAULT_SELECTOR = "cfg"

# Section 16.1. A filter plugin's `choices:` documentation is not enforced at runtime, so this
# list is the only thing standing between a mistyped format and a scheme directive built from it.
FORMATS = ("xml", "json", "yaml", "ini", "namespace", "quotednamespace")

_NAME_SHORT_ESCAPES = frozenset(".*=#!$@}")
_VALUE_CONTAINER_SENTINELS = {"{}": "\\{}", "[]": "\\[]"}
_FORCED_HEX = frozenset("\u0085\u2028\u2029")

_IDENTITY_CACHE: dict = {}
_RENDER_CACHE: dict = {}


try:  # pragma: no cover -- exercised by whichever of the two environments is running
    from ansible.errors import AnsibleFilterError as _FilterErrorBase
except ImportError:
    # Ansible is always importable where this plugin is loaded. The fallback exists so the
    # encoder can be exercised on a machine with no controller installed, which is where the
    # specification-side half of the oracle is cheapest to run.
    _FilterErrorBase = Exception

try:  # pragma: no cover -- as above
    from ansible.utils.display import Display

    _DISPLAY = Display()
except ImportError:
    _DISPLAY = None


class Namespace2XmlError(_FilterErrorBase):  # type: ignore[valid-type, misc]
    """A failure: bad input data, a tool that could not be found, or a run that did not succeed.

    Derived from ``AnsibleFilterError`` so a play reports a failed template as a filter error
    with the message attached rather than as a traceback from an unrecognised exception type.
    """


def _needs_hex(char):
    """Whether a scalar has to be written as ``\\u{HEX}`` rather than as itself."""
    if char in _FORCED_HEX:
        return True

    # Cc, Cf and Cs are the section 19.1 forbidden set. A record is a line, so CR and LF would
    # end it early; they are Cc and so already covered, but the intent is worth stating.
    return unicodedata.category(char) in ("Cc", "Cf", "Cs")


def encode_name_part(part):
    """Encode one qualified-name part so the section 8.2 lexer reads back the text given.

    The encoding is total: every scalar has a spelling, either a short escape or ``\\u{HEX}``.
    ``Q`` is escaped only in first position, which is the only place ``Q{`` can introduce an XML
    canonical component; ``@`` and ``#`` are escaped everywhere, because the short forms exist
    and are cheaper to reason about than a positional rule.
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
    """Encode a scalar as a section 8.3 interpreted value.

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


def encode_scalar(value):
    """Encode a leaf as namespace value text.

    Section 18 infers the payload type from this text, so the mapping is lossy in one direction
    that matters: the string ``"true"`` and the boolean ``True`` produce the same record, and so
    do the string ``"3"`` and the integer ``3``. The README records this; a ``type`` scheme rule
    is the only way to force a string.
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

    An empty mapping or sequence becomes the section 8.3 sentinel rather than disappearing,
    which is the difference between an emitted ``<empty />`` and no element at all.
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
            # Section 8.7: a canonical decimal integer part makes the parent a sequence.
            # str(int) has no leading zero, and a leading zero would disable inference for the
            # whole parent.
            _walk(value, path + [str(index)], records)

        return

    records.append(name + "=" + encode_scalar(node))


def _reject_record_breaks(name, value):
    """Refuse a line break in a value interpolated raw into the generated scheme.

    A scheme is line-oriented, so a break does not corrupt the directive it sits in -- it ends
    it and starts another. Section 15.2 then lets the later directive override the earlier one,
    which is what makes this worth a guard rather than a note: a newline in ``root`` can append
    a second ``output`` declaration to a scheme built for XML, and the tool exits 0 having
    written exactly what the appended line asked for. The caller receives a well-formed document
    in the wrong format and no diagnostic at all.

    This applies only where escaping is not available. A Section 8.3 value is encoded on the way
    in, and ``\\n`` inside one is an interpreted escape rather than a record break, so an encoded
    argument is already safe and refusing it would remove a capability for nothing. ``root`` is
    the argument that cannot be encoded: Section 8.2 makes it carry typed component markers and
    its own backslash escapes, so encoding would rewrite what the caller wrote. Refusing is what
    is left, and it costs nothing real -- no document element name contains a line break.
    """
    if "\n" in value or "\r" in value:
        raise Namespace2XmlError(
            "'%s' contains a line break. The scheme is line-oriented, so the text after the "
            "break would be read as a further scheme directive rather than as part of the "
            "value. Remove it, or write the scheme yourself and pass it as 'scheme'." % name)


def synthesize_scheme(fmt, selector=DEFAULT_SELECTOR, root=None, delimiter=None):
    """Build the minimal scheme a render needs.

    Every run needs an ``output`` declaration. XML additionally needs a ``root`` whenever the
    selected view has more than one top-level member, because XML has one document element; the
    caller supplies it rather than the filter guessing, because the element name is a fact about
    the target document and not about the data.

    The three interpolated arguments are guarded differently because the specification gives
    them different kinds, and a single blanket treatment would be wrong for two of them.
    ``fmt`` is one of the six Section 16.1 formats, so it is checked against that list.
    ``delimiter`` is an ordinary Section 8.3 value and is encoded as one, which also gives a tab
    delimiter its ``\\t`` spelling. ``root`` is neither: Section 8.2 makes it carry typed XML
    component markers and its own backslash escapes, so value-encoding it would silently rewrite
    what the caller wrote -- ``\\@id`` would become ``\\\\@id`` and stop naming an escaped
    literal. It is therefore left verbatim and merely refused if it could break the record.
    """
    if fmt not in FORMATS:
        raise Namespace2XmlError(
            "'%s' is not one of the section 16.1 output formats. Use one of: %s."
            % (fmt, ", ".join(FORMATS)))

    lines = ["%s.output=%s" % (encode_name_part(selector), fmt)]

    if root is not None:
        root = str(root)
        _reject_record_breaks("root", root)
        lines.append("%s.root=%s" % (encode_name_part(selector), root))

    if delimiter is not None:
        delimiter = str(delimiter)
        lines.append(
            "%s.delimiter=%s" % (encode_name_part(selector), encode_value(delimiter)))

    return "".join(line + "\n" for line in lines)


def _split_unescaped(text, separator):
    """Split on a separator that a preceding backslash protects, per Section 8.2."""
    parts = []
    current = []
    index = 0

    while index < len(text):
        char = text[index]

        if char == "\\" and index + 1 < len(text):
            current.append(char)
            current.append(text[index + 1])
            index += 2
            continue

        if char == separator:
            parts.append("".join(current))
            current = []
        else:
            current.append(char)

        index += 1

    parts.append("".join(current))

    return parts


def _declared_outputs(scheme_text):
    """The set of formats an explicit scheme declares, for cross-checking against ``fmt``.

    This reads deliberately little. Section 8 record kinds are honoured -- an unescaped leading
    ``#`` is a comment and an unescaped leading ``!`` is a mask, so neither declares anything --
    and a name is split on unescaped dots so that a rule ending in a literal ``\\.output`` part
    is not mistaken for one. Everything past that is left alone: the point is to catch a plain
    disagreement, not to re-implement Section 15.2 matching here. When nothing is recognised the
    caller stays silent and lets the tool speak for itself.
    """
    outputs = set()

    for line in scheme_text.splitlines():
        stripped = line.strip(" \t")

        if not stripped or stripped[0] in "#!":
            continue

        pieces = _split_unescaped(stripped, "=")

        if len(pieces) < 2:
            continue

        name_parts = _split_unescaped(pieces[0], ".")

        if name_parts[-1].strip() == "output":
            outputs.add("=".join(pieces[1:]).strip())

    return outputs


def _refuse_swallowed_arguments(scheme_text, fmt, root, delimiter):
    """Refuse arguments that an explicit scheme would silently discard.

    ``root`` and ``delimiter`` are read only while synthesizing a scheme. Passed alongside one,
    they reach nothing -- and the render then succeeds, returning a document that ignored them.
    That is the failure this collection exists to avoid, so it is made loud.

    ``fmt`` is worse, because it is a required positional: before this check the API compelled
    every custom-scheme caller to name a format and then ignored the answer. Cross-checking it
    against the scheme's own declaration turns that compelled value into the one thing it can
    usefully be.
    """
    swallowed = [name for name, value in (("root", root), ("delimiter", delimiter))
                 if value is not None]

    if swallowed:
        raise Namespace2XmlError(
            "%s cannot be combined with an explicit 'scheme'. Those arguments are read only "
            "while synthesizing a scheme, so here they would be discarded and the render would "
            "succeed having ignored them. Declare them in the scheme instead, as "
            "'<selector>.root=...' and '<selector>.delimiter=...'."
            % " and ".join("'%s'" % name for name in swallowed))

    declared = _declared_outputs(scheme_text)

    if declared and fmt not in declared:
        raise Namespace2XmlError(
            "the scheme declares output %s, but the filter was asked for '%s'. The format "
            "argument is not applied on top of an explicit scheme, so one of the two is a "
            "mistake rather than a refinement of the other; make them agree."
            % (" and ".join(sorted("'%s'" % value for value in declared)), fmt))


def _identity_key(executable):
    """Identify the binary by what it is, not only by where it is.

    ``dotnet tool update --global`` rewrites the shim in place, at the same path. A path-keyed
    cache would go on serving the pre-upgrade identity for the life of the ``ansible-playbook``
    process, and because that identity is a component of the render key, pre-upgrade *output*
    with it. The README promises the render cache cannot survive a tool upgrade, and that
    promise is only kept if this key moves when the file does.
    """
    try:
        info = os.stat(executable)
    except OSError:
        return None

    return (executable, info.st_mtime_ns, info.st_size)


def _support_hint(executable):
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

    Both halves matter to a cache key. The package version says which build, and the
    ``contract-bundle`` revision says which contract that build was compiled against -- two
    builds of one version against different bundle revisions may legitimately render
    differently.
    """
    executable = _resolve(tool)
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

    ``PATH`` is not enough. The install writes to this directory whether or not the login shell
    happens to name it, and a filter runs inside ``ansible-playbook``, whose environment was
    fixed before the play began -- so a tool installed by an earlier task in the same play is
    invisible to ``PATH`` no matter what that task did to it.
    """
    directories = [os.environ.get("DOTNET_TOOLS_PATH")]

    for base in (os.environ.get("DOTNET_CLI_HOME"),
                 os.environ.get("USERPROFILE") if os.name == "nt" else None,
                 os.environ.get("HOME"),
                 os.path.expanduser("~")):
        if base:
            directories.append(os.path.join(base, ".dotnet", "tools"))

    return directories


def _resolve(tool):
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
    # highest stable version, which is 2.4.0 -- the build _identify() refuses for having no
    # contract-bundle. Omitting the flag here would send the reader round the loop twice: install,
    # get told the tool is a 2.x build, come back. Say it once, in the message that sends them.
    raise Namespace2XmlError(
        "namespace2xml was not found on PATH or in the dotnet global tools directory. "
        "Install it with 'dotnet tool install --global --prerelease namespace2xml', or set "
        "$NAMESPACE2XML or the filter's 'tool' argument to the binary's path. '--prerelease' is "
        "required while the 3.0 line is on preview: without it dotnet installs the 2.x tool, "
        "which this filter refuses.")


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
    :param fmt: one of the section 16.1 output formats -- ``xml``, ``json``, ``yaml``, ``ini``,
        ``namespace``, ``quotednamespace``.
    :param scheme: explicit scheme text, used instead of the synthesized minimal one. The
        selector it declares must be ``selector``.
    :param root: the section 16.3 root, which XML needs for a multi-member view.
    :param selector: the top-level name the data is written under.
    :param delimiter: the section 16.4 output delimiter, for the flat formats.
    :param tool: path to the binary. Defaults to ``$NAMESPACE2XML``, then ``PATH``, then the
        dotnet global-tools directory. An explicit value must resolve: it is never fallen back
        on.
    :param memoize: reuse a previous identical render. The key is the whole marshalled input
        plus the tool's contract identity, so it cannot survive a tool or contract change.
    :param workdir: parent directory for the temporary marshalling directory.
    :returns: the rendered text.
    """
    profile = flatten(config, selector)

    if scheme is not None:
        _refuse_swallowed_arguments(scheme, fmt, root, delimiter)

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

    A filter has data in memory and the CLI is file-in, directory-out, so every call pays a
    directory, two writes, a process, a read and a delete.
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


def _warn(text):
    """Surface the tool's non-fatal diagnostics instead of discarding them.

    The tool writes WARN codes to stderr for the things it can see but must not decide -- a
    scheme rule matching no path, an output instance selecting nothing. Section 15.2 emits
    those precisely so that a mistyped rule is not silent; a filter that captured stderr and
    then dropped it on success would put the silence back, and the author would get a
    plausible-looking document with the rule they wrote having done nothing.
    """
    for line in text.splitlines():
        line = line.strip()

        if not line:
            continue

        if _DISPLAY is not None:
            _DISPLAY.warning(line)
        else:
            sys.stderr.write("namespace2xml: %s\n" % line)


def _run_and_read(executable, input_path, scheme_path, output_dir):
    """Spawn the tool over prepared files and read the single output back."""
    # The pipes are decoded as UTF-8 explicitly. `text=True` alone decodes with the locale
    # encoding and `errors='strict'`, and the tool's diagnostics carry the section sign (U+00A7)
    # in every specification citation -- so on a controller whose locale resolves to ASCII the
    # decode raises inside subprocess.run, before the returncode is ever examined. A
    # UnicodeDecodeError traceback would then replace the diagnostic exactly when there is one.
    completed = subprocess.run(
        [executable, "-i", input_path, "-s", scheme_path, "-o", output_dir],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )

    if completed.returncode != 0:
        # The diagnostic text is the whole value of a non-zero exit here: hiding a failure's own
        # explanation turns a precise contract error into "the filter did not work".
        raise Namespace2XmlError(
            "'%s' exited %d\n%s%s"
            % (executable, completed.returncode, (completed.stderr or "").strip(),
               _support_hint(executable)))

    _warn(completed.stderr or "")

    # "dummy", not "_": ansible-test's pylint profile lists "_" in bad-names, so the
    # conventional Python throwaway fails collection sanity.
    produced = sorted(
        os.path.join(base, name)
        for base, dummy, names in os.walk(output_dir)
        for name in names)

    if len(produced) != 1:
        raise Namespace2XmlError(
            "expected exactly one output file, got %d: %s%s"
            % (len(produced), ", ".join(os.path.basename(path) for path in produced),
               _support_hint(executable)))

    with open(produced[0], "r", encoding="utf-8", newline="") as handle:
        return handle.read()


def _write(path, text):
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


class FilterModule:
    """Ansible discovers this class and calls :meth:`filters`."""

    def filters(self):
        """Expose ``stop_cran.namespace2xml.render``.

        One filter, deliberately. Galaxy cannot yank a single version, so the first release
        carries the least permanent surface it can. ``flatten`` is useful and needs no binary,
        but exposing it would freeze the name-encoding convention as a public output before
        issue #103 has decided whether that convention should be selectable.
        """
        return {"render": render}
