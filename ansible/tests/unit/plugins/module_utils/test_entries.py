"""The uniform entry shape, and the refusals that keep it unambiguous.

Every source this collection takes -- inputs and schemes, on the filter and on the module -- is
a list of entries in the shape defined by ``module_utils.entries``. These expectations are
authored from that shape and from the specification sections it answers to: Section 7.1 and
Section 15 for the parser-by-extension rule that makes a file name load-bearing, Section 7.3
and Section 17.1 for the ordering rule that makes the list ordered, Section 8.3 for the input
encoder and Section 15.2 for the scheme encoder.

They are never captured from the implementation. Several of the tests below assert a refusal
rather than a result, because the value of this shape is mostly in what it declines to guess:
an entry naming two sources, or a ``format`` that could not be honoured, is a playbook whose
author believed something the tool will not do.
"""

from __future__ import annotations

import pathlib
import sys

import pytest

_ANSIBLE = pathlib.Path(__file__).resolve().parents[4]

try:
    from ansible_collections.stop_cran.namespace2xml.plugins.module_utils import (
        entries, n2x)
except ImportError:  # a checkout that is not inside an ansible_collections tree
    if str(_ANSIBLE) not in sys.path:
        sys.path.insert(0, str(_ANSIBLE))

    from plugins.module_utils import entries, n2x  # type: ignore[no-redef]


def inputs(value, selector="cfg", convention="escaped", argument="inputs"):
    return entries.marshal_inputs(value, selector, convention, argument)


def schemes(value, argument="scheme"):
    return entries.marshal_schemes(value, argument)


# --- the shape ---------------------------------------------------------------------------

def test_nothing_supplied_is_no_entries():
    """Absence is not an error. Every caller has an argument that may simply be omitted."""
    assert inputs(None) == []
    assert schemes(None) == []


def test_a_bare_string_names_a_file():
    """The common case stays short.

    Most entries name a file that already exists, and requiring ``{file: ...}`` for those would
    make the ordinary spelling the noisy one. A string is therefore sugar for a file entry --
    which is also what keeps ``src: [/path]`` and the module's ``scheme: [/path]`` working
    unchanged across the reshape.
    """
    got, = inputs(["/etc/app/base.properties"])

    assert got.path == "/etc/app/base.properties"
    assert got.text is None


def test_a_file_is_handed_over_where_it_lies():
    """A file entry is not read, not copied and not rewritten.

    Reading it here would put this collection between the operator and the tool's own parser,
    and would change the path the tool quotes in its diagnostics -- the operator would be told
    to go and fix a scratch file that will not exist by the time they look.
    """
    got, = inputs([{"file": "/etc/app/base.properties"}])

    assert got.path == "/etc/app/base.properties"
    assert got.name == "/etc/app/base.properties"
    assert got.text is None


def test_a_leading_tilde_is_expanded():
    """The coercion ``elements: raw`` costs us, done by hand so both plugins keep it.

    An entry may be a string or a mapping, which rules out ``type: path`` on the module's
    argument spec, and with it the expansion Ansible would otherwise have applied. Without this
    the module would hand the tool a directory literally named ``~``.
    """
    got, = inputs([{"file": "~/base.properties"}])

    assert not got.path.startswith("~")
    assert got.path.endswith("base.properties")


def test_the_order_given_is_the_order_kept():
    """Section 7.3 merges in command-line order and Section 17.1 gives the later one precedence.

    The list is therefore an override chain, and any reordering here -- sorting, grouping by
    kind, hoisting files above inline text -- would silently change which value wins.
    """
    got = inputs([{"text": "a=1"}, "/etc/second.properties", {"data": {"a": 2}}])

    assert [entry.text is None for entry in got] == [False, True, False]
    assert got[1].path == "/etc/second.properties"


# --- inline content ----------------------------------------------------------------------

def test_text_is_written_under_a_name_whose_extension_selects_the_parser():
    """Section 7.1 picks the parser from the extension, so the name carries the format."""
    got, = inputs([{"text": "<r/>", "format": "xml"}])

    assert got.text == "<r/>"
    assert got.name.endswith(".xml")


def test_text_defaults_to_the_namespace_profile():
    """The profile is the tool's own syntax and the one an entry means when it says nothing."""
    got, = inputs([{"text": "a.b=1"}])

    assert got.name.endswith(".txt")


def test_data_is_flattened_by_the_section_8_3_encoder():
    """``data`` is the whole point of the shape: a variable the playbook already has.

    It is encoded here rather than templated by the author, which is what makes a structure
    with a dot or a marker in a key safe to pass.
    """
    got, = inputs([{"data": {"a": {"b": 1}}}])

    assert got.text == "cfg.a.b=1\n"
    assert got.name.endswith(".txt")


def test_scheme_data_is_encoded_as_a_section_15_json_document():
    """Section 15 picks the scheme parser from the extension too, so this must arrive as .json.

    Emitting it under a ``.txt`` name would have the tool read a JSON document as line-oriented
    scheme text, which fails as a parse error about the wrong language.
    """
    got, = schemes([{"data": {"cfg": {"output": "xml"}}}])

    assert got.name.endswith(".json")
    assert '"output": "xml"' in got.text


def test_scheme_text_is_written_under_the_extension_its_format_selects():
    """Section 15 reads a scheme as its own text, as JSON or as YAML, chosen by extension.

    A scheme already held as YAML *text* -- read by ``lookup``, or assembled elsewhere -- has
    nowhere else to declare what it is. Without this it would be written under ``.txt`` and the
    tool would read a YAML document as a line-oriented scheme, failing as a parse error about
    the wrong language rather than as a statement about the format.
    """
    got, = schemes([{"text": "cfg:\n  output: json\n", "format": "yaml"}])

    assert got.text == "cfg:\n  output: json\n"
    assert got.name.endswith(".yaml")


def test_scheme_text_defaults_to_the_tools_own_syntax():
    """Saying nothing means the spelling a scheme has on the command line and in a file."""
    got, = schemes([{"text": "cfg.output=json"}])

    assert got.name.endswith(".txt")


def test_xml_is_refused_for_a_scheme_because_section_15_does_not_offer_it():
    """The input and scheme format sets differ, and the message has to name the right one.

    XML is a perfectly good *input* format, so this refusal is the only thing distinguishing
    the two sets to a reader. It must list what a scheme takes rather than what an input does.
    """
    with pytest.raises(n2x.Namespace2XmlError) as caught:
        schemes([{"text": "<r/>", "format": "xml"}])

    assert "namespace, json, yaml" in str(caught.value)
    assert "xml," not in str(caught.value)


def test_entries_are_named_in_position_order():
    """The names are read by whoever has to fix the source they name, so they identify it."""
    got = inputs([{"text": "a=1"}, {"text": "b=2"}])

    assert [entry.name for entry in got] == ["input-1.txt", "input-2.txt"]


# --- refusals ----------------------------------------------------------------------------

def test_a_single_entry_written_without_the_list_is_refused():
    """The plausible near-miss: one entry supplied directly instead of wrapped in a list."""
    with pytest.raises(n2x.Namespace2XmlError, match="is a list"):
        inputs({"text": "a=1"})


def test_a_bare_string_for_the_whole_argument_is_refused():
    """A string is iterable, so this is the near-miss that would otherwise be walked by letter.

    Left alone it would produce one entry per character, each a file named ``/``, ``e``, ``t``
    and so on -- a diagnostic about a missing file rather than about the argument's shape.
    """
    with pytest.raises(n2x.Namespace2XmlError, match="is a list"):
        inputs("/etc/app/base.properties")


def test_an_entry_naming_two_sources_is_refused():
    """They are alternative spellings of one source, so both together says nothing definite."""
    with pytest.raises(n2x.Namespace2XmlError, match="'file' and 'text'"):
        inputs([{"file": "/etc/a", "text": "a=1"}])


def test_an_entry_naming_no_source_is_refused():
    """An entry that contributes nothing still occupies a position, so it is not a placeholder."""
    with pytest.raises(n2x.Namespace2XmlError, match="none of"):
        inputs([{"format": "xml"}])


def test_an_unrecognised_key_is_refused_rather_than_ignored():
    """An ignored key here would be a source silently not applied, which is the silent class."""
    with pytest.raises(n2x.Namespace2XmlError, match="unrecognised key 'files'"):
        inputs([{"files": "/etc/a"}])


def test_a_format_beside_a_file_is_refused_because_it_could_not_be_honoured():
    """The tool reads the extension of the file it is given, and that file is not copied.

    Accepting ``format`` here would let it disagree with the extension while appearing to have
    been applied -- the render would succeed, reading the file as the wrong language.
    """
    with pytest.raises(n2x.Namespace2XmlError, match="selects the parser from the"):
        inputs([{"file": "/etc/a.conf", "format": "json"}])


def test_a_format_beside_data_is_refused_for_the_same_reason():
    """A structure is encoded here into the tool's syntax, so no other parser is left to reach."""
    with pytest.raises(n2x.Namespace2XmlError, match="no other parser"):
        inputs([{"data": {"a": 1}, "format": "json"}])


@pytest.mark.parametrize("fmt", ["ini", "shell"])
def test_an_output_only_format_is_refused_for_an_input(fmt):
    """Section 7.1 has INI and shell as output only.

    Accepting either would write a file the tool then reads as a namespace profile, and the
    mistake would surface as a parse error about a language the author never mentioned.
    """
    with pytest.raises(n2x.Namespace2XmlError, match="not one the tool reads"):
        inputs([{"text": "a=1", "format": fmt}])


def test_xml_is_refused_for_a_scheme():
    """Section 15 offers a scheme as its own text, JSON or YAML, and does not offer XML."""
    with pytest.raises(n2x.Namespace2XmlError, match="not one the tool reads"):
        schemes([{"text": "<r/>", "format": "xml"}])


def test_a_file_that_is_not_a_string_is_refused():
    with pytest.raises(n2x.Namespace2XmlError, match="'file' as a mapping"):
        inputs([{"file": {"path": "/etc/a"}}])


def test_an_empty_file_is_refused():
    """An empty path would reach the tool as an argument naming nothing."""
    with pytest.raises(n2x.Namespace2XmlError, match="empty 'file'"):
        inputs([{"file": "   "}])


def test_text_that_is_not_a_string_is_refused():
    """Text reaches the tool unparsed; a structure has its own key and its own encoder."""
    with pytest.raises(n2x.Namespace2XmlError, match="'text' as a mapping"):
        inputs([{"text": {"a": 1}}])


def test_an_entry_that_is_neither_a_string_nor_a_mapping_is_refused():
    with pytest.raises(n2x.Namespace2XmlError, match="Input 1 is a number"):
        inputs([7])


# --- the message quotes the argument the author actually wrote ---------------------------

def test_the_refusal_names_the_argument_the_caller_used():
    """The module keeps ``src`` as a deprecated alias, and a message has to quote what was written.

    Being told to fix ``'inputs'`` when the playbook says ``src`` sends the author looking for
    an argument that is not there -- the same defect ``scheme_text`` was introduced to avoid.
    """
    with pytest.raises(n2x.Namespace2XmlError, match="'src' is a mapping"):
        inputs({"text": "a=1"}, argument="src")
