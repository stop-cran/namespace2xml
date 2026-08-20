# Copyright (c) 2026 stop-cran
# Apache License 2.0 (see LICENSE)

"""Rendering on a managed node: argument construction, safety, and convergence.

The filter transforms data that already lives on the controller. This is the other topology,
and the one the tool was designed for: the inputs are files on the node, and it is the node's
own file that decides the node's state.

Everything here is deliberately free of ``ansible`` imports so it can be exercised on any
machine with a Python interpreter, including one where ``ansible.module_utils.basic`` cannot be
imported at all. ``plugins/modules/render.py`` is the thin shell that adds argument parsing,
check mode and file attributes on top.

Two properties are the reason this file exists rather than a handful of lines in the module.

**Convergence is decided by comparison, not by hope.** The tool is deterministic -- section 3
of the specification makes byte-identical output from identical inputs a contract term -- so
rendering into a scratch directory and comparing bytes against the destination is not a
heuristic for "did anything change", it is the answer. ``check_mode`` and ``--diff`` then come
out of the same comparison for free, rather than being a second, weaker estimate of it.

**A source file is never a destination file.** Section 16.10 defines ``merge=append`` as
rebasing a later sequence contribution onto fresh implicit ordering values above the current
high-water mark. A run whose output overwrites its own input therefore does not converge under
append: it re-reads what it just appended and appends to that. ``["STDOUT"]`` becomes
``["STDOUT", "FILE"]`` becomes ``["STDOUT", "FILE", "FILE"]``, once per run, forever. The guard
below refuses that arrangement outright rather than documenting it as a caveat, because the
failure is silent, cumulative, and indistinguishable from a working play until someone reads
the file.
"""

from __future__ import annotations

import filecmp
import os
import shutil

from .n2x import Namespace2XmlError, resolve, run_tool, tool_identity

__all__ = [
    "build_argv",
    "encode_variable",
    "discover",
    "guard_sources",
    "plan",
    "render",
]


def encode_variable(name, value):
    """Render one ``variables`` entry as the ``name=value`` token ``-v`` expects.

    The name is passed through verbatim. That is the point of this option: an operator writing
    ``configuration.root.@level`` means the XML attribute addressing of section 11, and any
    escaping applied on the way would turn a deliberate marker into a literal name part. Names
    synthesized from a nested mapping are a different problem with a different answer -- see the
    filter, and issue 103.
    """
    if isinstance(value, bool):
        # YAML 1.2 spells these lowercase and so does section 18. ``str(True)`` would hand the
        # tool ``True``, which is a string scalar, not the boolean the playbook author wrote.
        return "%s=%s" % (name, "true" if value else "false")

    if value is None:
        raise Namespace2XmlError(
            "variables['%s'] is null. A YAML null is ambiguous here: it may mean the tool's "
            "'null' literal or an empty string. Write whichever is intended as a quoted "
            "string." % name)

    if isinstance(value, (dict, list, tuple)):
        raise Namespace2XmlError(
            "variables['%s'] is a %s. 'variables' carries one namespace entry per key, and its "
            "keys are namespace paths passed to the tool verbatim. To build entries from nested "
            "data, render that data with the stop_cran.namespace2xml.render filter on the "
            "controller and pass the result as an input file, or write the leaf paths out one "
            "per key." % (name, type(value).__name__))

    return "%s=%s" % (name, value)


def build_argv(src, schemes, variables, out_dir):
    """The tool arguments for one render, in specification order.

    Order is meaning, not style. Section 15.2 makes a later directive win over an earlier one,
    so the sequence in which schemes are handed over is part of what the play asked for; the
    module preserves the operator's order and never sorts.
    """
    if not src:
        raise Namespace2XmlError("'src' must name at least one input file.")

    if not schemes:
        raise Namespace2XmlError(
            "at least one scheme is required: pass 'scheme' file paths, 'scheme_text', or both. "
            "The tool's '-s' option is mandatory because a render with no output directive "
            "produces no files.")

    argv = []

    for path in src:
        argv += ["-i", path]

    for path in schemes:
        argv += ["-s", path]

    for name, value in (variables or {}).items():
        argv += ["-v", encode_variable(name, value)]

    argv += ["-o", out_dir]

    return argv


def discover(root):
    """Every file under a directory, as relative paths, sorted for a stable report."""
    found = []

    for directory, dummy, names in os.walk(root):
        for name in names:
            full = os.path.join(directory, name)
            found.append(os.path.relpath(full, root).replace(os.sep, "/"))

    return sorted(found)


def guard_sources(src, dest, relatives):
    """Refuse a render whose output would overwrite one of its own inputs.

    See the module docstring for why this is a refusal and not a warning. The check is by
    resolved path and so catches the arrangement however it was spelled -- a relative ``src``, a
    ``dest`` reached through a symlink, or ``.`` -- because the trap is a property of the files,
    not of the spelling.
    """
    sources = {}

    for path in src:
        sources[os.path.realpath(path)] = path

    for relative in relatives:
        produced = os.path.realpath(os.path.join(dest, relative))

        if produced in sources:
            raise Namespace2XmlError(
                "this render would write '%s' over its own input '%s'. The module refuses that: "
                "a run that reads back its own output does not converge under 'merge=append', "
                "which section 16.10 defines as rebasing each later sequence contribution above "
                "the current high-water mark -- so a sequence grows by one copy on every run "
                "and the play reports 'changed' forever. Keep the input pristine and send the "
                "output somewhere else, or read from a template path the render never writes."
                % (os.path.join(dest, relative), sources[produced]))


def plan(scratch, dest, relatives):
    """Which produced files differ from what is already on the node.

    ``filecmp`` with ``shallow=False`` is a byte comparison. Size and mtime are not consulted,
    because the tool writes fresh files into a scratch directory on every run and their mtimes
    are therefore always newer than the destination's -- a shallow comparison would report every
    file changed on every run and destroy the idempotency this whole file exists to provide.
    """
    changed = []

    for relative in relatives:
        target = os.path.join(dest, relative)

        if not os.path.isfile(target):
            changed.append(relative)
            continue

        if not filecmp.cmp(os.path.join(scratch, relative), target, shallow=False):
            changed.append(relative)

    return changed


def _read(path, limit=1024 * 1024):
    """A file's text for a diff, or a note saying why it is not being shown."""
    try:
        size = os.path.getsize(path)
    except OSError:
        return ""

    if size > limit:
        return "(%d bytes; too large to diff)\n" % size

    with open(path, "rb") as handle:
        raw = handle.read()

    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        return "(%d bytes of non-UTF-8 content)\n" % size


def diffs(scratch, dest, changed):
    """Per-file before/after pairs in the shape ``ansible-playbook --diff`` expects."""
    entries = []

    for relative in changed:
        target = os.path.join(dest, relative)
        entries.append({
            "before": _read(target) if os.path.isfile(target) else "",
            "after": _read(os.path.join(scratch, relative)),
            "before_header": target,
            "after_header": relative,
        })

    return entries


def render(src, schemes, dest, scratch, variables=None, tool=None, check_mode=False,
           runner=None):
    """Render into a scratch directory, then converge the destination onto it.

    :param src: ordered input file paths on the node.
    :param schemes: ordered scheme file paths, inline text already materialised.
    :param dest: the output root directory.
    :param scratch: an existing empty directory the caller owns and will remove.
    :param variables: namespace entries applied after all inputs.
    :param tool: an explicit binary path, or ``None`` to search.
    :param check_mode: report what would change without writing anything.
    :param runner: an override for the tool invocation, for tests.
    :returns: a report dictionary.
    """
    executable = resolve(tool)
    # The identity gate runs before anything is written. A 2.x binary accepts these very
    # arguments and would render silently under the older contract, so proving the build first
    # is the difference between a refusal and a node quietly converged onto the wrong output.
    identity = tool_identity(tool)

    argv = build_argv(src, schemes, variables, scratch)
    diagnostics = (runner or run_tool)(executable, argv)

    produced = discover(scratch)

    if not produced:
        raise Namespace2XmlError(
            "the tool produced no output files. A scheme must name at least one 'output' "
            "directive for a render to write anything; see section 16 of the specification."
            + ("\n%s" % diagnostics.strip() if diagnostics.strip() else ""))

    guard_sources(src, dest, produced)

    changed = plan(scratch, dest, produced)
    report = {
        "changed": bool(changed),
        "files": [os.path.join(dest, relative) for relative in produced],
        "changed_files": [os.path.join(dest, relative) for relative in changed],
        "tool": executable,
        "tool_identity": identity,
        "diagnostics": diagnostics.strip(),
        "diff": diffs(scratch, dest, changed),
    }

    if check_mode or not changed:
        return report

    for relative in changed:
        target = os.path.join(dest, relative)
        parent = os.path.dirname(target)

        if parent and not os.path.isdir(parent):
            os.makedirs(parent)

        # copyfile writes content only, leaving an existing file's mode, owner and ACL alone.
        # Anything the play wants changed about those is applied afterwards through Ansible's
        # own file arguments, which is the only path that reports them in the diff.
        shutil.copyfile(os.path.join(scratch, relative), target)

    return report
