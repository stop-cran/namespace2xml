#!/usr/bin/python
# Copyright (c) 2026 stop-cran
# Apache License 2.0 (see LICENSE)

from __future__ import annotations

DOCUMENTATION = r"""
module: render
short_description: Render namespace2xml inputs on a managed node into configuration files
version_added: 2.0.0
author:
  - stop-cran (@stop-cran)
description:
  - Runs the C(namespace2xml) transformer B(on the target node) over input files that are
    already there, and converges a destination directory onto the result.
  - This is the counterpart to the C(stop_cran.namespace2xml.render) B(filter). The filter
    transforms data held on the controller; this module transforms files held on the node, so
    the node's own files decide the node's state. Use the module when the inputs are part of
    the deployed application, and the filter when the inputs are play variables.
  - Idempotence is decided by comparison, not estimated. The render goes to a scratch directory
    and every produced file is compared byte for byte against the destination; only files that
    actually differ are written. Section 24 of the specification makes the tool's output
    deterministic, which is what makes that comparison exact rather than a heuristic.
  - The module refuses a render whose output would overwrite one of its own C(src) files. See
    the C(src) option for why that is a refusal rather than a warning.
  - Diagnostics are the tool's own and reach you unchanged. Warnings from a successful render
    are returned in I(diagnostics) and issued as Ansible warnings; a failure carries the tool's
    text and the address to report it to. Every code is listed in the diagnostic registry
    linked below.
options:
  inputs:
    description:
      - Ordered inputs. Each becomes one C(-i) argument, in the order given, and section 5.1
        makes a later contribution win over an earlier one - so this order is part of what the
        play is asking for and is never sorted.
      - An entry is either a B(string), naming a file B(on the managed node), or a B(mapping)
        carrying exactly one of O(inputs[].file), O(inputs[].text) or O(inputs[].data).
      - Formats are recognised by extension per section 7.1 of the specification.
      - No file named here is ever written to. If the render would produce a file at a path
        listed here, the module fails instead. A run that reads back its own output does not
        converge under C(merge=append), which section 16.10 defines as rebasing each later
        sequence contribution above the current high-water mark - so the sequence would grow by
        one copy per run and the task would report C(changed) forever. Keep inputs pristine and
        send output elsewhere.
      - Was named C(src), which still works and is deprecated. The new name is the one the
        C(stop_cran.namespace2xml.render) filter uses, so a render moves between the two
        without an argument being renamed.
    type: list
    elements: raw
    required: true
    aliases:
      - src
    suboptions:
      file:
        description:
          - A path on the managed node, passed to the tool unread. C(~) is expanded.
          - Writing the string on its own is the same thing.
          - Section 7.1 selects the parser from the extension, so O(inputs[].format) is not
            read here.
          - Mutually exclusive with O(inputs[].text) and O(inputs[].data).
        type: path
        version_added: 3.0.0
      text:
        description:
          - The input written inline in the playbook, as a document in the syntax
            O(inputs[].format) names.
          - Written to a temporary file on the node, so nothing needs to be staged there first.
          - Mutually exclusive with O(inputs[].file) and O(inputs[].data).
        type: str
        version_added: 3.0.0
      data:
        description:
          - The input as a native mapping, flattened to the tool's own syntax under
            O(selector) and O(convention) - the same encoding the
            C(stop_cran.namespace2xml.render) filter applies to the data piped into it.
          - Mutually exclusive with O(inputs[].file) and O(inputs[].text).
        type: raw
        version_added: 3.0.0
      format:
        description:
          - How O(inputs[].text) is parsed. Section 7.1 selects the parser from the file
            extension, and this chooses the extension the text is written under.
          - Meaningful only with O(inputs[].text). Setting it alongside O(inputs[].data) or
            O(inputs[].file) is refused rather than ignored.
          - Section 7.1's support matrix lists INI and shell as output formats only, so neither
            is offered here.
        type: str
        default: namespace
        choices:
          - namespace
          - json
          - yaml
          - xml
        version_added: 3.0.0
  scheme:
    description:
      - Ordered schemes, each becoming one C(-s) argument. They B(concatenate) rather than
        compete - section 15.2 resolves declarations in source order across all of them - so a
        shared scheme file with a per-host override layered on top is the shape this exists for.
      - An entry is either a B(string), naming a scheme file on the managed node, or a
        B(mapping) carrying exactly one of O(scheme[].file), O(scheme[].text) or
        O(scheme[].data).
      - At least one scheme is required. A render with no C(output) directive produces no
        files, so the tool makes C(-s) mandatory.
      - Input options such as C(xmlinputoptions) must be written unqualified. Section 16.8
        makes a selector-qualified input option a blocking C(SCHEME001), because input parsing
        happens before any output instance exists.
    type: list
    elements: raw
    default: []
    suboptions:
      file:
        description:
          - A path on the managed node, passed to the tool unread. C(~) is expanded.
          - Writing the string on its own is the same thing.
          - Mutually exclusive with O(scheme[].text) and O(scheme[].data).
        type: path
        version_added: 3.0.0
      text:
        description:
          - The scheme written inline in the playbook, as one block of text in the scheme
            language of section 16.
          - Mutually exclusive with O(scheme[].file) and O(scheme[].data).
        type: str
        version_added: 3.0.0
      data:
        description:
          - The scheme written as a native mapping instead of a block of text, so it reads as
            part of the playbook rather than as an embedded document. Passed to the tool as a
            JSON document, which section 15 accepts alongside YAML and which needs nothing
            installed on the node beyond what this module already requires.
          - B(The nesting carries the path.) Section 9 makes a mapping key one name part, so a
            dot does not separate names here as it does in O(scheme[].text) - a key containing
            a dot asks for one name with a literal dot in it. It is refused rather than passed
            through, because as a selector it would draw only C(WARN009) and the render would
            succeed with the directive inert. Write C(configuration:) then C(output:) beneath
            it, not C(configuration.output:). Quoting does not change this - C(a.b) and
            C('a.b') load to the same string.
          - To select a name that really does contain a dot, escape it C(\.) as section 8 does -
            C('a\.b') is the single name C(a.b). Write it plain or single-quoted, as YAML's
            double-quoted style rejects C("a\.b") as an unknown escape.
          - A dot inside a leading C(Q{...}) needs no escape. Section 11.4 makes those dots part
            of the URI, where they "do not split the qualified path", so C('Q{urn:example.com}name')
            and C('@Q{urn:example.com}x') are written as they read. The URI ends at the first
            unescaped C(}); a dot in the local name after it is ambiguous like any other.
          - A directive that takes several values is one comma-separated scalar, C(output),
            C("xml,json"). A YAML list is refused, because section 15 wants a nonempty scalar.
          - Quote a wildcard selector - bare C(*) is a YAML alias indicator. Quote anything that
            YAML would read as a number, since C(3.10) arrives as C(3.1).
          - Mutually exclusive with O(scheme[].file) and O(scheme[].text).
        type: dict
        version_added: 3.0.0
  selector:
    description:
      - The selector a O(inputs[].data) entry is flattened under. It has to match the selector
        the scheme declares, or the scheme's directives address nothing.
      - Read only for O(inputs[].data). It names nothing on its own.
    type: str
    default: cfg
    version_added: 3.0.0
  convention:
    description:
      - How a mapping key in a O(inputs[].data) entry is read.
      - V(escaped), the default, reads a key as data. Every section 8.2 and 11.4 marker in it
        is escaped, so any key at all round-trips as the name it spells and no key can address
        an attribute, a namespace or a content node.
      - V(xmltodict) reads the four markers section 11.4 defines, in the spelling the ecosystem
        already uses for XML-shaped mappings. C(@x) is an attribute, C(Q{uri}x) is an element
        in a namespace, C(@Q{uri}x) is a namespaced attribute, C(#0) and C(#1) are content
        nodes, and C(#text) is an element's own text.
      - Identical to the filter's argument of the same name; see it for the escaping rules.
    type: str
    default: escaped
    choices:
      - escaped
      - xmltodict
    version_added: 3.0.0
  scheme_text:
    description:
      - Deprecated. Write the same text as a O(scheme) entry's C(text) instead.
      - Kept working for plays written against 2.x. Passed B(after) every other scheme, so a
        directive here overrides the same directive in a scheme file, following section 15.2.
      - Mutually exclusive with O(scheme_yaml).
    type: str
  scheme_yaml:
    description:
      - Deprecated. Write the same mapping as a O(scheme) entry's C(data) instead.
      - Kept working for plays written against 2.x. Every note under O(scheme[].data) about
        dots, wildcards and comma-separated values applies here unchanged.
      - Mutually exclusive with O(scheme_text).
    type: dict
    version_added: 2.1.0
  variables:
    description:
      - Namespace entries applied after all input files, each becoming one C(-v name=value)
        argument in the order written.
      - Keys are namespace paths passed to the tool B(verbatim), which is what makes the XML
        addressing of section 11 reachable - C(configuration.root.@level) means that attribute.
        Nothing here is escaped, so a key is exactly the path the tool sees.
      - Values may be strings, numbers or booleans. A mapping or list value is refused, and so
        is C(null) - write the intended literal as a quoted string.
    type: dict
  dest:
    description:
      - The output root directory on the managed node, passed as C(-o). Files are written
        beneath it at the paths the scheme's C(filename) directives ask for, and intermediate
        directories are created.
      - Files already under I(dest) that this render does not produce are left untouched. The
        module never deletes.
    type: path
    required: true
  tool:
    description:
      - Path to the C(namespace2xml) binary on the managed node, or a bare name to resolve on
        C(PATH).
      - When omitted the module searches C($NAMESPACE2XML), then C(PATH), then the dotnet
        global tools directory. The environment variable comes first so an operator can pin a
        specific build without editing the play; the dotnet directory comes last and matters
        more here than on the controller, because a module runs in a non-interactive shell
        that never sourced the profile C(dotnet tool install) asked the operator to re-source.
      - A binary whose C(--version) declares no C(contract-bundle) is refused. That is the
        signal for a pre-3.0 build, which accepts these very arguments and would render
        silently under the older contract instead of failing.
    type: path
extends_documentation_fragment:
  - ansible.builtin.files
attributes:
  check_mode:
    support: full
    description: Renders and compares without writing, reporting exactly what would change.
  diff_mode:
    support: full
    description: Reports before and after content for every file that differs.
  platform:
    support: full
    description: Runs the tool through a subprocess and converges files with the standard
      Ansible file arguments, neither of which is POSIX-specific; the value below records
      where it is actually tested.
    platforms: posix
requirements:
  - namespace2xml 3.0 or newer, installed on the managed node
  - .NET SDK on the managed node, when installing the tool with C(dotnet tool install)
notes:
  - The tool must be present on every node this runs against. Install it with
    C(dotnet tool install --global --prerelease namespace2xml). C(--prerelease) is required
    while the 3.0 line is on preview; without it dotnet installs the 2.x tool, which this
    module refuses.
  - Reading and rewriting XML in the same format normally needs
    C(xmlinputoptions=NormalizeFormattingWhitespace), written unqualified. Without it,
    indentation is parsed as content and the run is refused with C(TYPE001). Enabling it emits
    C(WARN007), which records that the same-format round-trip guarantee is weakened; the
    warning is passed through rather than suppressed.
  - The top-level namespace name of an XML input is its document element. An overlay for
    C(<configuration>) is therefore rooted at C(configuration), and the scheme needs
    C(root=configuration) to write the single document element back out.
seealso:
  - name: Contract summary, shipped in this collection
    description: >-
      The rules that decide what this module does to your files, quoted verbatim from the
      specification and checked against it in CI. It is installed alongside this plugin at
      C(docs/specification-summary.md) inside the collection, so it is readable with no network
      access. Start here; the full specification is 300 KB.
    link: https://github.com/stop-cran/namespace2xml/blob/master/ansible/docs/specification-summary.md
  - name: namespace2xml specification
    description: The normative contract this module runs against.
    link: https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md
  - name: Diagnostic code registry
    description: >-
      Every code the tool emits, what it means, and the specification clause it enforces. A
      code arriving from this module, such as C(TYPE001) or C(WARN007), is looked up here. A
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
    description: The transformer this module runs.
    link: https://www.nuget.org/packages/namespace2xml
  - name: The controller-side render filter
    description: >-
      The other plugin in this collection. It carries the same short name but a different
      plugin type - ask for it with C(ansible-doc -t filter). Use it when the truth lives in
      play variables rather than in files on the node.
    link: https://github.com/stop-cran/namespace2xml/blob/master/ansible/README.md#the-render-filter
"""

EXAMPLES = r"""
- name: Raise the logback root level on each node, in place from a pristine template
  stop_cran.namespace2xml.render:
    inputs:
      - /opt/app/templates/logback.xml
    scheme:
      - text: |
          xmlinputoptions=NormalizeFormattingWhitespace
          configuration.output=xml
          configuration.filename=logback.xml
          configuration.root=configuration
    variables:
      configuration.root.@level: DEBUG
    dest: /opt/app/conf
    mode: "0644"

- name: The same scheme as a YAML mapping, where the nesting carries the path
  stop_cran.namespace2xml.render:
    inputs:
      - /opt/app/templates/logback.xml
    scheme:
      - data:
          xmlinputoptions: NormalizeFormattingWhitespace
          configuration:
            output: xml
            filename: logback.xml
            root: configuration
            # Quoted: a bare asterisk is a YAML alias indicator, not a selector.
            appender:
              "*":
                name:
                  type: ignore
    variables:
      configuration.root.@level: DEBUG
    dest: /opt/app/conf
    mode: "0644"

- name: Combine a shipped default with a node-local override
  stop_cran.namespace2xml.render:
    inputs:
      - /opt/app/defaults.yml
      - /etc/app/overrides.yml
    scheme:
      - /opt/app/render.scheme
    dest: /etc/app/generated

- name: Render play variables that were never staged on the node
  stop_cran.namespace2xml.render:
    inputs:
      - /opt/app/defaults.yml
      - data: "{{ app_settings }}"
    scheme:
      - /opt/app/render.scheme
    dest: /etc/app/generated

- name: A shipped scheme with a per-host override layered on top
  stop_cran.namespace2xml.render:
    inputs: [/opt/app/defaults.yml]
    scheme:
      - /opt/app/render.scheme
      - data:
          cfg:
            filename: "{{ inventory_hostname }}.json"
    dest: /etc/app/generated

- name: Report what would change without touching the node
  stop_cran.namespace2xml.render:
    inputs: [/opt/app/defaults.yml]
    scheme: [/opt/app/render.scheme]
    dest: /etc/app/generated
  check_mode: true
  register: preview

- name: Restart only when a rendered file actually changed
  ansible.builtin.debug:
    var: preview.changed_files
"""

RETURN = r"""
files:
  description: Every file the render produced, as absolute paths under I(dest).
  returned: success
  type: list
  elements: str
  sample: ["/etc/app/generated/logback.xml"]
changed_files:
  description:
    - The subset of I(files) whose content differed from what was already on the node. Those
      files were written, except under C(check_mode), where they are the files that would have
      been written. Empty on a converged run.
    - A file appears here only for a content difference. A file whose mode or ownership changed
      is reported through the task's C(changed) status, not through this list.
  returned: success
  type: list
  elements: str
  sample: ["/etc/app/generated/logback.xml"]
diagnostics:
  description:
    - The tool's own diagnostic output from a successful run, verbatim. Empty when the run was
      clean. Codes are looked up in the diagnostic registry.
  returned: success
  type: str
  sample: "WARN007: ..."
tool:
  description: The resolved path of the binary that ran.
  returned: success
  type: str
  sample: "/root/.dotnet/tools/namespace2xml"
tool_identity:
  description:
    - The contract identity of that binary, as C(version|contract-bundle). Two builds of one
      version compiled against different bundle revisions may legitimately render differently,
      so both halves are reported.
  returned: success
  type: str
  sample: "3.0.0|r99+aaaaaaa"
"""

import os
import shutil
import tempfile

from ansible.module_utils.basic import AnsibleModule

from ..module_utils.n2x import Namespace2XmlError
from ..module_utils.entries import marshal_inputs, marshal_schemes
from ..module_utils.render_node import render


def _materialize(entry, directory):
    """The path the tool should read for one entry, writing it out first when it is content.

    A file entry is handed over where it lies. Copying it onto the node's scratch space would
    double the disk a large template costs and would put a path into the tool's own diagnostics
    that is deleted before the operator can go and look at it.
    """
    if entry.path is not None:
        return entry.path

    path = os.path.join(directory, entry.name)

    with open(path, "w", encoding="utf-8") as handle:
        handle.write(entry.text)

    return path


def _scheme_entries(module):
    """Every scheme this task supplies, in the order the tool should apply them.

    The deprecated spellings come last on purpose, and that is not new: section 15.2 makes the
    later directive win, and a scheme written in the playbook is a more specific statement of
    intent than one shipped in a file with the application. Appending them rather than refusing
    them alongside C(scheme) is also what 2.x did, and plays depend on it.
    """
    given = module.params["scheme"]

    # Deliberately not `list(given)`: a mapping written where a list belongs would become a list
    # of its keys, and the render would go looking for files named `text` and `data`. Anything
    # that is not a list is passed through so the shared marshaller refuses it by name.
    if isinstance(given, (list, tuple)):
        given = list(given)

        if module.params["scheme_text"]:
            given.append({"text": module.params["scheme_text"]})

        # Not truthiness: an empty mapping is a mistake worth reporting, not one to ignore.
        if module.params["scheme_yaml"] is not None:
            given.append({"data": module.params["scheme_yaml"]})

    return marshal_schemes(given)


def main():
    module = AnsibleModule(
        argument_spec=dict(
            inputs=dict(type="list", elements="raw", required=True, aliases=["src"],
                        deprecated_aliases=[dict(name="src", version="4.0.0",
                                                 collection_name="stop_cran.namespace2xml")]),
            scheme=dict(type="list", elements="raw", default=[]),
            scheme_text=dict(type="str"),
            scheme_yaml=dict(type="dict"),
            selector=dict(type="str", default="cfg"),
            convention=dict(type="str", default="escaped",
                            choices=["escaped", "xmltodict"]),
            variables=dict(type="dict"),
            dest=dict(type="path", required=True),
            tool=dict(type="path"),
        ),
        add_file_common_args=True,
        supports_check_mode=True,
        mutually_exclusive=[("scheme_text", "scheme_yaml")],
    )

    dest = module.params["dest"]
    scratch = tempfile.mkdtemp(dir=module.tmpdir)

    try:
        inputs = marshal_inputs(module.params["inputs"], module.params["selector"],
                                module.params["convention"])
        schemes = _scheme_entries(module)

        # Into `module.tmpdir` and never into `scratch`: `scratch` is the output directory, and
        # anything written there would be discovered as a file the render produced.
        src = [_materialize(entry, module.tmpdir) for entry in inputs]
        scheme_paths = [_materialize(entry, module.tmpdir) for entry in schemes]

        result = render(
            src=src,
            schemes=scheme_paths,
            dest=dest,
            scratch=scratch,
            variables=module.params["variables"],
            tool=module.params["tool"],
            check_mode=module.check_mode,
            diff_mode=module._diff,
            unsafe_writes=module.params["unsafe_writes"],
        )
    except Namespace2XmlError as error:
        module.fail_json(msg=str(error))
    except OSError as error:
        # A bare OSError would reach the operator as a Python traceback with no indication of
        # which file it was about. It can also arrive after some files have already been
        # published, so the message says that rather than leaving the node's state a guess.
        module.fail_json(
            msg="failed while publishing under '%s': %s. Some files may already have been "
                "written; re-run the task to converge the rest." % (dest, error))
    finally:
        shutil.rmtree(scratch, ignore_errors=True)

    if result["diagnostics"]:
        module.warn(result["diagnostics"])

    # Every produced file, not only the ones whose content differed. A file whose content is
    # already converged can still have the wrong mode or owner, and leaving it that way would
    # make the task's idempotence depend on which run first created the file.
    #
    # This runs in check mode too. Skipping it there is what makes a mode-only difference
    # report changed=false under --check and changed=true on the real run, which contradicts
    # the check_mode: full claim above. Ansible's own setters are check-mode aware: they
    # compare and report without touching the file, and they tolerate a path that does not
    # exist yet -- which is every file of a first render.
    file_args = module.load_file_common_arguments(module.params)

    for path in result["files"]:
        file_args["path"] = path
        result["changed"] = module.set_fs_attributes_if_different(file_args, result["changed"])

    module.exit_json(**result)


if __name__ == "__main__":
    main()
