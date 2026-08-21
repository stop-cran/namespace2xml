"""Side A of the oracle: the encoder against profile text authored from the specification.

The expectations here are written by hand from sections 8.2, 8.3, 8.7 and 18. They are never
regenerated from ``flatten``, and never captured from the tool. A captured expectation asserts
only that the code still does what it did, which is the one thing that needs no test.

Side B -- the tool run by hand over the same profile text, compared against what ``render``
produced from the data -- needs the binary and lives in
``tests/integration/targets/render``.
"""

from __future__ import annotations

import ast
import pathlib
import sys

import pytest

# `filt` is the filter under test; `n2x` is the module_utils it calls into, named for the
# file it is. Discovery, the contract-bundle gate and the section 8.3 encoder live in `n2x`,
# so a test that stands in for one of those patches it there -- patching the filter would
# leave the code under test calling the real thing.
try:
    from ansible_collections.stop_cran.namespace2xml.plugins.filter import render as filt
    from ansible_collections.stop_cran.namespace2xml.plugins.module_utils import n2x
    from ansible_collections.stop_cran.namespace2xml.plugins.module_utils import profile
except ImportError:  # a checkout that is not inside an ansible_collections tree
    # Through `plugins` -- an implicit namespace package -- rather than by path: the filter
    # reaches module_utils by relative import, and a relative import needs a package. Loading
    # both halves through one package root is also what makes the shared module one object.
    _ANSIBLE = pathlib.Path(__file__).resolve().parents[4]

    if str(_ANSIBLE) not in sys.path:
        sys.path.insert(0, str(_ANSIBLE))

    from plugins.filter import render as filt  # type: ignore[no-redef]
    from plugins.module_utils import n2x  # type: ignore[no-redef]
    from plugins.module_utils import profile  # type: ignore[no-redef]


def test_the_filter_shares_the_encoders_rather_than_carrying_its_own():
    """One object, not two that agree today.

    These encoders were duplicated for as long as a filter was believed unable to import its
    collection's ``module_utils``, and the duplication was policed by comparing the two copies
    -- first by corpus, then by source text, after the corpus twice failed to notice a value
    that was wrong in both. Identity retires both gates: there is nothing left to drift.

    Asserted with ``is`` on purpose. Equality of behaviour is what a reintroduced copy would
    also satisfy on the day it was written, which is exactly when the comparison is useless.

    The Section 8.3 encoder proper moved to ``module_utils.profile`` for the same reason the
    value encoders did, one caller later: a module ships with ``module_utils`` and cannot
    import a filter, so an encoder held in the filter is reachable from the controller only.
    Everything the filter still names is a re-export, and these assertions are what says so.
    """
    assert filt.encode_value is n2x.encode_value
    assert filt.encode_scheme_mapping is n2x.encode_scheme_mapping
    assert filt.flatten is profile.flatten
    assert filt.encode_name_part is profile.encode_name_part
    assert filt.encode_xml_name_part is profile.encode_xml_name_part


def test_the_shared_encoder_does_not_depend_on_the_controller():
    """``profile`` has to stay importable where no ansible is installed.

    A module runs on the target host and ships with ``module_utils`` alone, which is the whole
    reason the encoder moved out of the filter. An ``import ansible...`` added here later would
    not be caught by the rest of this suite -- it runs on a controller, where the import
    succeeds -- and would fail on the node instead, at the point of use. Reading the module's
    own import statements is what catches it, on any machine.
    """
    source = pathlib.Path(profile.__file__).read_text(encoding="utf-8")

    imported = set()
    for node in ast.walk(ast.parse(source)):
        if isinstance(node, ast.Import):
            imported.update(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.level == 0 and node.module:
            imported.add(node.module)

    assert not [name for name in imported if name.split(".")[0] == "ansible"], (
        "the shared encoder must not import ansible: it ships to nodes that have none")


# (id, data, hand-authored profile). The profile column is the thing under test.
CASES = [
    (
        "nesting-sequence-types-empty-mapping",
        {
            "appender": [{"name": "STDOUT", "level": "DEBUG"}, {"name": "FILE"}],
            "enabled": True,
            "retries": 3,
            "empty": {},
        },
        # Section 8.7: the canonical decimal parts 0 and 1 make 'appender' a sequence.
        # Section 18: 'true' is a boolean and '3' an integer, from the value text alone.
        "cfg.appender.0.name=STDOUT\n"
        "cfg.appender.0.level=DEBUG\n"
        "cfg.appender.1.name=FILE\n"
        "cfg.enabled=true\n"
        "cfg.retries=3\n"
        "cfg.empty={}\n",
    ),
    (
        "name-parts-that-would-be-delimiters-wildcards-or-typed-components",
        {
            "a.b": "x",
            "with*star": "y",
            "Q{urn}": "z",
            "@attr": "w",
            "#1": "v",
            "tab\tkey": "u",
            "vt\u000bkey": "s",
            "zwsp\u200bkey": "t",
        },
        # Section 8.2: '.' '*' '@' '#' and '}' take their short escapes; a leading 'Q' takes
        # '\\Q' so the part is not read as a Q{...} canonical component; a Cc or Cf scalar has
        # no short form and takes '\\u{HEX}'.
        #
        # Section 8.2 accepts either case on input, so the tool reads '\\u{b}' and '\\u{B}'
        # alike and side B cannot see the difference. The uppercase spelling is what section
        # 19.1 emits, and it is pinned here, on the side that compares text. U+0009 alone would
        # not pin it -- '9' is the same in both cases, which is why U+000B and U+200B are here.
        "cfg.a\\.b=x\n"
        "cfg.with\\*star=y\n"
        "cfg.\\Q{urn\\}=z\n"
        "cfg.\\@attr=w\n"
        "cfg.\\#1=v\n"
        "cfg.tab\\u{9}key=u\n"
        "cfg.vt\\u{B}key=s\n"
        "cfg.zwsp\\u{200B}key=t\n",
    ),
    (
        "values-with-backslashes-wildcards-reference-starts-newlines-and-sentinels",
        {
            "backslash": "C:\\Users\\alice",
            "star": "a*b",
            "ref": "${x}",
            "newline": "l1\nl2",
            "brace": "{}",
            "bracket": "[]",
            "emptylist": [],
            "nil": None,
        },
        # Section 8.3: '\\' '*' and '${' are escaped; a record is a line so LF is written
        # '\\n'; a value of exactly '{}' or '[]' is the empty-container sentinel, so the
        # *string* takes '\\{}' and '\\[]'. An empty list is the sentinel itself. None is the
        # null payload.
        "cfg.backslash=C:\\\\Users\\\\alice\n"
        "cfg.star=a\\*b\n"
        "cfg.ref=\\${x}\n"
        "cfg.newline=l1\\nl2\n"
        "cfg.brace=\\{}\n"
        "cfg.bracket=\\[]\n"
        "cfg.emptylist=[]\n"
        "cfg.nil=null\n",
    ),
]


@pytest.mark.parametrize(("data", "profile"), [case[1:] for case in CASES],
                         ids=[case[0] for case in CASES])
def test_flatten_matches_the_specification(data, profile):
    assert filt.flatten(data, "cfg") == profile


def test_a_tuple_is_a_sequence_like_a_list():
    assert filt.flatten({"a": ("x", "y")}, "cfg") == "cfg.a.0=x\ncfg.a.1=y\n"


def test_an_empty_name_part_is_a_parse_error():
    with pytest.raises(n2x.Namespace2XmlError, match="empty name part"):
        filt.encode_name_part("")


def test_binary_data_is_refused_rather_than_guessed_at():
    with pytest.raises(n2x.Namespace2XmlError, match="binary data"):
        filt.flatten({"blob": b"\x00\x01"}, "cfg")


def test_q_is_escaped_only_where_it_could_open_a_canonical_component():
    # 'Q{' can introduce a section 11.4 component at the start of a part and nowhere else, so
    # escaping it elsewhere would spend an escape to prevent nothing.
    assert filt.encode_name_part("Qx") == "\\Qx"
    assert filt.encode_name_part("xQ") == "xQ"


def test_a_lone_dollar_is_not_a_reference_start():
    # Section 8.3 escapes '${', not '$'. Escaping the bare character would change text that
    # round-trips perfectly well as itself.
    assert filt.encode_value("costs $5") == "costs $5"
    assert filt.encode_value("$ {x}") == "$ {x}"


def test_the_container_sentinels_are_escaped_only_when_they_are_the_whole_value():
    assert filt.encode_value("{}") == "\\{}"
    assert filt.encode_value("a{}b") == "a{}b"


def test_the_synthesized_scheme_declares_only_what_was_asked_for():
    assert filt.synthesize_scheme("json", "cfg") == "cfg.output=json\n"
    assert filt.synthesize_scheme("xml", "cfg", "configuration") == (
        "cfg.output=xml\ncfg.root=configuration\n")
    assert filt.synthesize_scheme("ini", "cfg", None, ":") == "cfg.output=ini\ncfg.delimiter=:\n"


def test_a_selector_needing_escapes_is_escaped_in_both_the_profile_and_the_scheme():
    assert filt.flatten({"k": "v"}, "my.app") == "my\\.app.k=v\n"
    assert filt.synthesize_scheme("json", "my.app") == "my\\.app.output=json\n"


# ---------------------------------------------------------------------------------------------
# The `xmltodict` convention (issue #103). Profiles below are authored from section 11.4, which
# gives the four canonical spellings: '@' prefixes an attribute, 'Q{uri}local' qualifies a name,
# '#n' orders a content node, and an only-child text run stands at the element's own path.
# ---------------------------------------------------------------------------------------------

# (id, data, hand-authored profile).
XML_CASES = [
    (
        "attributes-plain-and-qualified",
        {"bean": {"@id": "ds", "@Q{urn:p}scope": "singleton"}},
        # Section 11.4: "an attribute is prefixed with '@'". The name after the marker is an
        # ordinary name part, and a qualified one keeps its Q{...} wrapper.
        "cfg.bean.@id=ds\n"
        "cfg.bean.@Q{urn:p}scope=singleton\n",
    ),
    (
        "a-uri-keeps-its-dots-and-the-local-name-does-not",
        {"Q{urn:example.com/ns}a.b": "v"},
        # Section 11.4 makes Q{...} one atomic lexer context where "delimiter, wildcard,
        # reference, and ordinary name-escape recognition is suspended", so the dots in the URI
        # stand as themselves. "The following local name uses ordinary name escaping", so the
        # dot after the closing brace is escaped or it would split the path.
        "cfg.Q{urn:example.com/ns}a\\.b=v\n",
    ),
    (
        "a-brace-inside-the-uri-is-passed-through-already-escaped",
        {"Q{urn:a\\}b}c": "v"},
        # Section 11.4: "the first unescaped '}' ends the URI; '\\}' encodes a literal closing
        # brace". The URI is not re-escaped on the way through -- it is already in the tool's
        # spelling, and escaping it again would change the URI rather than preserve it.
        "cfg.Q{urn:a\\}b}c=v\n",
    ),
    (
        "own-text-stands-at-the-element-path-beside-its-attributes",
        {"bean": {"@id": "ds", "#text": "hello"}},
        # Section 11.4: an element with no child elements and one text run "exposes that run as
        # the scalar at the element path rather than as a content node", and the run "is not
        # addressable as '#n'". So this is 'cfg.bean=hello', never 'cfg.bean.#0=hello'.
        # Attributes are not child elements, so they do not disturb it.
        "cfg.bean.@id=ds\n"
        "cfg.bean=hello\n",
    ),
    (
        "content-tokens-order-mixed-content-and-nest",
        {"a": {"#0": "before", "#1": {"b": "child"}, "#2": "after"}},
        # Section 11.4: "every content node uses an ordered part", and "a mixed-content child
        # element is addressed as '#n.element-name'".
        "cfg.a.#0=before\n"
        "cfg.a.#1.b=child\n"
        "cfg.a.#2=after\n",
    ),
    (
        "a-backslash-restores-every-name-the-markers-took",
        {"\\@x": "a", "\\#0": "b", "\\Q{urn}y": "c", "\\\\@x": "d", "\\z": "e"},
        # The escape hatch. Consuming one backslash before a marker leaves the rest to the
        # section 8.2 encoder, which escapes the marker as a literal. The backslash escapes
        # itself too, so '\\\\@x' is the name '\\@x' and nothing is unwritable. A backslash
        # before anything else is not a marker escape, so '\\z' is the name '\\z'.
        "cfg.\\@x=a\n"
        "cfg.\\#0=b\n"
        "cfg.\\Q{urn\\}y=c\n"
        "cfg.\\\\\\@x=d\n"
        "cfg.\\\\z=e\n",
    ),
    (
        "an-unmarked-key-is-encoded-exactly-as-it-is-by-default",
        {"a.b": "x", "with*star": "y", "Qx": "z"},
        # Only the markers change meaning. Everything else keeps the section 8.2 encoding, so
        # switching convention cannot quietly alter a key that was never about XML. 'Q' not
        # followed by '{' opens nothing, so it is escaped as the literal it always was.
        "cfg.a\\.b=x\n"
        "cfg.with\\*star=y\n"
        "cfg.\\Qx=z\n",
    ),
]


@pytest.mark.parametrize(("data", "profile"), [case[1:] for case in XML_CASES],
                         ids=[case[0] for case in XML_CASES])
def test_the_xmltodict_convention_matches_the_specification(data, profile):
    assert filt.flatten(data, "cfg", "xmltodict") == profile


def test_the_default_convention_still_escapes_every_marker():
    # The reason `escaped` stays the default: under it a key is data, and this profile is what
    # every existing caller already gets. Adding a convention must not move anyone.
    assert filt.flatten({"@id": "1", "#text": "x", "Q{urn}y": "z"}, "cfg") == (
        "cfg.\\@id=1\n"
        "cfg.\\#text=x\n"
        "cfg.\\Q{urn\\}y=z\n")


def test_the_selector_is_a_name_rather_than_data_under_either_convention():
    # It names where the data hangs and has to match the scheme's own selector, so it is not
    # read for markers even when the keys beneath it are.
    assert filt.flatten({"k": "v"}, "@sel", "xmltodict") == "\\@sel.k=v\n"


def test_an_unknown_convention_is_refused_rather_than_defaulted():
    with pytest.raises(n2x.Namespace2XmlError, match="not a key convention"):
        filt.flatten({"k": "v"}, "cfg", "badgerfish")


def test_text_beside_a_child_element_is_refused_because_it_has_no_position():
    # Section 11.4 gives an element's own text the element's path only while it has no child
    # elements. Once it has one the element is mixed, every content node takes an ordered part,
    # and the mapping does not record which one the text was.
    with pytest.raises(n2x.Namespace2XmlError, match="mixed content"):
        filt.flatten({"a": {"#text": "hi", "b": 1}}, "cfg", "xmltodict")


def test_text_beside_a_content_token_is_refused_for_the_same_reason():
    with pytest.raises(n2x.Namespace2XmlError, match="mixed content"):
        filt.flatten({"a": {"#text": "hi", "#0": "x"}}, "cfg", "xmltodict")


def test_text_beside_an_escaped_literal_child_is_refused_too():
    # '\\@b' is an ordinary element named '@b', not an attribute, so it competes for position.
    with pytest.raises(n2x.Namespace2XmlError, match="mixed content"):
        filt.flatten({"a": {"#text": "hi", "\\@b": 1}}, "cfg", "xmltodict")


def test_text_holding_a_container_is_refused():
    with pytest.raises(n2x.Namespace2XmlError, match="own text"):
        filt.flatten({"a": {"#text": {"b": 1}}}, "cfg", "xmltodict")


def test_an_unclosed_namespace_marker_is_refused_here_rather_than_by_the_tool():
    # Section 8 makes marker recognition committing, so the tool would refuse this with
    # PARSE001 naming a synthesized profile in a temporary directory. Refusing here names the
    # key the author actually wrote.
    with pytest.raises(n2x.Namespace2XmlError, match="never closes it"):
        filt.flatten({"Q{urn:p": "v"}, "cfg", "xmltodict")


def test_an_undefined_escape_inside_a_uri_is_refused():
    with pytest.raises(n2x.Namespace2XmlError, match="no escape section 11.4 defines"):
        filt.flatten({"Q{urn:\\p}x": "v"}, "cfg", "xmltodict")


@pytest.mark.parametrize("key", ["#text ", "#nope", "#", "#01", "#1x"])
def test_a_hash_key_that_is_neither_text_nor_a_canonical_index_is_refused(key):
    # Escaping it would be the silent wrong answer this convention exists to remove: the author
    # wrote a marker. '#01' is refused because section 8.7 counts only the canonical spelling,
    # so accepting it would produce a part that orders nothing.
    with pytest.raises(n2x.Namespace2XmlError, match="reserves for content"):
        filt.flatten({key: "v"}, "cfg", "xmltodict")


def test_an_attribute_with_no_name_is_refused():
    with pytest.raises(n2x.Namespace2XmlError, match="attribute with no name"):
        filt.flatten({"@": "v"}, "cfg", "xmltodict")


class _Spy:
    """Stand in for the tool so the cache can be tested without one installed."""

    def __init__(self, text="RENDERED"):
        self.text = text
        self.calls = 0
        self.layered = None
        self.schemes = None
        self.profile = None

    def __call__(self, layered, schemes, executable, workdir, probe=None,
                 fmt=None):
        self.calls += 1
        self.layered = layered
        # The piped data is written last, because section 7.3 merges in command-line order and
        # section 17.1 lets the later source win. Keeping it under its own name lets a test that
        # only cares about the flattening say so.
        self.profile = layered[-1].text
        self.schemes = schemes
        return self.text


@pytest.fixture(name="spy")
def _spy(monkeypatch):
    spy = _Spy()
    monkeypatch.setattr(n2x, "tool_identity", lambda tool=None: "3.0.0|deadbeef")
    monkeypatch.setattr(n2x, "resolve", lambda tool: "/stand-in/namespace2xml")
    monkeypatch.setattr(filt, "_marshal_and_run", spy)
    monkeypatch.setattr(filt, "_RENDER_CACHE", {})

    return spy


def test_an_identical_render_is_served_from_the_memo(spy):
    assert filt.render({"k": "v"}, "json") == "RENDERED"
    assert filt.render({"k": "v"}, "json") == "RENDERED"
    assert spy.calls == 1


def test_memoize_false_spawns_the_tool_every_time(spy):
    filt.render({"k": "v"}, "json", memoize=False)
    filt.render({"k": "v"}, "json", memoize=False)
    assert spy.calls == 2


def test_the_memo_is_keyed_on_the_contract_identity_not_only_the_data(spy, monkeypatch):
    filt.render({"k": "v"}, "json")
    monkeypatch.setattr(n2x, "tool_identity", lambda tool=None: "3.0.1|cafebabe")
    filt.render({"k": "v"}, "json")
    assert spy.calls == 2


def test_the_memo_distinguishes_formats_and_schemes(spy):
    filt.render({"k": "v"}, "json")
    filt.render({"k": "v"}, "yaml")
    filt.render({"k": "v"}, "yaml", root="doc")
    assert spy.calls == 3


def test_the_convention_reaches_render_and_changes_what_the_tool_is_given(spy):
    filt.render({"bean": {"@id": "ds"}}, "xml", root="beans", convention="xmltodict")
    assert spy.profile == "cfg.bean.@id=ds\n"

    filt.render({"bean": {"@id": "ds"}}, "xml", root="beans")
    assert spy.profile == "cfg.bean.\\@id=ds\n"


def test_two_conventions_that_produce_the_same_profile_share_the_memo(spy):
    # The cache key is built from the profile, not from the arguments that produced it, so a
    # convention needs no key component of its own: identical profiles are identical renders.
    filt.render({"plain": "v"}, "json")
    filt.render({"plain": "v"}, "json", convention="xmltodict")
    assert spy.calls == 1
