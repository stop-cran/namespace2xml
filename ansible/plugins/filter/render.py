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
        C(substitute) or C(merge) rules. The selector it declares must equal O(selector).
      - O(root) and O(delimiter) are refused alongside it, because they are read only while
        synthesizing a scheme and would otherwise be discarded without a word. Declare them in
        the scheme instead.
      - O(fmt) is still required, and is cross-checked against the C(output) the scheme
        declares. A disagreement is an error rather than an override, because the scheme wins
        and the argument would otherwise be a value the caller was compelled to supply and the
        filter then ignored. Names and formats are compared case-insensitively and a
        comma-separated C(output) is read as the set it declares, following sections 15 and
        16.1.
      - A filter returns one document, so a scheme that produces a set of files - several
        formats in one C(output) declaration, or several C(filename) targets - has no single
        result to hand back and the render fails. Use the C(stop_cran.namespace2xml.render)
        module when the scheme is meant to produce several files.
      - Mutually exclusive with O(scheme_yaml).
    type: str
  scheme_yaml:
    description:
      - The same scheme as O(scheme), written as a native mapping instead of a block of text,
        so it reads as part of the playbook rather than as an embedded document.
      - B(The nesting carries the path.) Section 9 makes a JSON or YAML mapping key one name
        part, so a dot does not separate names here as it does in O(scheme) - a key containing
        a dot asks for one name with a literal dot in it. It is refused rather than passed
        through, because as a selector it would draw only C(WARN009) and the render would
        succeed with the directive inert. Write C(cfg:) then C(output:) beneath it, not
        C(cfg.output:).
      - To select a name that really does contain a dot, escape it C(\.) as section 8 does -
        C('a\.b') is the single name C(a.b). Quoting cannot express this, since C(a.b) and
        C('a.b') load to the same string; write it plain or single-quoted, as YAML's
        double-quoted style rejects C("a\.b") as an unknown escape.
      - A dot inside a leading C(Q{...}) needs no escape. Section 11.4 makes those dots part
        of the URI, where they "do not split the qualified path", so C('Q{urn:example.com}name')
        and C('@Q{urn:example.com}x') are written as they read. The URI ends at the first
        unescaped C(}); a dot in the local name after it is ambiguous like any other.
      - A directive that takes several values is one comma-separated scalar, C(output),
        C("xml,json"). A list is refused, because section 15 wants a nonempty scalar. The
        one-document limit described under O(scheme) applies to such a declaration here too.
      - Quote a wildcard selector - bare C(*) is a YAML alias indicator. Quote anything YAML
        would read as a number, since C(3.10) arrives as C(3.1).
      - Carries the same refusals and the same O(fmt) cross-check as O(scheme). Passed to the
        tool as a JSON document, which section 15 accepts alongside YAML.
    type: dict
    version_added: 2.1.0
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
      The four issue forms. Select the component "Ansible collection" for a fault reached
      through this plugin.
    link: https://github.com/stop-cran/namespace2xml/issues/new/choose
  - name: Guide for automated agents
    description: Read order, repository map, and the rules an agent follows when reporting here.
    link: https://github.com/stop-cran/namespace2xml/blob/master/AGENTS.md
  - name: namespace2xml on NuGet
    description: The transformer this filter runs.
    link: https://www.nuget.org/packages/namespace2xml
  - module: stop_cran.namespace2xml.render
    description: >-
      The node-side module of the same name. It runs the tool on the managed node over input
      files that are already there, so the node's own files decide the node's state. Use it
      instead of this filter when the truth lives in files on the node rather than in play
      variables.
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
import sys
import tempfile
import unicodedata

# A collection plugin is a real package, so the controller-side filter can reach the same
# module_utils the node-side module uses. Everything shared -- the section 8.3 encoder, the
# scheme-mapping encoder, binary discovery, the contract-bundle gate and the runner -- lives
# there now and is imported rather than copied. See issue #107.
from ..module_utils import n2x
from ..module_utils.n2x import (
    Namespace2XmlError as _SharedError,
    encode_scheme_mapping,
    encode_value,
)

__all__ = ["render", "flatten", "encode_name_part", "encode_value", "encode_scheme_mapping"]

DEFAULT_SELECTOR = "cfg"

# Section 16.1. A filter plugin's `choices:` documentation is not enforced at runtime, so this
# list is the only thing standing between a mistyped format and a scheme directive built from it.
FORMATS = ("xml", "json", "yaml", "ini", "namespace", "quotednamespace")

_NAME_SHORT_ESCAPES = frozenset(".*=#!$@}")
_FORCED_HEX = frozenset("\u0085\u2028\u2029")

_RENDER_CACHE: dict = {}


try:  # pragma: no cover -- exercised by whichever of the two environments is running
    from ansible.errors import AnsibleFilterError as _FilterErrorBase
except ImportError:
    # Ansible is always importable where this plugin is loaded. The fallback exists so the
    # encoder can be exercised on a machine with no controller installed, which is where the
    # specification-side half of the oracle is cheapest to run.
    #
    # It has to derive from the shared error rather than be `Exception`: the shared error is
    # itself an `Exception`, so naming `Exception` as the first base of the class below would
    # put a superclass ahead of its own subclass and no consistent method resolution order
    # exists. That is an import-time TypeError -- the filter would not load at all on precisely
    # the machines this fallback is for.
    class _FilterErrorBase(_SharedError):  # type: ignore[no-redef]
        """Stand-in for ``AnsibleFilterError`` where no controller is installed."""

try:  # pragma: no cover -- as above
    from ansible.utils.display import Display

    _DISPLAY = Display()
except ImportError:
    _DISPLAY = None


class Namespace2XmlError(_FilterErrorBase, _SharedError):  # type: ignore[valid-type, misc]
    """A failure: bad input data, a tool that could not be found, or a run that did not succeed.

    Derived from ``AnsibleFilterError`` so a play reports a failed template as a filter error
    with the message attached rather than as a traceback from an unrecognised exception type,
    and from the shared error so that the one ``except`` clause in :func:`render` covers both
    this module's refusals and those raised by the shared code it now calls.
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


def _output_formats(declaration):
    """Split one ``output`` declaration into the formats it names, normalized for comparison.

    Section 16.1 makes an ``output`` declaration a comma-separated list -- "formats in one
    comma-separated declaration have a left-to-right declaration ordinal" -- and adds that
    "names are case-insensitive" and "whitespace around comma-separated values is ignored".
    Section 15 says the same thing more broadly: directive names are matched under ASCII
    case-insensitive comparison, "as is every other name and value in the scheme language:
    formats in Section 16.1".

    Comparing the declaration as raw text instead refuses spellings the tool itself accepts.
    ``output: XML`` and ``output: ' xml '`` are ``xml``, and ``output: "xml,json"`` declares
    two formats rather than one format oddly named ``xml,json`` -- which no O(fmt) value could
    ever equal, so the cross-check below would have demanded agreement that cannot be written.
    """
    return set(part.strip().lower() for part in declaration.split(",") if part.strip())


def _declared_outputs(scheme):
    """The set of formats an explicit scheme declares, for cross-checking against ``fmt``.

    Accepts either form of scheme. A mapping is walked for keys named ``output``; text is read
    deliberately little. Section 8 record kinds are honoured -- an unescaped leading ``#`` is a
    comment and an unescaped leading ``!`` is a mask, so neither declares anything -- and a name
    is split on unescaped dots so that a rule ending in a literal ``\\.output`` part is not
    mistaken for one. Everything past that is left alone: the point is to catch a plain
    disagreement, not to re-implement Section 15.2 matching here. When nothing is recognised the
    caller stays silent and lets the tool speak for itself.

    A scheme that is neither a mapping nor text is not this function's to judge. It abstains so
    that :func:`encode_scheme_mapping` reaches the argument and refuses it by name -- "a mapping
    scheme must be a mapping, not a list" -- rather than the caller meeting an ``AttributeError``
    raised here on the way past. Documented argument types are not runtime validation, so an
    ordinary playbook variable can arrive in any shape.
    """
    if isinstance(scheme, dict):
        return _declared_outputs_in_mapping(scheme)

    if not isinstance(scheme, str):
        return set()

    outputs = set()

    for line in scheme.splitlines():
        stripped = line.strip(" \t")

        if not stripped or stripped[0] in "#!":
            continue

        pieces = _split_unescaped(stripped, "=")

        if len(pieces) < 2:
            continue

        name_parts = _split_unescaped(pieces[0], ".")

        if name_parts[-1].strip().lower() == "output":
            outputs |= _output_formats("=".join(pieces[1:]))

    return outputs


def _declared_outputs_in_mapping(mapping, seen=None):
    """Walk a mapping scheme for ``output`` directives, wherever they are nested.

    The mapping form carries the path in its nesting, so there is no name to split: a key
    literally named ``output`` whose value is a scalar is the directive, at any depth.
    """
    outputs = set()
    seen = seen if seen is not None else set()

    if id(mapping) in seen:
        return outputs

    seen.add(id(mapping))

    for key, value in mapping.items():
        if isinstance(value, dict):
            outputs |= _declared_outputs_in_mapping(value, seen)
        elif (isinstance(key, str) and key.strip().lower() == "output"
              and value is not None and not isinstance(value, (list, tuple))):
            outputs |= _output_formats(
                ("true" if value else "false") if isinstance(value, bool) else str(value))

    return outputs


def _declare_hint(scheme, swallowed):
    """Spell the refused arguments the way the scheme in hand is written.

    A mapping scheme has no ``name=value`` lines, so quoting the text spelling at an author who
    wrote YAML would name a fix they cannot apply as given.
    """
    if isinstance(scheme, dict):
        return " and ".join("'%s:' nested under the selector" % name for name in swallowed)

    return " and ".join("'<selector>.%s=...'" % name for name in swallowed)


def _refuse_swallowed_arguments(scheme, fmt, root, delimiter):
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
            "succeed having ignored them. Declare them in the scheme instead, as %s."
            % (" and ".join("'%s'" % name for name in swallowed),
               _declare_hint(scheme, swallowed)))

    declared = _declared_outputs(scheme)

    if declared and fmt.strip().lower() not in declared:
        raise Namespace2XmlError(
            "the scheme declares output %s, but the filter was asked for '%s'. The format "
            "argument is not applied on top of an explicit scheme, so one of the two is a "
            "mistake rather than a refinement of the other; make them agree."
            % (" and ".join(sorted("'%s'" % value for value in declared)), fmt))


def render(
    config,
    fmt,
    scheme=None,
    scheme_yaml=None,
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
    :param scheme_yaml: the same thing written as a mapping, where the nesting carries the path
        rather than a dot. Mutually exclusive with ``scheme``.
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
    # This is the collection's one filter, so it is the one place a refusal from the shared
    # code can reach a play. module_utils cannot import ansible -- it ships to the node -- so
    # its errors are not `AnsibleFilterError` and would surface as an unrecognised exception
    # type with a traceback instead of a message. Restating them here, once, is what keeps a
    # missing binary reading as a failed task rather than as a bug in this collection.
    try:
        return _render(config, fmt, scheme, scheme_yaml, root, selector, delimiter, tool,
                       memoize, workdir)
    except Namespace2XmlError:
        raise
    except _SharedError as error:
        raise Namespace2XmlError(str(error)) from error


def _render(config, fmt, scheme, scheme_yaml, root, selector, delimiter, tool, memoize,
            workdir):
    """Do the work of :func:`render`, raising either error class."""
    profile = flatten(config, selector)

    if scheme is not None and scheme_yaml is not None:
        raise Namespace2XmlError(
            "'scheme' and 'scheme_yaml' are two spellings of one argument, so supplying both "
            "leaves it ambiguous which the render should use. Keep the one you mean.")

    explicit = scheme if scheme is not None else scheme_yaml

    if explicit is not None:
        _refuse_swallowed_arguments(explicit, fmt, root, delimiter)

    if scheme_yaml is not None:
        # Section 15 picks the parser from the extension, so this has to reach the tool under a
        # .json name and the text form must not.
        scheme_text, scheme_name = encode_scheme_mapping(scheme_yaml), "scheme.json"
    elif scheme is not None:
        scheme_text, scheme_name = scheme, "scheme.txt"
    else:
        scheme_text, scheme_name = synthesize_scheme(
            fmt, selector, root, delimiter), "scheme.txt"

    identity = n2x.tool_identity(tool)
    key = None

    if memoize:
        key = _cache_key(profile, scheme_text, scheme_name, fmt, identity)

        if key in _RENDER_CACHE:
            return _RENDER_CACHE[key]

    text = _marshal_and_run(profile, scheme_text, scheme_name, n2x.resolve(tool), workdir)

    if key is not None:
        _RENDER_CACHE[key] = text

    return text


def _cache_key(profile, scheme_text, scheme_name, fmt, identity):
    digest = hashlib.sha256()

    # The file name is part of the key because it selects the parser: identical bytes read as a
    # namespace profile and as JSON are two different schemes, and must not share a cache entry.
    for part in (profile, scheme_text, scheme_name, fmt, identity):
        digest.update(part.encode("utf-8"))
        digest.update(b"\x00")

    return digest.hexdigest()


def _marshal_and_run(profile, scheme_text, scheme_name, executable, workdir):
    """Write the inputs, run the tool, read the single output back, and clean up.

    A filter has data in memory and the CLI is file-in, directory-out, so every call pays a
    directory, two writes, a process, a read and a delete.
    """
    directory = tempfile.mkdtemp(prefix="n2x-", dir=workdir)

    try:
        input_path = os.path.join(directory, "input.txt")
        scheme_path = os.path.join(directory, scheme_name)
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
    # `run_tool` raises on a non-zero exit with the tool's own diagnostics folded into the
    # message, and returns stderr on success so a diagnostic about a rule that matched nothing
    # is not swallowed. Both halves of that are the shared behaviour; only the reading back of
    # a single file below is the filter's own.
    _warn(n2x.run_tool(executable, ["-i", input_path, "-s", scheme_path, "-o", output_dir]))

    # "dummy", not "_": ansible-test's pylint profile lists "_" in bad-names, so the
    # conventional Python throwaway fails collection sanity.
    produced = sorted(
        os.path.join(base, name)
        for base, dummy, names in os.walk(output_dir)
        for name in names)

    if len(produced) != 1:
        # A filter returns one document. A scheme that asks for several -- several formats in one
        # 'output' declaration, or several 'filename' targets -- has no single answer to return,
        # and one that asks for none has nothing to return. Naming both causes keeps a reader
        # from taking their own scheme's shape for a defect in this collection.
        raise Namespace2XmlError(
            "expected exactly one output file, got %d%s. A filter returns one document, so a "
            "scheme that produces a set of files has no single result to hand back: section "
            "16.1 reads 'output: xml,json' as two formats and writes one file for each, "
            "several 'filename' targets do the same, and 'output: ignore' writes none. Narrow "
            "the scheme, or use the 'stop_cran.namespace2xml.render' module, which publishes "
            "every produced file to the node.%s"
            % (len(produced),
               (": " + ", ".join(os.path.basename(path) for path in produced)) if produced else "",
               n2x.support_hint(executable)))

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
