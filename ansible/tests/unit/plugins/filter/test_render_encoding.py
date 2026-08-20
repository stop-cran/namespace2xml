"""Side A of the oracle: the encoder against profile text authored from the specification.

The expectations here are written by hand from sections 8.2, 8.3, 8.7 and 18. They are never
regenerated from ``flatten``, and never captured from the tool. A captured expectation asserts
only that the code still does what it did, which is the one thing that needs no test.

Side B -- the tool run by hand over the same profile text, compared against what ``render``
produced from the data -- needs the binary and lives in
``tests/integration/targets/render``.
"""

from __future__ import annotations

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
    assert n2x.flatten(data, "cfg") == profile


def test_a_tuple_is_a_sequence_like_a_list():
    assert n2x.flatten({"a": ("x", "y")}, "cfg") == "cfg.a.0=x\ncfg.a.1=y\n"


def test_an_empty_name_part_is_a_parse_error():
    with pytest.raises(n2x.Namespace2XmlError, match="empty name part"):
        n2x.encode_name_part("")


def test_binary_data_is_refused_rather_than_guessed_at():
    with pytest.raises(n2x.Namespace2XmlError, match="binary data"):
        n2x.flatten({"blob": b"\x00\x01"}, "cfg")


def test_q_is_escaped_only_where_it_could_open_a_canonical_component():
    # 'Q{' can introduce a section 11.4 component at the start of a part and nowhere else, so
    # escaping it elsewhere would spend an escape to prevent nothing.
    assert n2x.encode_name_part("Qx") == "\\Qx"
    assert n2x.encode_name_part("xQ") == "xQ"


def test_a_lone_dollar_is_not_a_reference_start():
    # Section 8.3 escapes '${', not '$'. Escaping the bare character would change text that
    # round-trips perfectly well as itself.
    assert n2x.encode_value("costs $5") == "costs $5"
    assert n2x.encode_value("$ {x}") == "$ {x}"


def test_the_container_sentinels_are_escaped_only_when_they_are_the_whole_value():
    assert n2x.encode_value("{}") == "\\{}"
    assert n2x.encode_value("a{}b") == "a{}b"


def test_the_synthesized_scheme_declares_only_what_was_asked_for():
    assert n2x.synthesize_scheme("json", "cfg") == "cfg.output=json\n"
    assert n2x.synthesize_scheme("xml", "cfg", "configuration") == (
        "cfg.output=xml\ncfg.root=configuration\n")
    assert n2x.synthesize_scheme("ini", "cfg", None, ":") == "cfg.output=ini\ncfg.delimiter=:\n"


def test_a_selector_needing_escapes_is_escaped_in_both_the_profile_and_the_scheme():
    assert n2x.flatten({"k": "v"}, "my.app") == "my\\.app.k=v\n"
    assert n2x.synthesize_scheme("json", "my.app") == "my\\.app.output=json\n"


class _Spy:
    """Stand in for the tool so the cache can be tested without one installed."""

    def __init__(self, text="RENDERED"):
        self.text = text
        self.calls = 0
        self.scheme_text = None
        self.scheme_name = None

    def __call__(self, profile, scheme_text, scheme_name, executable, workdir):
        self.calls += 1
        self.scheme_text = scheme_text
        self.scheme_name = scheme_name
        return self.text


@pytest.fixture(name="spy")
def _spy(monkeypatch):
    spy = _Spy()
    monkeypatch.setattr(n2x, "tool_identity", lambda tool=None: "3.0.0|deadbeef")
    monkeypatch.setattr(n2x, "_resolve", lambda tool: "/stand-in/namespace2xml")
    monkeypatch.setattr(n2x, "_marshal_and_run", spy)
    monkeypatch.setattr(n2x, "_RENDER_CACHE", {})

    return spy


def test_an_identical_render_is_served_from_the_memo(spy):
    assert n2x.render({"k": "v"}, "json") == "RENDERED"
    assert n2x.render({"k": "v"}, "json") == "RENDERED"
    assert spy.calls == 1


def test_memoize_false_spawns_the_tool_every_time(spy):
    n2x.render({"k": "v"}, "json", memoize=False)
    n2x.render({"k": "v"}, "json", memoize=False)
    assert spy.calls == 2


def test_the_memo_is_keyed_on_the_contract_identity_not_only_the_data(spy, monkeypatch):
    n2x.render({"k": "v"}, "json")
    monkeypatch.setattr(n2x, "tool_identity", lambda tool=None: "3.0.1|cafebabe")
    n2x.render({"k": "v"}, "json")
    assert spy.calls == 2


def test_the_memo_distinguishes_formats_and_schemes(spy):
    n2x.render({"k": "v"}, "json")
    n2x.render({"k": "v"}, "yaml")
    n2x.render({"k": "v"}, "yaml", root="doc")
    assert spy.calls == 3
