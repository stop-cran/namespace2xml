"""The section 8.3 profile encoder: arbitrary data in, namespace-profile text out.

This lives in ``module_utils`` because both callers need it and only one of them could hold
it. A module is shipped to the target host with its collection's ``module_utils`` and nothing
else -- it cannot import a filter plugin -- so an encoder kept in ``plugins/filter/render.py``
is reachable from the controller and nowhere else. Putting it here is what lets the same
``data:`` entry mean the same thing whether a play writes it against the filter or the module.

It is the same argument, and the same resolution, as issue #107: shared code is imported, not
copied. The filter re-exports every public name below, so ``filt.flatten is profile.flatten``
-- there is no second copy to drift.

Errors raised here are the shared :class:`Namespace2XmlError`. The filter converts them to its
own ``AnsibleFilterError``-derived subclass at the one boundary where that matters, which is
:func:`~ansible_collections.stop_cran.namespace2xml.plugins.filter.render.render`.
"""

from __future__ import annotations

import unicodedata

from .n2x import Namespace2XmlError, encode_value

__all__ = [
    "CONVENTIONS",
    "DEFAULT_CONVENTION",
    "DEFAULT_SELECTOR",
    "encode_name_part",
    "encode_scalar",
    "encode_xml_name_part",
    "flatten",
]


DEFAULT_SELECTOR = "cfg"

CONVENTIONS = ("escaped", "xmltodict")
DEFAULT_CONVENTION = "escaped"

_XMLTODICT = "xmltodict"
_XML_TEXT_KEY = "#text"

# A leading backslash is consumed before any of these. The three markers are section 11.4's;
# the backslash is itself, which is what keeps the encoding total -- without it a key that
# genuinely starts with a backslash and a marker would have no spelling, and the objection
# raised against this whole approach in issue #103 was that it trades one class of
# unrepresentable data for another.
_XML_ESCAPABLE = frozenset("@#Q\\")

_DIGITS = frozenset("0123456789")

_NAME_SHORT_ESCAPES = frozenset(".*=#!$@}")
_FORCED_HEX = frozenset("\u0085\u2028\u2029")


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


def _qname_span(name):
    """Index just past the ``}`` that closes a leading ``Q{``, or ``None`` if nothing closes it.

    Section 11.4: "the first unescaped ``}`` closes the URI; a literal ``}`` inside the URI is
    written as ``\\}``". A backslash inside the URI therefore consumes the character after it,
    which is what stops ``Q{a\\}b}c`` from ending at the brace the author escaped.
    """
    if not name.startswith("Q{"):
        return None

    index = 2

    while index < len(name):
        if name[index] == "\\":
            index += 2
        elif name[index] == "}":
            return index + 1
        else:
            index += 1

    return None


def _checked_uri(uri, part):
    """Return a namespace URI unchanged, refusing an escape section 11.4 does not define.

    The URI is passed through rather than escaped. Inside ``Q{...}`` section 11.4 suspends
    ordinary name escaping and defines exactly two sequences -- ``\\}`` for a literal closing
    brace and ``\\\\`` for a literal backslash -- so the text between the braces is already in
    the tool's own spelling and re-escaping it would change the URI rather than preserve it.
    That also means the two characters that would otherwise be unwritable stay writable.

    The cost of passing through is that a stray backslash reaches the tool, where section 11.4
    makes it "a blocking parse error". Checking here turns that into a message naming the key,
    which the tool cannot do: it sees a synthesized profile in a temporary directory and reports
    a path the caller never wrote.
    """
    index = 0

    while index < len(uri):
        if uri[index] != "\\":
            index += 1
            continue

        following = uri[index + 1] if index + 1 < len(uri) else ""

        if following not in ("}", "\\"):
            raise Namespace2XmlError(
                "The key '%s' has a backslash inside its 'Q{...}' namespace URI that starts no "
                "escape section 11.4 defines. Only '\\}' for a literal closing brace and '\\\\' "
                "for a literal backslash are escapes there; anything else is a blocking parse "
                "error. Write '\\\\' if a single backslash is part of the URI." % part)

        index += 2

    return uri


def _encode_qualified(name, part):
    """Encode a possibly namespace-qualified XML name -- ``Q{uri}local``, or a bare local name.

    Section 11.4 spells a namespace-qualified name ``Q{namespace-uri}local-name`` and gives the
    two halves different rules: the URI is one atomic lexer context where "delimiter, wildcard,
    reference, and ordinary name-escape recognition is suspended", while "the following local
    name uses ordinary name escaping". Encoding both alike would corrupt one of them -- escaping
    the dots in ``urn:example.com`` breaks the URI, and leaving a dot unescaped in the local
    name splits one name into two.
    """
    if not name.startswith("Q{"):
        return encode_name_part(name)

    span = _qname_span(name)

    if span is None:
        raise Namespace2XmlError(
            "The key '%s' opens a 'Q{' namespace marker and never closes it. Section 11.4 ends "
            "the URI at the first unescaped '}', and section 8 makes marker recognition "
            "committing, so the tool refuses this with PARSE001 rather than reading it as an "
            "ordinary name. Close the brace, or write '\\%s' if a name that merely begins with "
            "'Q' is what you meant." % (part, part))

    return ("Q{" + _checked_uri(name[2:span - 1], part) + "}"
            + encode_name_part(name[span:]))


def _canonical_index(text):
    """Whether ``text`` is a decimal integer written the one way section 8.7 counts as canonical."""
    if not text or not all(char in _DIGITS for char in text):
        return False

    return text == "0" or text[0] != "0"


def encode_xml_name_part(part):
    """Encode one mapping key under the ``xmltodict`` convention, where XML markers are live.

    The default encoding is total: section 11.4's three markers are escaped in every position,
    so any key reads back as itself and none of them can address anything. That is the right
    default and it is also why an attribute, a namespaced element and a content token are
    unreachable from a plain mapping -- issue #103. This encoding trades totality for reach in
    the way the ecosystem already spells it, so a mapping written for ``xmltodict``, for
    ``community.general.to_xml`` or by hand in the badgerfish style means here what it means
    there.

    Four keys are read rather than escaped, each mapping to a section 11.4 canonical spelling:

    - ``@x`` and ``@Q{uri}x`` are attributes, "an attribute is prefixed with ``@``";
    - ``Q{uri}x`` is an element in a namespace;
    - ``#0``, ``#1`` and so on are content tokens, "every content node uses an ordered part";
    - ``#text`` is the element's own text, which :func:`_walk` resolves because section 11.4
      places that scalar at the *element* path rather than at a part of its own.

    Totality is not lost, only moved: a key whose name really does start with one of the three
    markers is written with a backslash before it, ``\\@x``, exactly as section 8 spells a
    literal in the namespace form and as ``scheme_yaml`` already spells a literal dot. A leading
    backslash escapes itself the same way, ``\\\\@x`` for the name ``\\@x``, so every key still
    has exactly one spelling and nothing has become unwritable. A scheme key and an input key
    have been two different languages since ``*`` -- a wildcard in one, an escaped literal in
    the other -- and this follows that line.

    A ``#`` key that is neither ``#text`` nor a canonical index is refused rather than escaped.
    Escaping it would be the silent wrong answer this convention exists to remove: the author
    wrote a marker, and a name part is not what they meant.
    """
    if part == "":
        raise Namespace2XmlError("an empty name part is a parse error (section 8.2)")

    if len(part) > 1 and part[0] == "\\" and part[1] in _XML_ESCAPABLE:
        return encode_name_part(part[1:])

    if part.startswith("@"):
        if part == "@":
            raise Namespace2XmlError(
                "The key '@' names an attribute with no name. Section 11.4 spells an attribute "
                "'@x', so write the attribute's name after the '@', or '\\@' for a name part "
                "that is a single literal at-sign.")

        return "@" + _encode_qualified(part[1:], part)

    if part.startswith("Q{"):
        return _encode_qualified(part, part)

    if part.startswith("#"):
        if _canonical_index(part[1:]):
            return part

        raise Namespace2XmlError(
            "The key '%s' starts with '#', which section 11.4 reserves for content. A content "
            "token is '#' and a canonical index -- '#0', '#1' -- and '#text' is an element's "
            "own text. Neither fits '%s'. Write '\\%s' for a name part that literally starts "
            "with '#'." % (part, part, part))

    return encode_name_part(part)


def _competes_with_text(key):
    """Whether a sibling key occupies a position among its element's content.

    Section 11.4 gives every content node of a mixed element an ordered part, so text standing
    beside child elements needs to say *where* it stands. Attributes do not compete: they are
    not content, and section 11.4 has attribute and child-element names never colliding.
    """
    if len(key) > 1 and key[0] == "\\" and key[1] in _XML_ESCAPABLE:
        return True

    return not key.startswith("@")


def _reject_positionless_text(node, name):
    """Refuse ``#text`` beside content, where section 11.4 needs a position the mapping lacks.

    ``#text`` works because of a rule with a precondition: section 11.4 exposes a run of text as
    the scalar at the element path only for "an element with no child elements and exactly one
    non-comment text or CDATA node", and says outright that such a run "is not addressable as
    ``#n``". Where the element also has child elements it is mixed, "every content node uses its
    ``#n`` wrapper", and the scalar moves off the element path.

    A mapping cannot say which ``#n``. It carries one ``#text`` for what may have been several
    runs, and its key order records the order the author typed rather than the order the text
    and the elements stood in. Choosing an index would be inventing a document, and the wrong
    guess is not loud -- it renders, exits 0, and puts the text on the wrong side of a child.
    So this refuses and names the spelling that does carry the position.
    """
    competing = sorted(str(key) for key in node
                       if str(key) != _XML_TEXT_KEY and _competes_with_text(str(key)))

    if not competing:
        return

    raise Namespace2XmlError(
        "'%s' has '#text' beside %s, which makes it mixed content, and mixed content needs to "
        "say where the text stands. Section 11.4 gives an element's own text the element's path "
        "only when it has no child elements; once it does, every content node takes an ordered "
        "part and the text is one of them. A mapping does not record that order, so guessing "
        "one would render successfully with the text on the wrong side of a child. Write the "
        "content tokens instead -- '#0' for the text, '#1' for what follows it -- or move the "
        "text into its own element."
        % (name, ", ".join("'" + key + "'" for key in competing)))


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


def flatten(config, selector=DEFAULT_SELECTOR, convention=DEFAULT_CONVENTION):
    """Flatten a dictionary into a namespace profile rooted at ``selector``.

    An empty mapping or sequence becomes the section 8.3 sentinel rather than disappearing,
    which is the difference between an emitted ``<empty />`` and no element at all.

    ``convention`` chooses how a mapping key is read. Under ``escaped``, the default, a key is
    data and every section 11.4 marker in it is escaped, so the profile says exactly what the
    mapping said. Under ``xmltodict`` the markers are live -- see :func:`encode_xml_name_part`.
    ``selector`` is escaped as an ordinary name part either way: it is an argument naming where
    the data hangs, not data, and it has to match the selector the scheme declares.
    """
    _check_convention(convention)

    records = []
    _walk(config, [encode_name_part(selector)], records, convention)

    return "".join(record + "\n" for record in records)


def _check_convention(convention):
    if convention in CONVENTIONS:
        return

    raise Namespace2XmlError(
        "'%s' is not a key convention this filter knows. Use %s. The list is closed on purpose: "
        "a convention decides whether '@id' is an attribute or a name part, so an unrecognized "
        "one has to fail rather than fall back to a default and render something the caller did "
        "not ask for."
        % (convention, " or ".join("'" + name + "'" for name in CONVENTIONS)))


def _walk(node, path, records, convention=DEFAULT_CONVENTION):
    name = ".".join(path)
    encode = encode_xml_name_part if convention == _XMLTODICT else encode_name_part

    if isinstance(node, dict):
        if not node:
            records.append(name + "={}")
            return

        text_keys = ([key for key in node if str(key) == _XML_TEXT_KEY]
                     if convention == _XMLTODICT else [])

        if text_keys:
            _reject_positionless_text(node, name)

        for key, value in node.items():
            if key in text_keys:
                # Section 11.4 puts an only-child text run at the element's own path, where it
                # "is not addressable as #n", so this emits the parent's record rather than
                # descending. The refusal above has already established there is no child to
                # order it against.
                if isinstance(value, (dict, list, tuple)):
                    raise Namespace2XmlError(
                        "'%s' gives '#text' a %s. Section 11.4 makes an element's own text one "
                        "run of characters standing at the element's path, so it holds a scalar "
                        "-- there is no path left underneath it for members to hang from."
                        % (name, "mapping" if isinstance(value, dict) else "sequence"))

                records.append(name + "=" + encode_scalar(value))
                continue

            _walk(value, path + [encode(str(key))], records, convention)

        return

    if isinstance(node, (list, tuple)):
        if not node:
            records.append(name + "=[]")
            return

        for index, value in enumerate(node):
            # Section 8.7: a canonical decimal integer part makes the parent a sequence.
            # str(int) has no leading zero, and a leading zero would disable inference for the
            # whole parent.
            _walk(value, path + [str(index)], records, convention)

        return

    records.append(name + "=" + encode_scalar(node))
