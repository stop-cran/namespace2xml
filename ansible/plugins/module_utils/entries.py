"""The uniform entry shape: one way to name a source, wherever a source is named.

Both the filter and the module take inputs and schemes, and until this module existed they
took them differently. The filter's ``scheme`` was inline text; the module's ``scheme`` was a
list of paths. The same word meant two things, so moving a render from one to the other meant
rewriting the argument rather than moving it, and 2.3.0 shipped ``scheme_text`` to give inline
text a name that meant the same on both sides. That is a band-aid over a shape problem.

The shape here is the fix. Everywhere a source is named -- ``inputs`` on either plugin,
``scheme`` on either plugin -- it is a list, and every entry in that list is one of:

    - a bare string, which names a file. This is the common case and stays short:
      ``inputs: [/etc/app/base.properties]``.
    - a mapping carrying exactly one of ``file``, ``text`` or ``data``, and optionally
      ``format``.

``text`` is a document written out in the tool's own syntax. ``data`` is a structure this
collection encodes first -- through the Section 8.3 profile encoder for an input, through the
Section 15 scheme encoder for a scheme -- which is what lets a playbook pass a variable it
already has, without writing a template to spell it out.

The list is ordered and the order is load-bearing. Section 7.3 merges sources in command-line
order and Section 17.1 gives the later contribution precedence, so a later entry overrides an
earlier one; Section 15.2 says the same of scheme directives. Nothing here sorts.

Why this is shared rather than done twice: a module ships to the target host with its
collection's ``module_utils`` and cannot import a filter plugin, so a marshaller written in
the filter is reachable from the controller only. The two would then be two implementations
of one documented shape, which is the arrangement this collection has already had to retire
twice -- see issue #107 and the Section 8.3 encoder now in ``profile``.
"""

from __future__ import annotations

import collections
import os

from .n2x import Namespace2XmlError, encode_scheme_mapping
from .profile import flatten

__all__ = [
    "ENTRY_KEYS",
    "Entry",
    "INPUT_FORMATS",
    "SCHEME_FORMATS",
    "marshal_inputs",
    "marshal_schemes",
]

# What one entry resolves to. Exactly one of `path` and `text` is set: `path` names a file that
# already exists and is handed to the tool where it lies, `text` is content the caller must
# write out under `name` first. `name` is set in both cases and is what diagnostics quote.
Entry = collections.namedtuple("Entry", "path text name")

ENTRY_KEYS = ("file", "text", "data", "format")

# Section 7.1, the input half of the support matrix. Every other extension selects
# namespace-profile parsing, so this list is not the set of extensions the tool tolerates -- it
# is the set that means something. INI and shell are output-only there and are deliberately
# absent: offering them would write a `.ini` file that the tool reads as a namespace profile,
# and the mistake would surface as a parse error about the wrong language.
INPUT_FORMATS = ("namespace", "json", "yaml", "xml")

_INPUT_EXTENSIONS = {"namespace": "txt", "json": "json", "yaml": "yaml", "xml": "xml"}

# Section 15 reads a scheme as its own line-oriented text, or as JSON or YAML. XML is absent
# because the section does not offer it, not because it would be hard to add.
SCHEME_FORMATS = ("namespace", "json", "yaml")

_SCHEME_EXTENSIONS = {"namespace": "txt", "json": "json", "yaml": "yaml"}


def _kind(value):
    """A readable name for what the caller supplied, for a message that has to say so."""
    if value is None:
        return "null"

    if isinstance(value, bool):
        return "a boolean"

    if isinstance(value, dict):
        return "a mapping"

    if isinstance(value, (list, tuple)):
        return "a list"

    if isinstance(value, str):
        return "a string"

    if isinstance(value, (int, float)):
        return "a number"

    # Anything else is rare enough that the type's own name is the most useful thing to say.
    # The article is chosen rather than hardcoded so the sentence reads as a sentence.
    name = type(value).__name__

    return "%s %s" % ("an" if name[:1].lower() in "aeiou" else "a", name)


def marshal_inputs(entries, selector, convention, argument="inputs"):
    """Resolve the ``inputs`` list. A ``data`` entry is flattened by the Section 8.3 encoder."""
    def encode(data):
        return flatten(data, selector, convention), "namespace"

    return _marshal(entries, argument, "input", encode, INPUT_FORMATS, _INPUT_EXTENSIONS)


def marshal_schemes(entries, argument="scheme"):
    """Resolve the ``scheme`` list. A ``data`` entry is encoded as a Section 15 JSON document."""
    def encode(data):
        # Section 15 picks the parser from the extension, so this has to arrive as .json.
        return encode_scheme_mapping(data), "json"

    return _marshal(entries, argument, "scheme", encode, SCHEME_FORMATS, _SCHEME_EXTENSIONS)


def _marshal(entries, argument, singular, encode, formats, extensions):
    """Validate the list as a whole, then each entry in turn."""
    if entries is None:
        return []

    # A bare mapping is the plausible near-miss -- one entry written directly instead of wrapped
    # in a list -- and a bare string is the other, since a string is iterable and would
    # otherwise be walked character by character. Both are named before the generic refusal.
    if isinstance(entries, (dict, str)) or not isinstance(entries, (list, tuple)):
        raise Namespace2XmlError(
            "'%s' is %s. It is a list, one entry per %s, in the order they should be applied; "
            "a single %s is a list of one." % (argument, _kind(entries), singular, singular))

    return [_one(entry, position, argument, singular, encode, formats, extensions)
            for position, entry in enumerate(entries, 1)]


def _one(entry, position, argument, singular, encode, formats, extensions):
    """Validate and resolve one entry."""
    where = "%s %d" % (singular, position)

    # A bare string names a file. It is the shape most entries have, and spelling it
    # `{file: ...}` everywhere would make the common case the noisy one.
    if isinstance(entry, str):
        entry = {"file": entry}

    if not isinstance(entry, dict):
        raise Namespace2XmlError(
            "%s is %s. Every entry in '%s' is either a string naming a file, or a mapping "
            "carrying one of 'file', 'text' or 'data', and optionally 'format'."
            % (where.capitalize(), _kind(entry), argument))

    unknown = sorted(set(entry) - set(ENTRY_KEYS))

    if unknown:
        raise Namespace2XmlError(
            "%s has the unrecognised key %s. An entry in '%s' takes %s, and nothing else -- an "
            "ignored key here would be a %s silently not applied."
            % (where.capitalize(),
               ", ".join("'%s'" % name for name in unknown),
               argument,
               ", ".join("'%s'" % name for name in ENTRY_KEYS),
               singular))

    sources = [name for name in ("file", "text", "data") if name in entry]

    if len(sources) > 1:
        raise Namespace2XmlError(
            "%s supplies %s. They are alternative ways to write one %s -- a file on disk, a "
            "document written out here, and a structure this collection encodes first -- so an "
            "entry with more than one leaves it ambiguous which to apply. Write one entry each "
            "if both were meant, and their order then says which wins."
            % (where.capitalize(),
               " and ".join("'%s'" % name for name in sources), singular))

    if not sources:
        raise Namespace2XmlError(
            "%s supplies none of 'file', 'text' or 'data'. An entry that contributes nothing "
            "would still be counted, so it cannot be read as a placeholder; say what the %s "
            "is, or drop the entry." % (where.capitalize(), singular))

    source = sources[0]
    fmt = entry.get("format", "namespace")

    if fmt not in formats:
        raise Namespace2XmlError(
            "%s asks for format %r, which is not one the tool reads for a %s. It takes %s. "
            "Every unrecognised extension falls back to namespace-profile parsing, so "
            "accepting this would read the %s as the wrong language rather than refuse it."
            % (where.capitalize(), fmt, singular, ", ".join(formats), singular))

    if source == "file":
        return _from_file(entry, where, argument)

    if source == "data":
        return _from_data(entry, where, position, singular, encode, extensions)

    return _from_text(entry, where, position, fmt, singular, extensions)


def _from_file(entry, where, argument):
    """A path, handed to the tool where it lies."""
    path = entry["file"]

    if not isinstance(path, str):
        raise Namespace2XmlError(
            "%s has 'file' as %s. It names a path, so it has to be a string."
            % (where.capitalize(), _kind(path)))

    if not path.strip():
        raise Namespace2XmlError(
            "%s has an empty 'file'. Name the path, or drop the entry." % where.capitalize())

    if "format" in entry:
        raise Namespace2XmlError(
            "%s sets 'format' alongside 'file'. The tool selects the parser from the "
            "extension of the file it is given, and this file is passed where it lies rather "
            "than copied, so a 'format' here could not be honoured -- it would be read as "
            "agreeing with the extension when it did not. Rename the file, or read it in the "
            "playbook and pass the contents as 'text' with the 'format' you mean."
            % where.capitalize())

    # `elements: raw` is what lets an entry be either a string or a mapping, and it costs the
    # `type: path` coercion the module would otherwise get for free. Doing it here keeps `~`
    # meaning the same thing on both plugins, which is the whole point of this module.
    return Entry(path=os.path.expanduser(path), text=None, name=path)


def _from_data(entry, where, position, singular, encode, extensions):
    """A structure, encoded here into the syntax the tool reads."""
    if "format" in entry:
        raise Namespace2XmlError(
            "%s sets 'format' alongside 'data'. A structure is encoded into the tool's own "
            "syntax by this collection, so there is no other parser left for it to reach. Use "
            "'text' to hand the tool a document you have written yourself."
            % where.capitalize())

    text, fmt = encode(entry["data"])

    return Entry(path=None, text=text,
                 name="%s-%d.%s" % (singular, position, extensions[fmt]))


def _from_text(entry, where, position, fmt, singular, extensions):
    """A document, written out under a name whose extension selects the parser.

    The name is not cosmetic. Section 7.1 and Section 15 both pick the parser from the
    extension, so an XML template handed over as text is read as XML only because it is written
    to a ``.xml`` name; and the tool quotes the resolved path in its own diagnostics, so these
    names are read by whoever has to go and fix the source they name.
    """
    text = entry["text"]

    if not isinstance(text, str):
        raise Namespace2XmlError(
            "%s has 'text' as %s. Text reaches the tool unparsed, so it has to be a string; "
            "'data' is the key for a structure." % (where.capitalize(), _kind(text)))

    return Entry(path=None, text=text,
                 name="%s-%d.%s" % (singular, position, extensions[fmt]))
