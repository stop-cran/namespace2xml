"""Layering several inputs under the piped data, and refusing the entries that cannot mean one
thing.

The tool is file-in, directory-out and takes ``-i`` more than once. Section 7.3 requires the
merge, the wildcard evaluation and the precedence assignment to follow command-line source
order, and section 17.1 gives the later contribution precedence for a scalar. Together those
two make the order of this list load-bearing rather than cosmetic, so most of what is asserted
here is order: extras in the order written, the piped data last so it wins.

Section 7.1 is the other authority in this file. It picks the parser from the file extension --
``.json``, ``.yaml``, ``.yml`` and ``.xml``, with every other extension read as a namespace
profile -- which is why an entry's ``format`` has to become part of the temporary file's name,
and why a format outside that matrix has to be refused rather than passed through: an
unrecognised extension does not fail, it silently parses as the wrong language.

The expectations are authored from those sections and never captured from the tool; nothing
here runs it.
"""

from __future__ import annotations

import pathlib
import sys

import pytest

try:
    from ansible_collections.stop_cran.namespace2xml.plugins.filter import render as filt
    from ansible_collections.stop_cran.namespace2xml.plugins.module_utils import n2x
except ImportError:  # a checkout that is not inside an ansible_collections tree
    _ANSIBLE = pathlib.Path(__file__).resolve().parents[4]

    if str(_ANSIBLE) not in sys.path:
        sys.path.insert(0, str(_ANSIBLE))

    from plugins.filter import render as filt  # type: ignore[no-redef]
    from plugins.module_utils import n2x  # type: ignore[no-redef]


class _Spy:
    """Stand in for the tool, and keep the layered inputs it was handed."""

    def __init__(self, text="RENDERED"):
        self.text = text
        self.calls = 0
        self.layered = None

    def __call__(self, layered, schemes, executable, workdir, probe=None,
                 fmt=None):
        self.calls += 1
        self.layered = layered
        self.schemes = schemes
        return self.text

    @property
    def pairs(self):
        """The layered inputs as ``(content, file name)``, which is what these tests are about.

        ``_marshal_and_run`` takes entry records now, because an entry may name a file the
        filter never reads rather than content it has to write out. Every expectation below
        predates that and is about content and name, so the projection lives here rather than
        being spelled out at each assertion.
        """
        return [(entry.text, entry.name) for entry in self.layered]


@pytest.fixture(name="spy")
def _spy(monkeypatch):
    spy = _Spy()
    monkeypatch.setattr(n2x, "tool_identity", lambda tool=None: "3.0.0|deadbeef")
    monkeypatch.setattr(n2x, "resolve", lambda tool: "/stand-in/namespace2xml")
    monkeypatch.setattr(filt, "_marshal_and_run", spy)
    monkeypatch.setattr(filt, "_RENDER_CACHE", {})

    return spy


# --- what reaches the command line, and in what order ------------------------------------


def test_nothing_layered_is_the_single_input_it_has_always_been(spy):
    """The option is additive: not using it must leave the command line exactly as it was."""
    filt.render({"k": "v"}, "json")

    assert spy.pairs == [("cfg.k=v\n", "input.txt")]


def test_an_empty_list_layers_nothing(spy):
    """An empty list is a list of no inputs, not a malformed one -- a loop that produced none."""
    filt.render({"k": "v"}, "json", inputs=[])

    assert spy.pairs == [("cfg.k=v\n", "input.txt")]


def test_the_extras_come_first_in_the_order_written_and_the_piped_data_last(spy):
    """Section 7.3 merges in command-line order and section 17.1 lets the later source win.

    So the ordering is the whole feature: a template is layered first and the host's own data
    last, which is the direction that lets a host override a default rather than the reverse.
    Text reaches the tool verbatim -- only the piped data and `data` entries are flattened, and
    only those carry the selector.
    """
    filt.render({"port": "8443"}, "json", inputs=[
        {"text": "cfg.port=80\n"},
        {"text": "cfg.port=8080\n"},
    ])

    assert spy.pairs == [
        ("cfg.port=80\n", "input-1.txt"),
        ("cfg.port=8080\n", "input-2.txt"),
        ("cfg.port=8443\n", "input.txt"),
    ]


def test_the_piped_data_keeps_the_name_every_older_diagnostic_quotes(spy):
    """The tool quotes the resolved path it read from, so the name is user-visible output.

    Renaming the piped input would change the text of diagnostics that pre-date this option,
    for renders that do not use it.
    """
    filt.render({"k": "v"}, "json", inputs=[{"text": "a=1\n"}])

    assert spy.pairs[-1][1] == "input.txt"


@pytest.mark.parametrize("fmt, name", [
    ("namespace", "input-1.txt"),
    ("json", "input-1.json"),
    ("yaml", "input-1.yaml"),
    ("xml", "input-1.xml"),
])
def test_the_format_becomes_the_extension_because_section_7_1_reads_that(spy, fmt, name):
    """Section 7.1 selects the parser by extension, so this is how a format is requested."""
    filt.render({"k": "v"}, "json", inputs=[{"text": "<r/>", "format": fmt}])

    assert spy.pairs[0][1] == name


def test_the_default_format_is_the_namespace_profile(spy):
    """The tool's own language, and the one every other extension falls back to in 7.1."""
    filt.render({"k": "v"}, "json", inputs=[{"text": "a=1\n"}])

    assert spy.pairs[0][1] == "input-1.txt"


def test_positions_are_numbered_from_one_so_they_can_be_read_aloud(spy):
    """'input 1' has to mean the first entry written, not the zeroth."""
    filt.render({"k": "v"}, "json",
                inputs=[{"text": "a=1\n"}, {"text": "b=2\n"}, {"text": "c=3\n"}])

    assert [name for dummy, name in spy.pairs[:-1]] == [
        "input-1.txt", "input-2.txt", "input-3.txt"]


# --- data entries take the same road as the piped data ------------------------------------


def test_data_is_flattened_by_this_filter_not_serialised_as_json(spy):
    """A structure written as `data` must mean what the same structure piped in means.

    Handing it over as JSON instead would move it to section 9's key semantics, where a key is
    one name part and the delimiter stops separating -- a different reading of the same
    mapping, arrived at silently.
    """
    filt.render({"k": "v"}, "json", inputs=[{"data": {"a": {"b": "1"}}}])

    assert spy.pairs[0] == ("cfg.a.b=1\n", "input-1.txt")


def test_data_honours_the_selector_the_render_was_given(spy):
    """One render, one selector: an extra input landing under a different root would not merge."""
    filt.render({"k": "v"}, "json", selector="app", inputs=[{"data": {"a": "1"}}])

    assert spy.pairs[0][0] == "app.a=1\n"
    assert spy.pairs[-1][0] == "app.k=v\n"


def test_data_honours_the_convention(spy):
    """`xmltodict` changes what an @-prefixed key means, and it has to mean it in both places."""
    filt.render({"k": "v"}, "xml", convention="xmltodict",
                inputs=[{"data": {"bean": {"@id": "ds"}}}])

    assert spy.pairs[0][0] == "cfg.bean.@id=ds\n"


def test_data_and_text_may_be_mixed_across_entries(spy):
    """The two spellings are per entry, so a template and a structure can be layered together."""
    filt.render({"k": "v"}, "json",
                inputs=[{"text": "{\"a\": 1}", "format": "json"}, {"data": {"b": "2"}}])

    assert spy.pairs == [
        ("{\"a\": 1}", "input-1.json"),
        ("cfg.b=2\n", "input-2.txt"),
        ("cfg.k=v\n", "input.txt"),
    ]


# --- a file named rather than written out ------------------------------------------------


def test_a_bare_string_names_a_file_and_it_is_handed_over_where_it_lies(spy, tmp_path):
    """The controller already has the file, and the tool is about to open the same path.

    Copying it into the marshalling directory would double every large input and, worse, would
    make the path the tool quotes in a diagnostic a temporary one the author cannot go and look
    at. So a file entry contributes a path and no content.
    """
    existing = tmp_path / "base.txt"
    existing.write_text("cfg.port=80\n", encoding="utf-8")

    filt.render({"k": "v"}, "json", inputs=[str(existing)])

    assert [(entry.path, entry.text) for entry in spy.layered] == [
        (str(existing), None),
        (None, "cfg.k=v\n"),
    ]


def test_files_and_inline_content_interleave_in_the_order_written(spy, tmp_path):
    """Section 7.3 orders by the command line, not by how each source happened to be written.

    A shared file layered first and a host's own override written inline after it is the whole
    point of mixing them, so the two kinds cannot be grouped.
    """
    first = tmp_path / "base.txt"
    first.write_text("cfg.port=80\n", encoding="utf-8")
    third = tmp_path / "site.txt"
    third.write_text("cfg.port=443\n", encoding="utf-8")

    filt.render({"k": "v"}, "json", inputs=[
        str(first),
        {"text": "cfg.port=8080\n"},
        {"file": str(third)},
    ])

    assert [pathlib.Path(entry.name).name for entry in spy.layered] == [
        "base.txt", "input-2.txt", "site.txt", "input.txt",
    ], "the file entries keep the path the author wrote; only the order is under test here"


# --- the memo, once an input can be a file this filter never reads -------------------------
#
# The key used to be built from the marshalled text alone, which was sound while every input was
# text held in memory. A named file is content the filter does not hold and that anything else on
# the controller may rewrite between two renders in one process -- a `template` task ahead of a
# loop is the ordinary way that happens. So the file's identity has to enter the key, or the
# second render is served an answer to the first render's question.

def test_a_file_that_changed_between_two_renders_is_not_served_from_the_memo(spy, tmp_path):
    """The whole reason the stamp is in the key."""
    layered = tmp_path / "base.txt"
    layered.write_text("cfg.port=80\n", encoding="utf-8")
    filt.render({"k": "v"}, "json", inputs=[str(layered)])

    layered.write_text("cfg.port=443\n", encoding="utf-8")
    filt.render({"k": "v"}, "json", inputs=[str(layered)])

    assert spy.calls == 2


def test_an_unchanged_file_still_answers_from_the_memo(spy, tmp_path):
    """Keying on the file must not amount to switching the memo off for every render that
    names one: repeated identical renders across a loop are what it exists for."""
    layered = tmp_path / "base.txt"
    layered.write_text("cfg.port=80\n", encoding="utf-8")

    filt.render({"k": "v"}, "json", inputs=[str(layered)])
    filt.render({"k": "v"}, "json", inputs=[str(layered)])

    assert spy.calls == 1


def test_a_file_that_cannot_be_stamped_is_not_memoized_at_all(spy, tmp_path):
    """A key that silently omits an input it could not measure is a key that collides.

    Running twice costs a render; answering from a key that could not see the difference costs
    a wrong one, so the unsound key is not built rather than being built incompletely.
    """
    missing = tmp_path / "absent.txt"

    filt.render({"k": "v"}, "json", inputs=[str(missing)])
    filt.render({"k": "v"}, "json", inputs=[str(missing)])

    assert spy.calls == 2


# --- refusals: every case where the alternative is a wrong render at exit 0 ----------------


def test_one_input_written_without_the_list_is_refused(spy):
    """The plausible near-miss. A mapping would otherwise iterate as its keys."""
    with pytest.raises(filt.Namespace2XmlError, match="a mapping"):
        filt.render({"k": "v"}, "json", inputs={"text": "a=1\n"})


def test_a_string_where_the_list_belongs_is_refused(spy):
    """A string is iterable, so it would layer one input per character."""
    with pytest.raises(filt.Namespace2XmlError, match="a string"):
        filt.render({"k": "v"}, "json", inputs="a=1\n")


def test_an_entry_that_is_neither_a_string_nor_a_mapping_is_refused(spy):
    """A bare string names a file now, so the near-miss left is a value that names nothing.

    Naming the position locates it, which a type name on its own would not.
    """
    with pytest.raises(filt.Namespace2XmlError, match="Input 2 is a number"):
        filt.render({"k": "v"}, "json", inputs=[{"text": "a=1\n"}, 8080])


def test_an_unrecognised_key_is_refused_rather_than_ignored(spy):
    """A typo in a key name is an input silently not layered, which is a wrong render."""
    with pytest.raises(filt.Namespace2XmlError, match="unrecognised key 'txt'"):
        filt.render({"k": "v"}, "json", inputs=[{"txt": "a=1\n"}])


def test_both_text_and_data_in_one_entry_are_refused(spy):
    """Two ways to write one input, so an entry with both does not say which to layer."""
    with pytest.raises(filt.Namespace2XmlError, match="supplies 'text' and 'data'"):
        filt.render({"k": "v"}, "json", inputs=[{"text": "a=1\n", "data": {"b": "2"}}])


def test_an_entry_that_names_no_source_is_refused(spy):
    """It contributes nothing but is still counted, so it cannot be read as a placeholder."""
    with pytest.raises(filt.Namespace2XmlError, match="supplies none of 'file', 'text' or 'data'"):
        filt.render({"k": "v"}, "json", inputs=[{"format": "json"}])


@pytest.mark.parametrize("fmt", ["ini", "shell", "quoted-namespace"])
def test_an_output_only_format_is_refused(spy, fmt):
    """Section 7.1's matrix lists these as output only.

    Accepting one would write `input-1.ini`, which 7.1 does not recognise and therefore reads
    as a namespace profile -- the wrong language, arrived at without a diagnostic.
    """
    with pytest.raises(filt.Namespace2XmlError, match="not one the tool reads"):
        filt.render({"k": "v"}, "json", inputs=[{"text": "a=1\n", "format": fmt}])


def test_a_format_alongside_data_is_refused(spy):
    """`data` is flattened here into profile text, so no other parser is left for it to reach."""
    with pytest.raises(filt.Namespace2XmlError, match="sets 'format' alongside 'data'"):
        filt.render({"k": "v"}, "json", inputs=[{"data": {"a": "1"}, "format": "json"}])


def test_naming_the_encoding_data_already_uses_is_refused_too(spy):
    """`format` never travels with `data`, not even when it names what data already produces.

    An input's `data` is encoded to the tool's own syntax and a scheme's `data` to JSON, so no
    one spelling is true on both sides of the entry shape the module and this filter share.
    Accepting the one that happens to be true here would make `format` mean "the encoding" in
    one place and "the parser to reach" everywhere else.
    """
    with pytest.raises(filt.Namespace2XmlError, match="sets 'format' alongside 'data'"):
        filt.render({"k": "v"}, "json", inputs=[{"data": {"a": "1"}, "format": "namespace"}])


def test_text_that_is_not_a_string_is_refused(spy):
    """Text reaches the tool unparsed. A mapping here is a `data` entry written under the
    wrong key, and `str()` of it would be neither JSON nor a profile."""
    with pytest.raises(filt.Namespace2XmlError, match="'text' as a mapping"):
        filt.render({"k": "v"}, "json", inputs=[{"text": {"a": "1"}}])


# --- the memo has to tell these renders apart ----------------------------------------------


def test_the_same_render_twice_is_still_served_from_the_memo(spy):
    """Layering must not defeat the cache for a repeated, identical render."""
    arguments = dict(inputs=[{"text": "a=1\n"}])

    filt.render({"k": "v"}, "json", **arguments)
    filt.render({"k": "v"}, "json", **arguments)

    assert spy.calls == 1


def test_different_extras_under_the_same_data_are_different_renders(spy):
    """The piped data is equal, so only the layered inputs can separate the two keys."""
    filt.render({"k": "v"}, "json", inputs=[{"text": "a=1\n"}])
    filt.render({"k": "v"}, "json", inputs=[{"text": "a=2\n"}])

    assert spy.calls == 2


def test_reordering_the_extras_is_a_different_render(spy):
    """Section 17.1 decides by order, so the same two inputs swapped are not interchangeable."""
    filt.render({"k": "v"}, "json", inputs=[{"text": "a=1\n"}, {"text": "b=2\n"}])
    filt.render({"k": "v"}, "json", inputs=[{"text": "b=2\n"}, {"text": "a=1\n"}])

    assert spy.calls == 2


def test_the_same_bytes_split_across_a_different_number_of_inputs_are_different_renders(spy):
    """Two sources concatenate a sequence under section 7.3 where one does not.

    Without the count in the key, `a=1\\n` + `b=2\\n` as two inputs and as one would hash the
    same run of bytes and share an entry.
    """
    filt.render({"k": "v"}, "json", inputs=[{"text": "a=1\nb=2\n"}])
    filt.render({"k": "v"}, "json", inputs=[{"text": "a=1\n"}, {"text": "b=2\n"}])

    assert spy.calls == 2


def test_the_same_text_read_as_two_formats_is_a_different_render(spy):
    """The name selects the parser, so identical bytes are two sources, not one."""
    filt.render({"k": "v"}, "json", inputs=[{"text": "{}", "format": "json"}])
    filt.render({"k": "v"}, "json", inputs=[{"text": "{}", "format": "yaml"}])

    assert spy.calls == 2
