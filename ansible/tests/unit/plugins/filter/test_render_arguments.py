"""What the filter refuses, and why refusing is the only honest answer.

Every case here is one where the alternative to an error is a *successful* render of the wrong
thing. That is the failure mode this collection exists to prevent, and it is the one a test is
worth most against: a wrong document that arrives with exit 0 and no diagnostic is indistinguishable
from a right one until something downstream breaks.

The expectations are authored from the specification -- sections 8, 8.2, 8.3, 15.2 and 16.1 -- and
never captured from the filter. Where a spelling is pinned, section 19.1 is the authority for it.
"""

from __future__ import annotations

import json
import pathlib
import sys

import pytest

try:
    from ansible_collections.stop_cran.namespace2xml.plugins.filter import render as n2x
except ImportError:  # a checkout that is not inside an ansible_collections tree
    # Through `plugins` -- an implicit namespace package -- rather than by path: the filter
    # reaches module_utils by relative import, and a relative import needs a package. Loading
    # both halves through one package root is also what makes the shared module one object.
    _ANSIBLE = pathlib.Path(__file__).resolve().parents[4]

    if str(_ANSIBLE) not in sys.path:
        sys.path.insert(0, str(_ANSIBLE))

    from plugins.filter import render as n2x  # type: ignore[no-redef]


@pytest.fixture(autouse=True)
def _clean_identity_cache():
    """The identity cache is a module global, so a test that fills it would leak into the next."""
    n2x._IDENTITY_CACHE.clear()
    yield
    n2x._IDENTITY_CACHE.clear()


# --- Section 16.1: the format is an enumeration, not free text ----------------------------------

def test_every_section_16_1_format_is_accepted():
    for fmt in ("xml", "json", "yaml", "ini", "namespace", "quotednamespace"):
        assert n2x.synthesize_scheme(fmt) == "cfg.output=%s\n" % fmt


def test_a_format_outside_section_16_1_is_refused():
    """Left unchecked this reaches the tool as a declaration it does not recognise.

    The value of catching it here is the message: the filter can name the six formats, whereas
    the tool can only report that a scheme rule matched nothing.
    """
    with pytest.raises(n2x.Namespace2XmlError, match="section 16.1"):
        n2x.synthesize_scheme("Xml")


# --- Section 15.2: an injected record silently overrides the one above it -----------------------

@pytest.mark.parametrize("break_char", ["\n", "\r"], ids=["lf", "cr"])
def test_a_line_break_in_root_is_refused(break_char):
    """The whole point of the guard: a break in ``root`` appends a directive of the caller's choice.

    Section 15.2 makes the later declaration win, so 'configuration%scfg.output=json' produces a
    JSON document from a call that asked for XML, with exit 0 and nothing on stderr.
    """
    injected = "configuration%scfg.output=json" % break_char

    with pytest.raises(n2x.Namespace2XmlError, match="line break"):
        n2x.synthesize_scheme("xml", root=injected)


def test_a_root_is_otherwise_left_exactly_as_written():
    """Section 8.2 markers in a root must survive.

    ``\\@id`` is an escaped literal '@', which is how a caller addresses an element genuinely
    named with a leading '@'. Value-encoding the root would double the backslash and turn it into
    a literal backslash followed by an escape, so the root is interpolated verbatim and only
    checked for the one thing that cannot be expressed.
    """
    assert n2x.synthesize_scheme("xml", root="\\@id") == "cfg.output=xml\ncfg.root=\\@id\n"


def test_a_line_break_in_the_delimiter_is_encoded_rather_than_refused():
    """A section 8.3 value has an escape for this, so refusing would remove a capability for free.

    Section 19.1 spells a line feed inside an interpreted value '\\n', which is two characters and
    therefore cannot end the record.
    """
    scheme = n2x.synthesize_scheme("ini", delimiter="\n")

    assert scheme == "cfg.output=ini\ncfg.delimiter=\\n\n"
    assert scheme.count("\n") == 2


def test_a_tab_delimiter_takes_its_section_19_1_spelling():
    assert n2x.synthesize_scheme("ini", delimiter="\t") == "cfg.output=ini\ncfg.delimiter=\\t\n"


# --- Arguments an explicit scheme would swallow -------------------------------------------------

@pytest.mark.parametrize(
    ("argument", "value"),
    [("root", "configuration"), ("delimiter", ":")],
)
def test_a_synthesis_only_argument_alongside_an_explicit_scheme_is_refused(argument, value):
    """These are read only while synthesizing, so with a scheme supplied they reach nothing.

    Discarding them silently is the sharp edge: the render succeeds and returns a document in
    which the caller's root or delimiter simply is not there.
    """
    arguments = {"root": None, "delimiter": None}
    arguments[argument] = value

    with pytest.raises(n2x.Namespace2XmlError, match="explicit 'scheme'"):
        n2x._refuse_swallowed_arguments("cfg.output=xml\n", "xml", **arguments)


def test_a_scheme_declaring_a_different_output_than_the_format_asked_for_is_refused():
    """``fmt`` is a required positional, so a caller with a scheme is compelled to supply it.

    Before this check that answer was collected and thrown away. Cross-checking is what makes the
    compelled argument mean something.
    """
    with pytest.raises(n2x.Namespace2XmlError, match="declares output 'json'"):
        n2x._refuse_swallowed_arguments("cfg.output=json\n", "xml", None, None)


def test_a_scheme_agreeing_with_the_format_is_accepted():
    n2x._refuse_swallowed_arguments("cfg.output=xml\ncfg.root=configuration\n", "xml", None, None)


def test_a_scheme_declaring_no_output_is_left_to_the_tool():
    """Reading little on purpose: silence here is deferred judgement, not approval."""
    n2x._refuse_swallowed_arguments("cfg.appender.*.port.type=int\n", "xml", None, None)


# --- Section 8 record kinds, applied to the scheme this filter reads ----------------------------

@pytest.mark.parametrize(
    "scheme",
    [
        "#cfg.output=json\ncfg.output=xml\n",
        "   \t #cfg.output=json\ncfg.output=xml\n",
        "!cfg.output=json\ncfg.output=xml\n",
        "\ncfg.output=xml\n",
    ],
    ids=["comment", "indented-comment", "mask", "blank-record"],
)
def test_a_record_that_declares_nothing_is_not_read_as_a_declaration(scheme):
    """Section 8: an unescaped leading '#' is a comment and an unescaped leading '!' is a mask.

    Reading either as a declaration would reject a correct call, because the format argument
    would then be checked against a line the tool never applies. A blank record is ignored for
    the same reason. The parametrised id names the kind under test.
    """
    n2x._refuse_swallowed_arguments(scheme, "xml", None, None)


def test_an_escaped_hash_does_not_begin_a_comment():
    """Section 8 is explicit that '\\#' is not a comment marker, so this record does declare."""
    with pytest.raises(n2x.Namespace2XmlError, match="declares output 'json'"):
        n2x._refuse_swallowed_arguments("\\#cfg.output=json\n", "xml", None, None)


def test_an_escaped_dot_does_not_create_an_output_part():
    """Section 8.2: an escaped '.' is part of a name, not a separator.

    The last part of 'cfg.a\\.output' is the single part 'a.output', which is not an output
    declaration. Splitting naively would invent one and refuse a valid scheme.
    """
    n2x._refuse_swallowed_arguments("cfg.a\\.output=json\n", "xml", None, None)


# --- Sections 15 and 16.1: how an output declaration is compared --------------------------------

@pytest.mark.parametrize(
    "declared",
    ["XML", "Xml", " xml ", "xml,json", "json, xml", "JSON,XML"],
    ids=["upper", "mixed", "padded", "comma-first", "comma-second-padded", "comma-upper"],
)
def test_an_output_declaration_the_tool_would_accept_is_not_refused_here(declared):
    """Section 15 matches directive names under ASCII case-insensitive comparison, "as is every
    other name and value in the scheme language: formats in Section 16.1". Section 16.1 repeats
    it for this directive -- "names are case-insensitive", "whitespace around comma-separated
    values is ignored" -- and makes a comma-separated declaration name several formats, each
    with a left-to-right ordinal.

    Comparing the declaration as raw text refuses spellings the tool itself accepts. For the
    comma-separated ones it is worse than strict: it demands an agreement that cannot be
    written, because no section 16.1 format is spelled 'xml,json' and so no O(fmt) value could
    ever have satisfied it.

    Both spellings of a scheme are checked together. The mapping form carries its own copy of
    this comparison, and a copy is a place the two can quietly stop agreeing.
    """
    n2x._refuse_swallowed_arguments("cfg.output=%s\n" % declared, "xml", None, None)
    n2x._refuse_swallowed_arguments({"cfg": {"output": declared}}, "xml", None, None)


def test_the_format_argument_is_folded_on_its_side_of_the_comparison_too():
    """One clause governs both sides, so folding only the declaration would still refuse."""
    n2x._refuse_swallowed_arguments("cfg.output=xml\n", "XML", None, None)
    n2x._refuse_swallowed_arguments({"cfg": {"output": "xml"}}, "XML", None, None)


@pytest.mark.parametrize(
    "scheme",
    ["cfg.output=json,yaml\n", {"cfg": {"output": "json,yaml"}}],
    ids=["text", "mapping"],
)
def test_a_declaration_naming_neither_format_is_still_refused(scheme):
    """Normalizing the comparison must not quietly make it vacuous."""
    with pytest.raises(n2x.Namespace2XmlError, match="declares output"):
        n2x._refuse_swallowed_arguments(scheme, "xml", None, None)


def test_a_directive_name_is_matched_case_insensitively_in_a_mapping_too():
    """Section 15 folds the name as well as the value. Reading only the lowercase spelling let
    an 'OUTPUT:' declaration past the cross-check entirely, which is the silent half of the
    same defect."""
    with pytest.raises(n2x.Namespace2XmlError, match="declares output 'json'"):
        n2x._refuse_swallowed_arguments({"cfg": {"OUTPUT": "json"}}, "xml", None, None)


# --- Identity: the cache key must move when the binary does -------------------------------------

class _Version:
    def __init__(self, stdout):
        self.returncode = 0
        self.stdout = stdout
        self.stderr = ""


def _stub_version(monkeypatch, executable, texts):
    """Resolve to ``executable`` and answer each --version call from ``texts`` in turn."""
    calls = []

    def run(argv, **dummy):
        calls.append(argv)
        return _Version(texts[min(len(calls) - 1, len(texts) - 1)])

    monkeypatch.setattr(n2x, "_resolve", lambda tool: str(executable))
    monkeypatch.setattr(n2x.subprocess, "run", run)

    return calls


IDENTITY = "name: namespace2xml\nversion: %s\ncontract-bundle: %s\n"


def test_the_identity_is_cached_for_an_unchanged_binary(monkeypatch, tmp_path):
    binary = tmp_path / "namespace2xml"
    binary.write_text("first", encoding="utf-8")
    calls = _stub_version(monkeypatch, binary, [IDENTITY % ("3.0.0", "r99+aaaa")])

    assert n2x.tool_identity() == "3.0.0|r99+aaaa"
    assert n2x.tool_identity() == "3.0.0|r99+aaaa"
    assert len(calls) == 1


def test_upgrading_the_binary_in_place_invalidates_the_identity(monkeypatch, tmp_path):
    """``dotnet tool update --global`` rewrites the shim at the same path.

    A path-keyed cache would serve the pre-upgrade identity for the life of the process, and
    because the identity is part of the render key, pre-upgrade output with it. The README
    promises the render cache cannot survive a tool upgrade; this is that promise.
    """
    binary = tmp_path / "namespace2xml"
    binary.write_text("first", encoding="utf-8")
    calls = _stub_version(
        monkeypatch, binary,
        [IDENTITY % ("3.0.0", "r99+aaaa"), IDENTITY % ("3.1.0", "r100+bbbb")])

    assert n2x.tool_identity() == "3.0.0|r99+aaaa"

    binary.write_text("second and longer", encoding="utf-8")

    assert n2x.tool_identity() == "3.1.0|r100+bbbb"
    assert len(calls) == 2


def test_a_binary_without_a_contract_bundle_is_refused(monkeypatch, tmp_path):
    """A 2.x build is the case this exists for, and it does not announce itself.

    It accepts the same -i/-s/-o arguments and the same 'output' and 'root' scheme spellings, so
    it exits 0 and returns a document rendered under the 2.x contract. Every claim this collection
    makes is a claim about 3.x, so 'no contract-bundle' has to be an error rather than an unknown.
    """
    binary = tmp_path / "namespace2xml"
    binary.write_text("two-point-x", encoding="utf-8")
    _stub_version(monkeypatch, binary, ["namespace2xml 2.4.0\n"])

    with pytest.raises(n2x.Namespace2XmlError, match="--prerelease"):
        n2x.tool_identity()


def test_a_failure_points_at_the_report_address_the_binary_publishes(monkeypatch, tmp_path):
    """The message has to carry its own way out, because its reader is often an agent.

    Taking the address from the running build's --version rather than from a constant in this
    file means a report always names the tracker of the build that actually failed.
    """
    binary = tmp_path / "namespace2xml"
    binary.write_text("third", encoding="utf-8")
    _stub_version(
        monkeypatch, binary,
        [IDENTITY % ("3.0.0", "r99+aaaa") + "report: https://example.invalid/issues\n"])

    n2x.tool_identity()

    assert "https://example.invalid/issues" in n2x._support_hint(str(binary))
    assert "verbatim" in n2x._support_hint(str(binary))


def test_an_unknown_binary_contributes_no_hint_rather_than_a_broken_one(monkeypatch):
    assert n2x._support_hint("no-such-executable-anywhere") == ""


# --- Section 15: the mapping spelling of a scheme -----------------------------------------------

def test_a_mapping_scheme_carries_its_path_in_the_nesting():
    """Section 15 selects the parser from the extension, and a mapping scheme is a JSON document.

    The nesting is the path here, which is the whole point of the spelling: an author writing a
    playbook writes structure, not a dotted string that happens to live inside YAML.
    """
    text = n2x.encode_scheme_mapping({"cfg": {"output": "xml", "root": "configuration"}})

    assert json.loads(text) == {"cfg": {"output": "xml", "root": "configuration"}}


def test_a_mapping_scheme_keeps_the_order_it_was_written_in():
    """Section 15.2 gives scheme directives source order only, and no specificity ranking.

    A later matching directive overrides an earlier one, so sorting the keys would not be a
    cosmetic difference -- it would silently change which directive wins.
    """
    text = n2x.encode_scheme_mapping(
        {"cfg": {"appender": {"*": {"type": "ignore"}, "a": {"type": "element"}}}})

    assert list(json.loads(text)["cfg"]["appender"]) == ["*", "a"]


def test_a_dotted_key_in_a_mapping_scheme_is_refused_with_the_nested_spelling():
    """The trap: a dot separates names in the text form and is a literal character here.

    Section 9: a native mapping key is one component, and the delimiter "loses its meaning
    there, because a key is one part rather than a path". Left alone the tool echoes the name
    back with the dot escaped as \\u{2E}, and where the key is a selector rather than a
    directive name the render *succeeds* with only WARN009 and the directive inert.
    """
    with pytest.raises(n2x.Namespace2XmlError, match="cfg -> output"):
        n2x.encode_scheme_mapping({"cfg.output": "xml"})


def test_the_dotted_key_refusal_offers_both_the_nesting_and_the_escape():
    """An author reaching for 'a.b:' means one of exactly two things. Name both."""
    with pytest.raises(n2x.Namespace2XmlError) as failure:
        n2x.encode_scheme_mapping({"cfg.output": "xml"})

    message = str(failure.value)

    assert "cfg -> output" in message
    assert "cfg\\.output" in message
    assert "WARN009" in message


def test_an_escaped_dot_in_a_key_is_one_name_part_containing_a_dot():
    """Section 8 spells a literal dot '\\.' in the namespace form; the mapping form borrows it.

    YAML quoting cannot carry this: 'a.b', "a.b" and a.b all load to the same string and the
    quote style is discarded, so the escape has to live in the text.
    """
    document = json.loads(n2x.encode_scheme_mapping({"a\\.b": {"output": "xml"}}))

    assert list(document) == ["a.b"]
    assert document["a.b"] == {"output": "xml"}


def test_an_escaped_dot_is_not_confused_with_a_separator_in_the_same_key():
    """A key may carry both: 'a\\.b.c' is a literal-dot name followed by a separator.

    A hint is text the author is meant to paste back, so the literal dot has to stay escaped in
    it. 'a.b -> c' proposes a nesting whose own first key this converter refuses again, which is
    advice into a dead end. Both spellings the hint offers are exercised below, so the hint and
    what it proposes cannot drift apart: that is the check, and the wording is only how it is
    read.
    """
    with pytest.raises(n2x.Namespace2XmlError) as failure:
        n2x.encode_scheme_mapping({"a\\.b.c": {"output": "xml"}})

    message = str(failure.value)

    assert "a\\.b -> c" in message
    assert "a\\.b\\.c" in message

    nested = json.loads(n2x.encode_scheme_mapping({"a\\.b": {"c": {"output": "xml"}}}))
    escaped = json.loads(n2x.encode_scheme_mapping({"a\\.b\\.c": {"output": "xml"}}))

    assert nested == {"a.b": {"c": {"output": "xml"}}}
    assert escaped == {"a.b.c": {"output": "xml"}}


# --- Section 11.4: a dot inside Q{...} is URI text ----------------------------------------------

@pytest.mark.parametrize(
    "key",
    ["Q{urn:example.com}name", "@Q{urn:example.com}x", "Q{urn:a.b.c}n", "Q{urn:x\\}y.z}n"],
    ids=["element", "attribute", "several-dots", "escaped-brace"],
)
def test_a_dot_inside_a_qualified_name_is_uri_text_and_is_left_alone(key):
    """Section 8 lists 'Q{uri}x' among the markers a native key may carry, and section 11.4 says
    "dots inside Q{...} are part of the URI and do not split the qualified path". It closes the
    URI at the first unescaped '}', so an escaped one does not end it, and it spells an
    attribute '@Q{urn:p}x'.

    Refusing such a key rejects a name the tool accepts, and here the hint would be worse than
    the refusal: section 8 makes marker recognition committing, so the 'Q{urn:example' the hint
    would propose is PARSE001 rather than a part the author can retreat to.
    """
    document = json.loads(n2x.encode_scheme_mapping({"cfg": {key: {"type": "ignore"}}}))

    assert list(document["cfg"]) == [key]


def test_a_dot_after_the_closing_brace_is_ambiguous_like_any_other():
    """The URI ends at the first unescaped '}', and the local name that follows is ordinary
    text, so a dot in it separates nothing and is refused as everywhere else. The hint has to
    describe that split without also splitting the URI it left behind.
    """
    with pytest.raises(n2x.Namespace2XmlError) as failure:
        n2x.encode_scheme_mapping({"cfg": {"Q{urn:e.g}a.b": {"type": "ignore"}}})

    message = str(failure.value)

    assert "Q{urn:e.g}a -> b" in message
    assert "urn:e -> " not in message


def test_an_unterminated_marker_is_left_for_the_tool_to_report():
    """Section 8: recognition commits once 'Q{' is seen, and an unterminated one is PARSE001.

    That is a loud refusal naming the record, not the silent wrong answer this converter exists
    to prevent, so the key is passed through rather than second-guessed with a hint that would
    have to invent where the URI was meant to end.
    """
    document = json.loads(n2x.encode_scheme_mapping({"cfg": {"Q{urn:x": {"type": "ignore"}}}))

    assert list(document["cfg"]) == ["Q{urn:x"]


def test_an_escaped_marker_is_an_ordinary_part_and_its_dots_are_not_uri_text():
    """Section 8: an escaped marker is literal, so '\\Q{...}' is an ordinary part and the dot
    rule applies to it in full. Treating the escape as a marker would carry the exception into
    a key that never asked for it."""
    with pytest.raises(n2x.Namespace2XmlError, match="contains a dot"):
        n2x.encode_scheme_mapping({"cfg": {"\\Q{urn:e.g}n": {"type": "ignore"}}})


# --- Section 9.1: a backslash before anything but a dot is not an escape ------------------------

@pytest.mark.parametrize(
    "key",
    ["C:\\dir", "a\\b", "\\\\@x", "a\\\\b"],
    ids=["windows-path", "mid-key", "escaped-escape-then-marker", "doubled"],
)
def test_a_backslash_that_escapes_nothing_reaches_the_tool_unchanged(key):
    """Section 9.1: "Elsewhere in the key, and before any other character, a backslash
    contributes itself and consumes nothing, so a key such as C:\\dir needs no escaping". Only
    '\\.' is decoded here, and only a leading backslash suppresses a marker.

    Rewriting any other backslash would corrupt a name this converter is only meant to carry,
    and the section 9.1 rules that do apply are the tool's to apply, not this converter's to
    anticipate.
    """
    document = json.loads(n2x.encode_scheme_mapping({"cfg": {key: {"type": "ignore"}}}))

    assert list(document["cfg"]) == [key]


# --- A scheme of the wrong shape is refused by name, not by accident ----------------------------

@pytest.mark.parametrize(
    "scheme",
    [[], 3, True, 3.5, ["cfg.output=xml"], [{"cfg": {"output": "xml"}}]],
    ids=["empty-list", "int", "bool", "float", "list-of-text", "list-of-mappings"],
)
def test_a_scheme_that_is_neither_a_mapping_nor_text_is_refused_by_name(scheme):
    """The O(fmt) cross-check runs before the converter, and it used to read anything that was
    not a mapping as text and call splitlines() on it. A playbook variable arrives in whatever
    shape the playbook gave it -- documented argument types are not runtime validation -- so a
    list reached that call and the author met an AttributeError from inside the plugin instead
    of the sentence the converter had ready for exactly this mistake.

    The cross-check abstains on a shape it cannot read, which lets that sentence be the one the
    author sees.
    """
    with pytest.raises(n2x.Namespace2XmlError, match="must be a mapping"):
        n2x.render({"k": "v"}, "xml", scheme_yaml=scheme)


def test_a_bare_dot_key_is_refused_rather_than_read_as_an_empty_path():
    with pytest.raises(n2x.Namespace2XmlError):
        n2x.encode_scheme_mapping({"cfg": {".": "xml"}})


def test_a_backslash_that_does_not_precede_a_dot_survives_unchanged():
    """Section 9 keeps '\\@x' as an escaped marker. Only '\\.' is consumed here."""
    document = json.loads(n2x.encode_scheme_mapping({"cfg": {"\\@x": "element"}}))

    assert list(document["cfg"]) == ["\\@x"]


def test_a_list_directive_value_is_refused_naming_the_comma_spelling():
    """Section 15 wants a nonempty scalar, and YAML invites a list for a multi-valued directive."""
    with pytest.raises(n2x.Namespace2XmlError, match="'xml,json'"):
        n2x.encode_scheme_mapping({"cfg": {"output": ["xml", "json"]}})


def test_a_directive_written_with_no_value_is_refused():
    """A YAML key with nothing after the colon is null, which section 15 has no scalar for."""
    with pytest.raises(n2x.Namespace2XmlError, match="no value"):
        n2x.encode_scheme_mapping({"cfg": {"output": None}})


def test_a_number_shaped_directive_value_is_refused_rather_than_silently_shortened():
    """YAML reads 3.10 as a number, and a number does not remember its trailing zero.

    Stringifying it would put a value in the scheme that the author did not write, which is the
    quiet-wrong-answer failure this suite exists to catch.
    """
    with pytest.raises(n2x.Namespace2XmlError, match="Quote it"):
        n2x.encode_scheme_mapping({"cfg": {"filename": 3.10}})


def test_a_wildcard_selector_survives_a_mapping_scheme_unescaped():
    """Section 15 wildcards are matched, not literal, so the key must not be name-escaped.

    encode_name_part exists for data names and escapes an asterisk on purpose. Reusing it here
    would turn every wildcard rule into a rule matching one literal asterisk.
    """
    text = n2x.encode_scheme_mapping({"cfg": {"*": {"type": "ignore"}}})

    assert "*" in json.loads(text)["cfg"]
    assert "\\*" not in text


def test_a_mapping_scheme_declaring_a_different_output_than_the_format_is_refused():
    """The O(fmt) cross-check has to read both spellings, or it silently stops working for one."""
    with pytest.raises(n2x.Namespace2XmlError, match="declares output 'json'"):
        n2x._refuse_swallowed_arguments({"cfg": {"output": "json"}}, "xml", None, None)


def test_a_mapping_scheme_agreeing_with_the_format_is_accepted():
    n2x._refuse_swallowed_arguments({"cfg": {"output": "xml"}}, "xml", None, None)


def test_a_synthesis_only_argument_alongside_a_mapping_scheme_names_the_mapping_spelling():
    """Naming the '<selector>.root=...' fix at an author who wrote a mapping names a fix they
    cannot apply as written."""
    with pytest.raises(n2x.Namespace2XmlError, match="nested under the selector"):
        n2x._refuse_swallowed_arguments(
            {"cfg": {"output": "xml"}}, "xml", "configuration", None)


def test_supplying_both_scheme_spellings_is_refused():
    """They are one argument with two spellings, so both together leaves the render ambiguous."""
    with pytest.raises(n2x.Namespace2XmlError, match="two spellings"):
        n2x.render({"k": "v"}, "xml", scheme="cfg.output=xml\n",
                   scheme_yaml={"cfg": {"output": "xml"}})
