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
  convention:
    description:
      - How a mapping key in O(_input) is read.
      - V(escaped), the default, reads a key as data. Every section 8.2 and 11.4 marker in it is
        escaped, so any key at all round-trips as the name it spells and no key can address an
        attribute, a namespace or a content node.
      - V(xmltodict) reads the four markers section 11.4 defines, in the spelling the ecosystem
        already uses for XML-shaped mappings - C(xmltodict), C(community.general.to_xml) and the
        badgerfish style all write attributes this way. C(@x) is an attribute, C(Q{uri}x) is an
        element in a namespace, C(@Q{uri}x) is a namespaced attribute, C(#0) and C(#1) are
        content nodes, and C(#text) is an element's own text.
      - Escaping is not lost under V(xmltodict), only moved. A name part that really does begin
        with C(@), C(#) or C(Q) is written with a backslash before it - C(\@x) is the one name
        part C(@x). A leading backslash escapes itself the same way, so C(\\@x) is the name
        C(\@x) and nothing has become unwritable. This is the same move O(scheme_yaml) makes for
        a literal dot, and the same one section 8 makes for C(*).
      - Under V(xmltodict), C(#text) is refused beside child-element or content keys. Section
        11.4 puts an element's own text at the element's path only while it has no child
        elements; once it has any, the element is mixed and every content node takes an ordered
        part. A mapping does not record where the text stood, so write C(#0) and C(#1)
        explicitly for mixed content.
      - The dots inside C(Q{...}) belong to the URI and need no escaping, matching O(scheme_yaml).
        A literal C(}) or backslash inside the URI is written C(\}) or C(\\), as section 11.4
        spells it.
    type: str
    default: escaped
    choices:
      - escaped
      - xmltodict
    version_added: 2.1.0
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
      - >-
        A mismatch is not reported. The scheme's directives simply address nothing, and the
        render succeeds with the data absent from its result. The same applies to an
        O(inputs[].data) entry, which is flattened under this name too.
    type: str
    default: cfg
  inputs:
    description:
      - Further inputs, layered underneath O(_input). Each entry becomes its own C(--input)
        file, in the order written, and O(_input) is passed last.
      - Section 7.3 merges sources in command-line order and section 17.1 gives the later
        contribution precedence, so anything stated here is a base that O(_input) may override.
        That is the topology the tool was built for, where an application ships a pristine
        template and the play states the handful of things that differ on this host.
      - An entry is either a B(string), naming a file on the controller, or a B(mapping)
        carrying exactly one of O(inputs[].file), O(inputs[].text) or O(inputs[].data).
      - A file is handed to the tool B(where it lies). It is neither copied nor read into the
        play, so a large template costs nothing here, and the path the tool quotes in its own
        diagnostics is the path you wrote.
      - A Jinja C(combine) is not a substitute for this. It merges Python dictionaries by
        Ansible's rules, before the tool sees anything; these are separate sources merged by the
        tool under sections 7 and 17, which is where sequence ordering values, C(merge=append)
        and XML node kinds are decided. The two do not agree, and only one of them is the
        specification.
    type: list
    elements: raw
    default: []
    suboptions:
      file:
        description:
          - A path on the controller, passed to the tool unread. C(~) is expanded.
          - Writing the string on its own is the same thing - V(/etc/app/base.txt) is the entry
            whose C(file) is V(/etc/app/base.txt).
          - Section 7.1 selects the parser from the extension, so O(inputs[].format) is not
            read here; rename the file, or read it in and use O(inputs[].text), to parse it as
            something its extension does not say.
          - Mutually exclusive with O(inputs[].text) and O(inputs[].data).
        type: path
        version_added: 3.0.0
      text:
        description:
          - The input as a document in the tool's own syntax, written out verbatim and parsed as
            O(inputs[].format).
          - Mutually exclusive with O(inputs[].file) and O(inputs[].data).
        type: str
      data:
        description:
          - The input as an Ansible data structure, flattened into profile text exactly as
            O(_input) is and under the same O(selector) and O(convention).
          - Mutually exclusive with O(inputs[].file) and O(inputs[].text).
        type: raw
      format:
        description:
          - How O(inputs[].text) is parsed. Section 7.1 selects the parser from the file
            extension, and this chooses the extension the text is written under.
          - Meaningful only with O(inputs[].text). Setting it alongside O(inputs[].data) or
            O(inputs[].file) is refused rather than ignored - C(data) has already been
            flattened to the tool's own syntax by the time the tool sees it, and a C(file)
            carries its own extension.
          - Section 7.1's support matrix lists INI and shell as output formats only, so neither
            is offered here.
        type: str
        default: namespace
        choices:
          - namespace
          - json
          - yaml
          - xml
  scheme:
    description:
      - The scheme, used instead of the minimal scheme the filter would synthesize.
      - Needed for anything the synthesized scheme does not cover, such as C(type),
        C(substitute) or C(merge) rules. The selector it declares must equal O(selector).
      - A B(list of entries), each either a string naming a scheme file on the controller or a
        mapping carrying exactly one of O(scheme[].file), O(scheme[].text) or O(scheme[].data).
      - Several entries become several C(--scheme) arguments in the order written. They
        B(concatenate) rather than compete - section 15.2 resolves declarations in source order
        across all of them - so a shared scheme file with a small per-host override layered on
        top is the shape this exists for.
      - "B(Changed in 3.0.0): this argument was a single block of scheme text. A bare string is
        now a B(file path), matching C(scheme) on the C(stop_cran.namespace2xml.render) module.
        Text is refused with a message naming both fixes rather than being written to a file
        called after its own first line."
      - O(root) and O(delimiter) are refused alongside it, because they are read only while
        synthesizing a scheme and would otherwise be discarded without a word. Declare them in
        the scheme instead.
      - O(fmt) is still required, and is checked against what the scheme really produces. A
        disagreement is an error rather than an override, because the scheme wins and the
        argument would otherwise be a value the caller was compelled to supply and the filter
        then ignored. Names and formats are compared case-insensitively and a comma-separated
        C(output) is read as the set it declares, following sections 15 and 16.1.
      - Where the scheme carries several C(output) declarations, or one written as a section
        15.1 C(${...}) reference, which of them applies is a precedence question that only the
        tool can answer. The filter therefore renders a second time with C(<selector>.output)
        set to O(fmt) - section 15.2 gives that last declaration precedence - and compares the
        two results. This costs one extra invocation of the tool, and only for schemes of that
        shape; a single literal C(output) is settled without it. A scheme file this filter
        cannot read - one named C(.json), C(.yaml) or C(.yml), or one that cannot be opened -
        is the same question and is settled the same way.
      - A filter returns one document, so a scheme that produces a set of files - several
        formats in one C(output) declaration, or several C(filename) targets - has no single
        result to hand back and the render fails. Use the C(stop_cran.namespace2xml.render)
        module when the scheme is meant to produce several files.
      - Mutually exclusive with O(scheme_text) and O(scheme_yaml), which are this same argument
        under the names 2.x gave it.
    type: list
    elements: raw
    suboptions:
      file:
        description:
          - A path on the controller, passed to the tool unread. C(~) is expanded.
          - Writing the string on its own is the same thing.
          - Section 7.1 lets the tool parse a scheme by extension, so a C(.json), C(.yaml) or
            C(.yml) scheme is accepted and handed over - this filter simply does not read it
            itself, and settles the O(fmt) check by asking the tool instead.
          - Mutually exclusive with O(scheme[].text) and O(scheme[].data).
        type: path
        version_added: 3.0.0
      text:
        description:
          - The scheme as a block of text in the tool's own syntax.
          - Mutually exclusive with O(scheme[].file) and O(scheme[].data).
        type: str
        version_added: 3.0.0
      data:
        description:
          - The scheme as a native mapping, so it reads as part of the playbook rather than as
            an embedded document. Encoded to JSON, which section 15 accepts alongside YAML.
          - B(The nesting carries the path.) Section 9 makes a mapping key one name part, so a
            dot does not separate names here as it does in O(scheme[].text) - a key containing
            a dot asks for one name with a literal dot in it, and is refused rather than passed
            through, because as a selector it would draw only C(WARN009) and the render would
            succeed with the directive inert. Write C(cfg:) then C(output:) beneath it, not
            C(cfg.output:). Quoting does not change this - C(a.b) and C('a.b') load to the same
            string.
          - To select a name that really does contain a dot, escape it C(\.) as section 8 does -
            C('a\.b') is the single name C(a.b). Write it plain or single-quoted, as YAML's
            double-quoted style rejects C("a\.b") as an unknown escape.
          - A dot inside a leading C(Q{...}) needs no escape. Section 11.4 makes those dots part
            of the URI, where they "do not split the qualified path", so C('Q{urn:example.com}name')
            and C('@Q{urn:example.com}x') are written as they read. The URI ends at the first
            unescaped C(}); a dot in the local name after it is ambiguous like any other.
          - A directive that takes several values is one comma-separated scalar, C(output),
            C("xml,json"). A list is refused, because section 15 wants a nonempty scalar.
          - Quote a wildcard selector - bare C(*) is a YAML alias indicator. Quote anything YAML
            would read as a number, since C(3.10) arrives as C(3.1).
          - Mutually exclusive with O(scheme[].file) and O(scheme[].text).
        type: dict
        version_added: 3.0.0
  scheme_text:
    description:
      - Deprecated. Write the same text as a O(scheme) entry's C(text) instead.
      - Kept working for plays written against 2.3.0, where this was the only spelling the
        filter and the module shared. It carries every refusal and every check described under
        O(scheme), and is refused alongside it.
    type: str
    version_added: 2.3.0
  scheme_yaml:
    description:
      - Deprecated. Write the same mapping as a O(scheme) entry's C(data) instead.
      - Kept working for plays written against 2.2.0. Every note under O(scheme[].data) about
        dots, wildcards and comma-separated values applies here unchanged.
      - Refused alongside O(scheme) and O(scheme_text).
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
      - Reuse the result of an identical earlier render within the same worker process.
      - The cache key covers the whole marshalled input plus the tool's version and contract
        revision, so it cannot survive a tool or contract change. Set to V(false) to force
        every call to spawn the tool.
      - A memoized call does not re-run the tool, so any warning the tool reports is shown
        once for a given input rather than once per call.
      - B(The caches are per worker, not per run.) C(ansible-playbook) forks a fresh worker for
        every (host, task) pair, and every cache in this filter - the memoized render, the
        resolved binary path and the tool's contract identity - dies with it. Measured on
        ansible-core 2.17 under the C(linear) strategy - five identical renders in one task on
        one host cost one binary lookup, one C(--version) probe and one render subprocess; the
        same five across eight hosts cost eight of each; one render in each of three tasks
        across eight hosts costs twenty-four of each. The floor is one probe plus one render
        per (host, task). Rendering several documents in one task keeps you at that floor;
        spreading them over tasks does not.
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
  - Under the default C(escaped) convention, XML attributes, content tokens and
    namespace-qualified element names cannot be addressed. Every key is escaped so that it reads
    back as itself, so a C(@name) key becomes a literal element named C(\@name), which is not an
    C(NCName) and is reported as a blocking C(XML002) rather than silently producing a wrong
    document. Pass O(convention=xmltodict) to read C(@), C(Q{...}) and C(#) as section 11.4
    addressing instead. See the collection README.
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
      code arriving from this filter, such as C(XML002) or C(WARN009), is looked up here. A
      copy is installed alongside this plugin at C(docs/diagnostics.md) inside the collection,
      so it is readable with no network access.
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
    scheme:
      - text: |
          cfg.output=ini
          cfg.*.version.type=string

- name: Render an XML-shaped mapping, with attributes and a namespace
  ansible.builtin.copy:
    content: "{{ doc | stop_cran.namespace2xml.render('xml', root='beans', convention='xmltodict') }}"
    dest: /opt/app/beans.xml
    mode: "0644"
  vars:
    doc:
      bean:
        '@id': dataSource
        '@class': com.example.Pool
        'Q{urn:example.com/spring}property':
          '@name': url
          '#text': jdbc:postgresql://db/app

- name: Render with a tool built somewhere the default search would not find
  ansible.builtin.debug:
    msg: "{{ data | stop_cran.namespace2xml.render('yaml', tool='/opt/n2x/namespace2xml') }}"

- name: Layer host overrides over a template the application ships
  ansible.builtin.copy:
    content: "{{ overrides | stop_cran.namespace2xml.render('xml', root='configuration', inputs=layers) }}"
    dest: /opt/app/app.xml
    mode: "0644"
  vars:
    layers:
      # Named, not read: the file goes to the tool where it lies, so its size costs the play
      # nothing and the tool's own diagnostics quote a path you can go and look at.
      - files/app-defaults.txt
      - files/{{ app_tier }}.json
    overrides:
      server:
        port: "{{ app_port }}"

- name: Layer a structure rather than a document, flattened the same way the piped data is
  ansible.builtin.debug:
    msg: "{{ host_vars_slice | stop_cran.namespace2xml.render('json', inputs=[{'data': group_defaults}]) }}"

- name: Mix files, a document and a structure in one render, in the order written
  ansible.builtin.debug:
    msg: "{{ overrides | stop_cran.namespace2xml.render('json', inputs=layers) }}"
  vars:
    layers:
      - /etc/app/base.txt
      - text: |
          cfg.server.port=8080
      - data: "{{ group_defaults }}"

- name: Layer a per-host override onto a shared scheme file
  ansible.builtin.copy:
    content: "{{ settings | stop_cran.namespace2xml.render('xml', scheme=schemes) }}"
    dest: /opt/app/app.xml
    mode: "0644"
  vars:
    schemes:
      # Section 15.2 resolves declarations in source order across every --scheme, so the later
      # entry adds to the shared one rather than replacing it.
      - files/scheme-common.txt
      - data:
          cfg:
            root: configuration
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
from ..module_utils.profile import (
    DEFAULT_CONVENTION,
    DEFAULT_SELECTOR,
    encode_name_part,
    encode_xml_name_part,
    flatten,
)
from ..module_utils.entries import (
    INPUT_FORMATS,
    SCHEME_FORMATS,
    Entry,
    marshal_inputs,
    marshal_schemes,
)

__all__ = ["render", "flatten", "encode_name_part", "encode_xml_name_part", "encode_value",
           "encode_scheme_mapping"]


# Section 16.1. A filter plugin's `choices:` documentation is not enforced at runtime, so this
# list is the only thing standing between a mistyped format and a scheme directive built from it.
FORMATS = ("xml", "json", "yaml", "ini", "namespace", "quotednamespace")


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

    It stays with the scheme rather than moving to the shared encoder with the rest of Section
    8.3: what it guards is raw interpolation into a generated scheme directive, which is a
    property of :func:`synthesize_scheme` below -- its only caller -- and not of the profile
    encoding.
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


def _output_declarations(scheme):
    """Every ``output`` declaration an explicit scheme makes, in source order, still unparsed.

    Source order is the whole point. Section 15.2 gives the last matching declaration precedence
    with no specificity ranking, so the *set* of formats a scheme mentions cannot say which one
    of them wins -- and a check built on that set accepts a call whose answer will be a different
    format than the one asked for. The values come back exactly as written, because whether one
    is a literal or a Section 15.1 reference decides whether this file may read it at all.

    Accepts either form of scheme. A mapping is walked for keys named ``output``; text is read
    deliberately little. Section 8 record kinds are honoured -- an unescaped leading ``#`` is a
    comment and an unescaped leading ``!`` is a mask, so neither declares anything -- and a name
    is split on unescaped dots so that a rule ending in a literal ``\\.output`` part is not
    mistaken for one. Everything past that is left alone: the point is to find the declarations,
    not to re-implement Section 15.2 matching here. When nothing is recognised the caller stays
    silent and lets the tool speak for itself.

    A scheme that is neither a mapping nor text is not this function's to judge. It abstains so
    that :func:`encode_scheme_mapping` reaches the argument and refuses it by name -- "a mapping
    scheme must be a mapping, not a list" -- rather than the caller meeting an ``AttributeError``
    raised here on the way past. Documented argument types are not runtime validation, so an
    ordinary playbook variable can arrive in any shape.
    """
    if isinstance(scheme, dict):
        return _output_declarations_in_mapping(scheme)

    if not isinstance(scheme, str):
        return []

    declarations = []

    for line in scheme.splitlines():
        stripped = line.strip(" \t")

        if not stripped or stripped[0] in "#!":
            continue

        pieces = _split_unescaped(stripped, "=")

        if len(pieces) < 2:
            continue

        name_parts = _split_unescaped(pieces[0], ".")

        if name_parts[-1].strip().lower() == "output":
            declarations.append("=".join(pieces[1:]))

    return declarations


def _declared_outputs(scheme):
    """Every format an explicit scheme names anywhere, without regard to which one wins."""
    outputs = set()

    for declaration in _output_declarations(scheme):
        outputs |= _output_formats(declaration)

    return outputs


def _output_declarations_in_mapping(mapping, seen=None):
    """Walk a mapping scheme for ``output`` directives, wherever they are nested.

    The mapping form carries the path in its nesting, so there is no name to split: a key
    literally named ``output`` whose value is a scalar is the directive, at any depth. Mapping
    order is source order, because :func:`encode_scheme_mapping` writes the members out in the
    order they were given, so the declarations come back in the order Section 15.2 resolves them.
    """
    declarations = []
    seen = seen if seen is not None else set()

    if id(mapping) in seen:
        return declarations

    seen.add(id(mapping))

    for key, value in mapping.items():
        if isinstance(value, dict):
            declarations += _output_declarations_in_mapping(value, seen)
        elif (isinstance(key, str) and key.strip().lower() == "output"
              and value is not None and not isinstance(value, (list, tuple))):
            declarations.append(
                ("true" if value else "false") if isinstance(value, bool) else str(value))

    return declarations


def _declare_hint(sources, swallowed):
    """Spell the refused arguments the way the scheme in hand is written.

    A mapping scheme has no ``name=value`` lines, so quoting the text spelling at an author who
    wrote YAML would name a fix they cannot apply as given.

    With several schemes in play the hint follows the last one that can be read, because
    Section 15.2 gives the last matching declaration precedence: a directive added to any
    earlier scheme could be displaced by a later one, so that is where it has to go.
    """
    latest = next((source for source in reversed(sources)
                   if isinstance(source, (dict, str))), None)

    if isinstance(latest, dict):
        return " and ".join("'%s:' nested under the selector" % name for name in swallowed)

    return " and ".join("'<selector>.%s=...'" % name for name in swallowed)


def _has_reference(declaration):
    """Whether a declaration contains an unescaped Section 15.1 reference.

    ``\\${`` is an escaped dollar-brace and stands for itself, so counting the backslashes in
    front matters: an odd run escapes the marker and an even run leaves it live.
    """
    index = declaration.find("${")

    while index != -1:
        backslashes = 0
        behind = index - 1

        while behind >= 0 and declaration[behind] == "\\":
            backslashes += 1
            behind -= 1

        if backslashes % 2 == 0:
            return True

        index = declaration.find("${", index + 2)

    return False


def _refuse_swallowed_arguments(sources, fmt, root, delimiter, spelling="scheme"):
    """Refuse arguments that an explicit scheme would silently discard.

    ``root`` and ``delimiter`` are read only while synthesizing a scheme. Passed alongside one,
    they reach nothing -- and the render then succeeds, returning a document that ignored them.
    That is the failure this collection exists to avoid, so it is made loud.

    ``fmt`` is worse, because it is a required positional: before this check the API compelled
    every custom-scheme caller to name a format and then ignored the answer.

    What is refused here is only what can be settled without running anything: a scheme that
    never names ``fmt`` at all cannot produce it, whichever declaration wins. Whether a scheme
    that *does* name it will actually produce it is a Section 15.2 precedence question, and this
    file does not answer those -- :func:`_format_probe` puts it to the tool instead. The two
    together are what the format argument is worth; this half alone was the defect in #111,
    because membership of the set of mentioned formats was being reported as agreement.

    A declaration carrying a reference is not read at all. Section 15.1 resolves those inside the
    tool, so comparing the unresolved text would refuse a scheme whose ``output`` is perfectly
    valid -- the mirror of the same defect, and the reason the guard below stands down entirely
    when one is present.

    ``sources`` is the ordered list of schemes, each already reduced to something readable --
    a mapping, its text, or ``None`` for one that could not be read. Their declarations are
    concatenated in order, which is what the tool does with ordered ``--scheme`` paths.
    """
    swallowed = [name for name, value in (("root", root), ("delimiter", delimiter))
                 if value is not None]

    if swallowed:
        raise Namespace2XmlError(
            "%s cannot be combined with an explicit '%s'. Those arguments are read only "
            "while synthesizing a scheme, so here they would be discarded and the render would "
            "succeed having ignored them. Declare them in the scheme instead, as %s."
            % (" and ".join("'%s'" % name for name in swallowed), spelling,
               _declare_hint(sources, swallowed)))

    declarations = _all_declarations(sources)

    if any(_has_reference(declaration) for declaration in declarations):
        return

    # A scheme nobody here could read may declare anything at all, so the set below is not the
    # whole set and cannot be used to refuse. Silence is the only honest answer.
    if any(source is None for source in sources):
        return

    declared = set()

    for declaration in declarations:
        declared |= _output_formats(declaration)

    if declared and fmt.strip().lower() not in declared:
        raise Namespace2XmlError(
            "the scheme declares output %s, but the filter was asked for '%s'. The format "
            "argument is not applied on top of an explicit scheme, so one of the two is a "
            "mistake rather than a refinement of the other; make them agree."
            % (" and ".join(sorted("'%s'" % value for value in declared)), fmt))


def _all_declarations(sources):
    """Every ``output`` declaration the ordered schemes make, concatenated in their order.

    Section 15.2 resolves declarations in source order across every ``--scheme`` given, so the
    schemes concatenate rather than compete: reading them in order here is reading what the tool
    will read. A source that could not be read contributes nothing and is caught separately.
    """
    return [declaration
            for source in sources
            for declaration in _output_declarations(source)]


def _format_probe(sources, fmt, selector):
    """The scheme that asks the tool outright for ``fmt``, or ``None`` when nothing need be asked.

    Section 15.2 gives the last matching declaration precedence, and ``--scheme`` takes ordered
    paths, so a one-line scheme file passed after the caller's own overrides whatever it said.
    Rendering twice and comparing the results therefore answers "would this scheme have produced
    what was asked for" without a second implementation of Section 15.2 living here -- the tool
    resolves the precedence both times. That matters more than it sounds: #107 was one duplicate
    of specification logic drifting from the original, and this would have been another.

    Most renders never pay for it. A single literal declaration cannot be displaced by anything
    -- there is nothing after it -- so the cheap comparison above is already conclusive and this
    returns ``None``. What is left is exactly the three shapes that comparison gets wrong:
    several declarations competing, where the set says one thing and precedence says another; a
    reference, which only the tool can resolve; and a scheme none of this could read, where the
    cheap comparison never happened at all and the probe is the only check left.
    """
    declarations = _all_declarations(sources)
    unreadable = any(source is None for source in sources)

    if not unreadable and len(declarations) < 2 and not any(
            _has_reference(item) for item in declarations):
        return None

    wanted = fmt.strip().lower()

    if wanted not in FORMATS:
        raise Namespace2XmlError(
            "'%s' is not one of the section 16.1 output formats. Use one of: %s."
            % (fmt, ", ".join(FORMATS)))

    return "%s.output=%s\n" % (encode_name_part(selector), wanted)


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
    convention=DEFAULT_CONVENTION,
    scheme_text=None,
    inputs=None,
):
    """Render a dictionary as configuration text in ``fmt``.

    :param config: the data to render.
    :param fmt: one of the section 16.1 output formats -- ``xml``, ``json``, ``yaml``, ``ini``,
        ``namespace``, ``quotednamespace``.
    :param scheme: the scheme, as an ordered list of entries. Every entry is a string naming a
        file, or a mapping carrying one of ``file``, ``text`` (a document in the tool's own
        syntax) or ``data`` (a mapping, where the nesting carries the path rather than a dot),
        and optionally ``format``. Section 15.2 gives the last matching declaration precedence,
        so a later entry overrides an earlier one and a small override may be layered onto a
        shared scheme file. Passing text directly as a string is refused: under this shape a
        bare string names a file.
    :param scheme_yaml: deprecated. ``[{data: ...}]`` in ``scheme``.
    :param root: the section 16.3 root, which XML needs for a multi-member view.
    :param selector: the top-level name the data is written under.
    :param delimiter: the section 16.4 output delimiter, for the flat formats.
    :param tool: path to the binary. Defaults to ``$NAMESPACE2XML``, then ``PATH``, then the
        dotnet global-tools directory. An explicit value must resolve: it is never fallen back
        on.
    :param memoize: reuse a previous identical render. The key is the whole marshalled input
        plus the tool's contract identity, so it cannot survive a tool or contract change.
    :param workdir: parent directory for the temporary marshalling directory.
    :param convention: how a mapping key is read -- ``escaped``, the default, where every
        section 11.4 marker in a key is escaped and the key means itself, or ``xmltodict``,
        where ``@x``, ``Q{uri}x``, ``#n`` and ``#text`` address an attribute, a namespaced
        element, a content node and an element's own text.
    :param scheme_text: deprecated. ``[{text: ...}]`` in ``scheme``.
    :param inputs: further inputs layered *under* ``config``, each becoming its own ``-i`` file
        in the order given, in the same entry shape as ``scheme``. Section 7.3 merges in
        command-line order, so ``config`` goes last and wins.
    :returns: the rendered text.
    """
    # This is the collection's one filter, so it is the one place a refusal from the shared
    # code can reach a play. module_utils cannot import ansible -- it ships to the node -- so
    # its errors are not `AnsibleFilterError` and would surface as an unrecognised exception
    # type with a traceback instead of a message. Restating them here, once, is what keeps a
    # missing binary reading as a failed task rather than as a bug in this collection.
    try:
        return _render(config, fmt, scheme, scheme_yaml, root, selector, delimiter, tool,
                       memoize, workdir, convention, scheme_text, inputs)
    except Namespace2XmlError:
        raise
    except _SharedError as error:
        raise Namespace2XmlError(str(error)) from error


def _scheme_entries(scheme, scheme_text, scheme_yaml):
    """Collapse the three spellings of the scheme argument into one ordered entry list.

    ``scheme_text`` and ``scheme_yaml`` are what 2.x offered before every source took the same
    shape: one for inline text, because ``scheme`` there was already taken by something else on
    the module, and one for a mapping. Both are now that shape written the long way round, so
    they are kept working and mapped onto it rather than reimplemented. Supplying more than one
    is refused, and the name the caller wrote is carried forward for every later message to
    quote back -- being told to fix ``'scheme'`` when you wrote ``scheme_text`` sends you
    looking for an argument that is not in your playbook.
    """
    supplied = [(name, value) for name, value in (("scheme", scheme),
                                                  ("scheme_text", scheme_text),
                                                  ("scheme_yaml", scheme_yaml))
                if value is not None]

    if len(supplied) > 1:
        raise Namespace2XmlError(
            "%s are spellings of one argument, so supplying more than one leaves it ambiguous "
            "which the render should use. Keep the one you mean -- 'scheme' takes them all now, "
            "as a list of entries."
            % " and ".join("'%s'" % name for name, dummy in supplied))

    if not supplied:
        return [], "scheme"

    name, value = supplied[0]

    if name == "scheme_text":
        return [{"text": value}], "scheme_text"

    if name == "scheme_yaml":
        return [{"data": value}], "scheme_yaml"

    # The one deliberate break in this reshape. Before it, a bare string here was inline scheme
    # text; after it, a bare string in any source list names a file. Guessing between the two
    # would be the worst of the three options -- a render that reads the caller's scheme as a
    # path, or their path as a document, and says nothing either way.
    if isinstance(value, str):
        raise Namespace2XmlError(
            "'scheme' is a string. Every source is a list of entries now, and a bare string in "
            "one names a file, so inline text can no longer be told apart from a path here. "
            "Write scheme=[{'text': ...}] for a document you have inline, or scheme=[<path>] "
            "for a file that holds one.")

    return value, "scheme"


def _scheme_sources(written, schemes):
    """What each scheme says, in a form the checks above can read.

    A ``data`` entry is read as the mapping it was written as rather than as the JSON it was
    encoded into, because the mapping walk understands nesting and the line-oriented reader
    would find nothing in JSON.

    A ``file`` entry in the tool's own text syntax is read from disk: the filter runs on the
    controller, where the file is, and the tool is about to read the same bytes. Anything else
    -- a file that cannot be read, or one whose extension says JSON or YAML, which the reader
    below does not speak -- comes back as ``None``. That is not the same as a scheme declaring
    nothing: :func:`_refuse_swallowed_arguments` stands down when it sees one, and
    :func:`_format_probe` runs instead, which settles the question by asking the tool.
    """
    sources = []

    for original, entry in zip(written, schemes):
        if isinstance(original, dict) and "data" in original:
            sources.append(original["data"])
        elif entry.text is not None:
            sources.append(entry.text)
        else:
            sources.append(_read_scheme(entry.path))

    return sources


def _read_scheme(path):
    """A scheme file's text, or ``None`` when this file has no business reading it."""
    if os.path.splitext(path)[1].lower() in (".json", ".yaml", ".yml"):
        return None

    try:
        with open(path, "r", encoding="utf-8") as handle:
            return handle.read()
    except (OSError, UnicodeDecodeError):
        # The tool is about to open the same path and say so far better than a second diagnostic
        # invented here, which would name this filter as the thing that failed.
        return None


def _render(config, fmt, scheme, scheme_yaml, root, selector, delimiter, tool, memoize,
            workdir, convention, scheme_text=None, inputs=None):
    """Do the work of :func:`render`, raising either error class."""
    written, spelling = _scheme_entries(scheme, scheme_text, scheme_yaml)

    # Section 7.3 merges sources in command-line order and section 17.1 gives the later
    # contribution precedence, so the piped data is written last and overrides everything
    # layered beneath it. The extras keep numbered names and the piped one keeps the name it
    # has always had, which is the one quoted in every diagnostic written before this option
    # existed.
    layered = marshal_inputs(inputs, selector, convention)
    layered.append(Entry(path=None, text=flatten(config, selector, convention),
                         name="input.txt"))

    schemes = marshal_schemes(written, spelling)
    probe = None

    if schemes:
        sources = _scheme_sources(written, schemes)
        _refuse_swallowed_arguments(sources, fmt, root, delimiter, spelling)
        probe = _format_probe(sources, fmt, selector)
    else:
        schemes = [Entry(path=None, name="scheme.txt",
                         text=synthesize_scheme(fmt, selector, root, delimiter))]

    identity = n2x.tool_identity(tool)
    key = None

    if memoize:
        key = _cache_key(layered, schemes, fmt, identity)

        if key in _RENDER_CACHE:
            return _RENDER_CACHE[key]

    text = _marshal_and_run(layered, schemes, n2x.resolve(tool), workdir, probe, fmt)

    if key is not None:
        _RENDER_CACHE[key] = text

    return text


def _cache_key(layered, schemes, fmt, identity):
    """A key for this exact render, or ``None`` when one cannot be built soundly.

    The cache lives for a single ``ansible-playbook`` process, and a play is perfectly entitled
    to write a scheme with ``template`` and render against it twice in the same run. A file
    entry is therefore keyed by what would have to change for its content to differ -- its
    path, its size and its modification time -- rather than by its path alone, which would
    serve the first render's answer to the second.
    """
    digest = hashlib.sha256()

    for group in (layered, schemes):
        # The count is hashed before the entries so that no regrouping of the same bytes across
        # a different number of files can collide: two sources concatenate under section 7.3,
        # one does not, and the two renders are not interchangeable.
        digest.update(("%d" % len(group)).encode("utf-8"))
        digest.update(b"\x00")

        for entry in group:
            # The file name is part of the key because it selects the parser: identical bytes
            # read as a namespace profile and as JSON are two different sources, and must not
            # share a cache entry.
            stamp = entry.text if entry.path is None else _file_stamp(entry.path)

            if stamp is None:
                return None

            for part in (entry.name, stamp):
                digest.update(part.encode("utf-8"))
                digest.update(b"\x00")

    for part in (fmt, identity):
        digest.update(part.encode("utf-8"))
        digest.update(b"\x00")

    return digest.hexdigest()


def _file_stamp(path):
    """What would have to change for a file's content to differ, or ``None`` if unknowable."""
    try:
        status = os.stat(path)
    except OSError:
        return None

    return "size=%d mtime=%d" % (status.st_size, status.st_mtime_ns)


def _marshal_and_run(layered, schemes, executable, workdir, probe=None, fmt=None):
    """Write the inline sources, run the tool, read the single output back, and clean up.

    A filter has data in memory and the CLI is file-in, directory-out, so every call pays a
    directory, a write per inline source, a process, a read and a delete. An entry that already
    names a file is handed over where it lies -- copying it would change the path the tool
    quotes in its own diagnostics into one that is deleted before anyone can go and look.
    """
    directory = tempfile.mkdtemp(prefix="n2x-", dir=workdir)

    try:
        input_paths = [_materialize(entry, directory) for entry in layered]
        scheme_paths = [_materialize(entry, directory) for entry in schemes]
        output_dir = os.path.join(directory, "out")

        os.mkdir(output_dir)

        text = _run_and_read(executable, input_paths, scheme_paths, output_dir)

        if probe is not None:
            _confirm_the_format_asked_for(executable, input_paths, scheme_paths, output_dir,
                                          directory, probe, fmt)

        return text
    finally:
        shutil.rmtree(directory, ignore_errors=True)


def _materialize(entry, directory):
    """The path the tool should read for one entry, writing it out first when it is content."""
    if entry.path is not None:
        return entry.path

    path = os.path.join(directory, entry.name)
    _write(path, entry.text)

    return path


def _fingerprint(output_dir):
    """What a run produced, as sorted ``(path below the output directory, content digest)``.

    Names alone would not do. A scheme is free to declare ``filename: app.conf`` for an INI
    output, and the same declaration keeps that name when the format changes underneath it, so
    two renders that differ in nothing but format can produce identical file names. The digest
    is what notices.
    """
    prints = []

    for base, dummy, names in os.walk(output_dir):
        for name in names:
            path = os.path.join(base, name)

            with open(path, "rb") as handle:
                prints.append((os.path.relpath(path, output_dir).replace(os.sep, "/"),
                               hashlib.sha256(handle.read()).hexdigest()))

    return sorted(prints)


def _confirm_the_format_asked_for(executable, input_paths, scheme_paths, output_dir, directory,
                                  probe, fmt):
    """Refuse a render whose format is not the one the caller asked for.

    The scheme already ran; this runs it again with ``probe`` appended, which by Section 15.2
    is the same render with the caller's format forced to win. Identical results mean the format
    was already the caller's and there is nothing to report. Different results mean a later
    ``output`` declaration displaced the one the caller had in mind, and the document about to be
    returned is in some other format -- an answer rather than an error, which is why nothing else
    catches it.

    A failure of the second run counts as a difference, not as an error to pass on. A redundant
    declaration cannot break a render that already agreed with it, so the failure is evidence
    about the format; but it describes a render nobody asked for, and surfacing its diagnostic
    would send the reader to fix a scheme that is not the one they wrote.
    """
    probe_path = os.path.join(directory, "probe.scheme.txt")
    probe_dir = os.path.join(directory, "probe-out")

    _write(probe_path, probe)
    os.mkdir(probe_dir)

    try:
        # The tool's own diagnostics are deliberately dropped: `_warn` already surfaced the ones
        # belonging to the caller's render, and repeating them for a render invented here would
        # double every warning the scheme legitimately raises.
        n2x.run_tool(executable, _argv(input_paths, scheme_paths + [probe_path], probe_dir))
        asked_for = _fingerprint(probe_dir)
    except _SharedError:
        asked_for = None

    if asked_for == _fingerprint(output_dir):
        return

    raise Namespace2XmlError(
        "the scheme does not produce '%s' here. The filter rendered it a second time with "
        "'%s' appended -- section 15.2 gives the last matching declaration precedence, so that "
        "is the render that was asked for -- and the two did not match. A later 'output' "
        "declaration is displacing the one meant to apply, and the document this would have "
        "returned is in a different format. Make the last declaration that matches the "
        "rendered subtree name '%s', or ask for the format that scheme really produces.%s"
        % (fmt, probe.strip(), fmt, n2x.support_hint(executable)))


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


def _argv(input_paths, scheme_paths, output_dir):
    """The tool's argument vector, with the sources in the order they are meant to merge in.

    The order is the whole meaning. Section 7.3 requires CLI source order for merging, wildcard
    evaluation and precedence assignment, and section 17.1 gives the later contribution
    precedence, so this list *is* the precedence the caller wrote down.
    """
    argv = []

    for path in input_paths:
        argv.extend(["-i", path])

    for path in scheme_paths:
        argv.extend(["-s", path])

    argv.extend(["-o", output_dir])

    return argv


def _run_and_read(executable, input_paths, scheme_paths, output_dir):
    """Spawn the tool over prepared files and read the single output back."""
    # `run_tool` raises on a non-zero exit with the tool's own diagnostics folded into the
    # message, and returns stderr on success so a diagnostic about a rule that matched nothing
    # is not swallowed. Both halves of that are the shared behaviour; only the reading back of
    # a single file below is the filter's own.
    _warn(n2x.run_tool(executable, _argv(input_paths, scheme_paths, output_dir)))

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
