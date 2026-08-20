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
  src:
    description:
      - Ordered input file paths B(on the managed node). Each becomes one C(-i) argument, in
        the order given, and section 15.2 makes a later contribution win over an earlier one -
        so this order is part of what the play is asking for and is never sorted.
      - Formats are recognised by extension per section 7.1 of the specification.
      - No file named here is ever written to. If the render would produce a file at a path
        listed here, the module fails instead. A run that reads back its own output does not
        converge under C(merge=append), which section 16.10 defines as rebasing each later
        sequence contribution above the current high-water mark - so the sequence would grow by
        one copy per run and the task would report C(changed) forever. Keep inputs pristine and
        send output elsewhere.
    type: list
    elements: path
    required: true
  scheme:
    description:
      - Ordered scheme file paths on the managed node, each becoming one C(-s) argument.
      - At least one of I(scheme) or I(scheme_text) is required. A render with no C(output)
        directive produces no files, so the tool makes C(-s) mandatory.
    type: list
    elements: path
    default: []
  scheme_text:
    description:
      - Scheme directives written inline in the playbook, as one block of text in the scheme
        language of section 16.
      - Written to a temporary file and passed B(after) every path in I(scheme), so a directive
        here overrides the same directive in a scheme file, following section 15.2.
      - Input options such as C(xmlinputoptions) must be written unqualified. Section 16.8
        makes a selector-qualified input option a blocking C(SCHEME001), because input parsing
        happens before any output instance exists.
    type: str
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
      code arriving from this module, such as C(TYPE001) or C(WARN007), is looked up here.
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
    src:
      - /opt/app/templates/logback.xml
    scheme_text: |
      xmlinputoptions=NormalizeFormattingWhitespace
      configuration.output=xml
      configuration.filename=logback.xml
      configuration.root=configuration
    variables:
      configuration.root.@level: DEBUG
    dest: /opt/app/conf
    mode: "0644"

- name: Combine a shipped default with a node-local override
  stop_cran.namespace2xml.render:
    src:
      - /opt/app/defaults.yml
      - /etc/app/overrides.yml
    scheme:
      - /opt/app/render.scheme
    dest: /etc/app/generated

- name: Report what would change without touching the node
  stop_cran.namespace2xml.render:
    src: [/opt/app/defaults.yml]
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
from ..module_utils.render_node import render


def main():
    module = AnsibleModule(
        argument_spec=dict(
            src=dict(type="list", elements="path", required=True),
            scheme=dict(type="list", elements="path", default=[]),
            scheme_text=dict(type="str"),
            variables=dict(type="dict"),
            dest=dict(type="path", required=True),
            tool=dict(type="path"),
        ),
        add_file_common_args=True,
        supports_check_mode=True,
    )

    dest = module.params["dest"]
    schemes = list(module.params["scheme"])
    scratch = tempfile.mkdtemp(dir=module.tmpdir)

    try:
        if module.params["scheme_text"]:
            # Last, so an inline directive overrides the same directive in a scheme file.
            # Section 15.2 makes the later one win, and the playbook is the more specific
            # statement of intent than a file shipped with the application.
            inline = os.path.join(module.tmpdir, "inline.scheme")

            with open(inline, "w", encoding="utf-8") as handle:
                handle.write(module.params["scheme_text"])

            schemes.append(inline)

        result = render(
            src=module.params["src"],
            schemes=schemes,
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
