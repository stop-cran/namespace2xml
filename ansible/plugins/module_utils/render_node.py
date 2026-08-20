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

**Convergence is decided by comparison, not by hope.** The tool is deterministic -- section 24
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

import errno
import math
import os
import stat

from .n2x import Namespace2XmlError, encode_value, resolve, run_tool, tool_identity

__all__ = [
    "build_argv",
    "encode_variable",
    "discover",
    "guard_sources",
    "open_confined",
    "plan",
    "publish",
    "render",
]


def encode_variable(name, value):
    """Render one ``variables`` entry as the ``name=value`` token ``--variables`` expects.

    The name is passed through verbatim. That is the point of this option: an operator writing
    ``configuration.root.@level`` means the XML attribute addressing of section 11, and any
    escaping applied on the way would turn a deliberate marker into a literal name part. Names
    synthesized from a nested mapping are a different problem with a different answer -- see the
    filter, and issue 103.

    The value is the opposite case and gets the opposite treatment. It arrives as data -- a
    string a playbook author wrote -- and section 8.3 would read it as namespace-profile value
    syntax, so it is encoded through ``encode_value``, exactly as the filter encodes the scalars
    it finds in a mapping. Without that, ``C:\\temp\\new`` reaches the model as ``C:`` plus a tab
    plus a line feed, and ``${JAVA_HOME}/bin`` fails the task with ``REFERENCE002`` instead of
    reaching the document. A reference that is *meant* as a reference belongs in a scheme or an
    input file, where it is visible as one.
    """
    if isinstance(value, bool):
        # Section 18 infers a boolean from case-insensitive 'true'/'false', so 'True' would be
        # read as one too. Lowercase is the canonical spelling -- it is what section 24 requires
        # on the way out and what the filter's encode_scalar emits -- so the two plugins put the
        # same eight characters in front of the tool for the same playbook value.
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

    if isinstance(value, float) and not math.isfinite(value):
        raise Namespace2XmlError(
            "variables['%s'] is %r, which has no decimal spelling in section 18: it would be "
            "handed over as the string 'inf', 'nan' or '-inf' and inferred as a string rather "
            "than a number. Write the value the document should carry as a quoted string."
            % (name, value))

    return "%s=%s" % (name, encode_value(str(value)))


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

    # The inline long form, not '-i value'. Section 6.2 classifies any active token beginning
    # with '-' as an option token, so a detached value that happens to start with a dash -- a
    # file literally named '-evil.yml', or a variable record whose name starts with one -- is
    # read as another option and fails with CLI001 rather than being used. '--input=-evil.yml'
    # has no such ambiguity, and '--variables=a.b=c' splits on the first '=' only.
    for path in src:
        argv.append("--input=%s" % path)

    for path in schemes:
        argv.append("--scheme=%s" % path)

    for name, value in (variables or {}).items():
        argv.append("--variables=%s" % encode_variable(name, value))

    argv.append("--output=%s" % out_dir)

    return argv


def discover(root):
    """Every file under a directory, as relative paths, sorted for a stable report."""
    found = []

    for directory, dummy, names in os.walk(root):
        for name in names:
            full = os.path.join(directory, name)
            found.append(os.path.relpath(full, root).replace(os.sep, "/"))

    return sorted(found)


def _identity(path):
    """A value equal for two paths naming the same file, however they were spelled.

    ``st_dev``/``st_ino`` is file identity as the filesystem understands it, so it sees through
    hard links and case-insensitive names, which a path comparison cannot. It only exists for a
    file that exists; for a destination that does not yet, the resolved path is the best
    available answer and is the one that catches the ordinary symlink and relative spellings.
    """
    try:
        info = os.stat(path)
    except OSError:
        return None

    return (info.st_dev, info.st_ino)


def guard_sources(src, schemes, dest, relatives):
    """Refuse a render whose output would overwrite one of its own inputs.

    See the module docstring for why this is a refusal and not a warning. Inputs and schemes are
    both guarded: a render that destroys the scheme that produced it is no better than one that
    destroys its input.

    Two comparisons, because one is not enough. The resolved path catches the arrangement
    however it was spelled -- a relative ``src``, a ``dest`` reached through a symlink, or ``.``
    -- and works for a destination that does not exist yet. ``st_dev``/``st_ino`` catches the
    aliases a path cannot see: a hard link, or the same file reached through a case-insensitive
    filesystem, where two different strings are one inode and a copy would truncate the input.
    """
    guarded = {}
    identities = {}

    for path in list(src) + list(schemes):
        guarded[os.path.realpath(path)] = path
        key = _identity(path)

        if key is not None:
            identities[key] = path

    for relative in relatives:
        target = os.path.join(dest, relative)
        original = guarded.get(os.path.realpath(target))

        if original is None:
            original = identities.get(_identity(target))

        if original is not None:
            raise Namespace2XmlError(
                "this render would write '%s' over its own input '%s'. The module refuses that: "
                "a run that reads back its own output does not converge under 'merge=append', "
                "which section 16.10 defines as rebasing each later sequence contribution above "
                "the current high-water mark -- so a sequence grows by one copy on every run "
                "and the play reports 'changed' forever. Keep the input pristine and send the "
                "output somewhere else, or read from a template path the render never writes."
                % (target, original))


_CONFINEMENT = (
    hasattr(os, "O_NOFOLLOW")
    and hasattr(os, "O_DIRECTORY")
    # os.replace is renameat(2) on POSIX, exactly as os.rename is, but only os.rename is listed
    # in supports_dir_fd -- so os.rename is the honest proxy for "renameat is available here".
    and {os.open, os.mkdir, os.stat, os.rename, os.unlink, os.chmod} <= os.supports_dir_fd
)

_CONFINEMENT_REFUSAL = (
    "this platform cannot provide the filesystem primitives needed to publish safely: "
    "handle-relative opens that refuse to traverse a symbolic link. Section 21.1 of the "
    "specification requires the implementation to fail before creating directories or opening "
    "destinations rather than publish without containment, because a symlinked component under "
    "'dest' would redirect a privileged write anywhere on the node. This module supports POSIX "
    "targets; run the tool through the controller-side filter instead."
)


def _confined_parent(dest, relative, create):
    """A directory handle for ``relative``'s parent under ``dest``, refusing every symlink.

    Section 21.1 requires publication through "handle-relative or equivalent no-follow
    filesystem operations" with "symbolic-link, junction, and reparse-point containment when
    opening each destination". A path check cannot deliver that: whatever ``realpath`` said a
    moment ago can be replaced by a symlink before the open, and the module frequently runs
    under ``become``. Descending one component at a time with ``O_NOFOLLOW`` closes the window,
    because each handle names the directory that was actually opened.
    """
    if not _CONFINEMENT:
        raise Namespace2XmlError(_CONFINEMENT_REFUSAL)

    parts = [part for part in relative.split("/") if part]
    flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW

    try:
        handle = os.open(dest, flags)
    except OSError as error:
        raise Namespace2XmlError(
            "cannot open the output root '%s' as a directory: %s. A symbolic link is refused "
            "here on purpose -- see section 21.1." % (dest, error.strerror))

    try:
        for part in parts[:-1]:
            try:
                nested = os.open(part, flags, dir_fd=handle)
            except FileNotFoundError:
                if not create:
                    raise
                os.mkdir(part, 0o755, dir_fd=handle)
                nested = os.open(part, flags, dir_fd=handle)
            except OSError as error:
                if error.errno in (errno.ELOOP, errno.ENOTDIR):
                    raise Namespace2XmlError(
                        "'%s' under the output root '%s' is a symbolic link or not a directory, "
                        "so publishing through it would write outside the root. Section 21.1 "
                        "requires that to be refused." % (part, dest))
                raise

            os.close(handle)
            handle = nested
    except BaseException:
        os.close(handle)
        raise

    return handle, parts[-1]


def _stream_equal(left_fd, right_fd, chunk=65536):
    """Whether two open files hold the same bytes, without loading either whole."""
    while True:
        left = os.read(left_fd, chunk)
        right = os.read(right_fd, chunk)

        if left != right:
            return False

        if not left:
            return True


def _open_existing(handle, name):
    """An fd for a regular file in an opened directory, or ``None`` if there is nothing usable.

    ``O_NOFOLLOW`` here is what stops a diff from reading, and a comparison from trusting, a
    file outside the output root that a symlink happens to point at.
    """
    try:
        fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW, dir_fd=handle)
    except OSError:
        return None

    try:
        if not stat.S_ISREG(os.fstat(fd).st_mode):
            os.close(fd)
            return None
    except OSError:
        os.close(fd)
        return None

    return fd


def plan(scratch, dest, relatives):
    """Which produced files differ from what is already on the node.

    A byte comparison, streamed. Size and mtime are not consulted, because the tool writes fresh
    files into a scratch directory on every run and their mtimes are therefore always newer than
    the destination's -- a shallow comparison would report every file changed on every run and
    destroy the idempotency this whole file exists to provide.

    An existing destination that is not a regular file -- a symlink, a directory, a device --
    counts as changed, and publication then refuses or replaces it as its own rules require.
    """
    changed = []

    for relative in relatives:
        handle, name = _confined_parent(dest, relative, create=False) if _parent_exists(
            dest, relative) else (None, None)

        if handle is None:
            changed.append(relative)
            continue

        try:
            target_fd = _open_existing(handle, name)
        finally:
            os.close(handle)

        if target_fd is None:
            changed.append(relative)
            continue

        source_fd = os.open(os.path.join(scratch, relative), os.O_RDONLY)

        try:
            if not _stream_equal(source_fd, target_fd):
                changed.append(relative)
        finally:
            os.close(source_fd)
            os.close(target_fd)

    return changed


def _parent_exists(dest, relative):
    """Whether every directory component of ``relative`` already exists under ``dest``."""
    probe = dest

    for part in [part for part in relative.split("/") if part][:-1]:
        probe = os.path.join(probe, part)

        if not os.path.isdir(probe):
            return False

    return os.path.isdir(dest)


def _read_confined(dest, relative, limit=1024 * 1024):
    """A destination file's text for a diff, or a note saying why it is not being shown."""
    if not _parent_exists(dest, relative):
        return ""

    try:
        handle, name = _confined_parent(dest, relative, create=False)
    except (OSError, Namespace2XmlError):
        return ""

    try:
        fd = _open_existing(handle, name)
    finally:
        os.close(handle)

    if fd is None:
        return ""

    try:
        size = os.fstat(fd).st_size

        if size > limit:
            return "(%d bytes; too large to diff)\n" % size

        raw = os.read(fd, limit)
    finally:
        os.close(fd)

    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        return "(%d bytes of non-UTF-8 content)\n" % len(raw)


def _read_scratch(path, limit=1024 * 1024):
    """A produced file's text for a diff. The scratch tree is ours, so no confinement is due."""
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
        entries.append({
            "before": _read_confined(dest, relative),
            "after": _read_scratch(os.path.join(scratch, relative)),
            "before_header": os.path.join(dest, relative),
            "after_header": relative,
        })

    return entries


def _default_file_mode():
    """The mode a newly created file would get, honouring the process umask."""
    current = os.umask(0)
    os.umask(current)

    return 0o666 & ~current


def publish(scratch, dest, changed, unsafe_writes=False):
    """Copy each changed file into ``dest`` atomically, without following a link anywhere.

    Ansible's ``files`` documentation fragment -- which this module ships, and whose
    ``unsafe_writes`` option it therefore accepts -- promises that writes are atomic by default.
    A plain copy is not: it truncates first, so an interruption leaves the node holding half a
    configuration file, and a reader between the two moments sees one. Writing a sibling
    temporary file and renaming it over the destination is atomic, and ``rename`` replaces a
    symbolic link rather than following it, so it is also the containment-preserving move.
    """
    for relative in changed:
        handle, name = _confined_parent(dest, relative, create=True)

        try:
            _publish_one(os.path.join(scratch, relative), handle, name, dest, relative,
                         unsafe_writes)
        finally:
            os.close(handle)


def _publish_one(source, handle, name, dest, relative, unsafe_writes):
    """One atomic, confined replacement inside an already-opened destination directory."""
    try:
        existing = os.stat(name, dir_fd=handle, follow_symlinks=False)
    except FileNotFoundError:
        existing = None

    if existing is not None and stat.S_ISDIR(existing.st_mode):
        raise Namespace2XmlError(
            "'%s' already exists under the output root as a directory, so the render cannot "
            "publish a file there. Choose a different 'filename' in the scheme, or a different "
            "'dest'." % os.path.join(dest, relative))

    # An existing regular file keeps the mode it already had; anything the play wants changed
    # about that is applied afterwards through Ansible's own file arguments, which is the only
    # path that reports the change. A brand new file gets what the umask would have given it.
    if existing is not None and stat.S_ISREG(existing.st_mode):
        mode = stat.S_IMODE(existing.st_mode)
    else:
        mode = _default_file_mode()

    with open(source, "rb") as reader:
        payload = reader.read()

    temporary = ".%s.n2x%d.tmp" % (name, os.getpid())

    try:
        fd = os.open(temporary,
                     os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                     mode,
                     dir_fd=handle)
    except OSError as error:
        if not unsafe_writes:
            raise Namespace2XmlError(
                "cannot create a temporary file next to '%s' for an atomic replacement: %s. "
                "Some filesystems -- a bind-mounted file inside a container, for one -- do not "
                "allow it. Set 'unsafe_writes: true' to accept a non-atomic write instead."
                % (os.path.join(dest, relative), error.strerror))

        _unsafe_write(handle, name, payload)
        return

    try:
        os.write(fd, payload)
        os.fsync(fd)
    finally:
        os.close(fd)

    try:
        os.chmod(temporary, mode, dir_fd=handle)
        os.replace(temporary, name, src_dir_fd=handle, dst_dir_fd=handle)
    except OSError:
        try:
            os.unlink(temporary, dir_fd=handle)
        except OSError:
            pass

        raise


def _unsafe_write(handle, name, payload):
    """The opt-in fallback: truncate the destination in place, still without following a link."""
    fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o644,
                 dir_fd=handle)

    try:
        os.write(fd, payload)
        os.fsync(fd)
    finally:
        os.close(fd)


def prepare_dest(dest, create):
    """Validate the output root, and create it only when a publication is actually due.

    Section 21.1 is precise about this. An existing non-directory root is a hard error; a
    missing root "is included as the first directory in the validated creation plan and is
    created only after the global validation gate"; and "a zero-destination plan does not create
    it". Creating the root up front instead would leave an empty directory behind on the node
    every time a render was refused, and would make check mode write.
    """
    if os.path.lexists(dest) and not os.path.isdir(dest):
        raise Namespace2XmlError(
            "the output root '%s' exists and is not a directory. Section 21.1 makes that a "
            "PATH001 error rather than something to overwrite." % dest)

    if create and not os.path.isdir(dest):
        os.makedirs(dest)


def render(src, schemes, dest, scratch, variables=None, tool=None, check_mode=False,
           runner=None, diff_mode=False, unsafe_writes=False):
    """Render into a scratch directory, then converge the destination onto it.

    :param src: ordered input file paths on the node.
    :param schemes: ordered scheme file paths, inline text already materialised.
    :param dest: the output root directory.
    :param scratch: an existing empty directory the caller owns and will remove.
    :param variables: namespace entries applied after all inputs.
    :param tool: an explicit binary path, or ``None`` to search.
    :param check_mode: report what would change without writing anything.
    :param runner: an override for the tool invocation, for tests.
    :param diff_mode: collect before/after file contents for ``--diff``.
    :param unsafe_writes: allow a non-atomic in-place write where an atomic one is impossible.
    :returns: a report dictionary.
    """
    # Absolute from here on. RETURN documents 'files' as absolute paths, and 'type: path' only
    # expands '~' and environment variables -- it does not make a relative 'dest' absolute.
    dest = os.path.abspath(dest)

    executable = resolve(tool)
    # The identity gate runs before anything is written. A 2.x binary accepts these very
    # arguments and would render silently under the older contract, so proving the build first
    # is the difference between a refusal and a node quietly converged onto the wrong output.
    identity = tool_identity(tool)

    prepare_dest(dest, create=False)

    argv = build_argv(src, schemes, variables, scratch)
    diagnostics = (runner or run_tool)(executable, argv)

    produced = discover(scratch)

    if not produced:
        raise Namespace2XmlError(
            "the tool produced no output files. A scheme must name at least one 'output' "
            "directive for a render to write anything; see section 16 of the specification."
            + ("\n%s" % diagnostics.strip() if diagnostics.strip() else ""))

    guard_sources(src, schemes, dest, produced)

    changed = plan(scratch, dest, produced)
    report = {
        "changed": bool(changed),
        "files": [os.path.join(dest, relative) for relative in produced],
        "changed_files": [os.path.join(dest, relative) for relative in changed],
        "tool": executable,
        "tool_identity": identity,
        "diagnostics": diagnostics.strip(),
        # Rendered configuration routinely carries credentials. Reading it back into the task
        # result when nobody asked for a diff sends it to the controller, into any registered
        # variable, and into every result-recording callback. Ansible's own modules gate this on
        # module._diff and so does this one.
        "diff": diffs(scratch, dest, changed) if diff_mode else [],
    }

    if check_mode or not changed:
        return report

    prepare_dest(dest, create=True)
    publish(scratch, dest, changed, unsafe_writes=unsafe_writes)

    return report
