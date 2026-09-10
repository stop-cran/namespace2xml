from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).resolve().parents[1] / "sync-ansible-doc-links.py"
SPEC = importlib.util.spec_from_file_location("sync_ansible_doc_links", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"could not load {MODULE_PATH}")
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class NormalizeLinksTests(unittest.TestCase):
    def test_only_same_repository_blob_and_tree_refs_are_rewritten(self) -> None:
        source = (
            b"spec https://github.com/stop-cran/namespace2xml/blob/master/"
            b"docs/specification.md#18-scalar-inference\n"
            b"tree https://github.com/stop-cran/namespace2xml/tree/old-ref/"
            b"ansible/plugins/module_utils\n"
            b"current https://github.com/stop-cran/namespace2xml/blob/"
            b"ansible-v3.0.2/AGENTS.md\n"
            b"issue https://github.com/stop-cran/namespace2xml/issues/127\n"
            b"external https://github.com/example/namespace2xml/blob/master/README.md\n"
            b"utf8 caf\xc3\xa9\n"
        )

        normalized, stale, count = MODULE.normalize_links(
            "ansible/example.md",
            source,
            "ansible-v3.0.2",
        )

        self.assertEqual(count, 3)
        self.assertEqual(len(stale), 2)
        self.assertEqual(
            stale[0].found,
            "https://github.com/stop-cran/namespace2xml/blob/master/"
            "docs/specification.md#18-scalar-inference",
        )
        self.assertEqual(
            stale[0].expected,
            "https://github.com/stop-cran/namespace2xml/blob/ansible-v3.0.2/"
            "docs/specification.md#18-scalar-inference",
        )
        expected = (
            b"spec https://github.com/stop-cran/namespace2xml/blob/ansible-v3.0.2/"
            b"docs/specification.md#18-scalar-inference\n"
            b"tree https://github.com/stop-cran/namespace2xml/tree/ansible-v3.0.2/"
            b"ansible/plugins/module_utils\n"
            b"current https://github.com/stop-cran/namespace2xml/blob/"
            b"ansible-v3.0.2/AGENTS.md\n"
            b"issue https://github.com/stop-cran/namespace2xml/issues/127\n"
            b"external https://github.com/example/namespace2xml/blob/master/README.md\n"
            b"utf8 caf\xc3\xa9\n"
        )
        self.assertEqual(normalized, expected)

        second_pass, second_stale, second_count = MODULE.normalize_links(
            "ansible/example.md",
            normalized,
            "ansible-v3.0.2",
        )
        self.assertEqual(second_pass, normalized)
        self.assertEqual(second_stale, [])
        self.assertEqual(second_count, 3)


class CollectionVersionTests(unittest.TestCase):
    def write_manifest(self, text: str) -> Path:
        temporary = tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            newline="\n",
            suffix=".yml",
            delete=False,
        )
        self.addCleanup(Path(temporary.name).unlink)
        with temporary:
            temporary.write(text)
        return Path(temporary.name)

    def test_reads_one_top_level_scalar_version(self) -> None:
        path = self.write_manifest(
            "namespace: stop_cran\n"
            "metadata:\n"
            "  version: ignored\n"
            "version: 3.0.2\n"
        )

        self.assertEqual(MODULE.collection_version(path), "3.0.2")
        self.assertEqual(MODULE.collection_ref(path), "ansible-v3.0.2")

    def test_rejects_duplicate_top_level_versions(self) -> None:
        path = self.write_manifest("version: 3.0.2\nversion: 3.0.3\n")

        with self.assertRaisesRegex(ValueError, "exactly one top-level version; found 2"):
            MODULE.collection_version(path)

    def test_rejects_missing_top_level_version(self) -> None:
        path = self.write_manifest("metadata:\n  version: 3.0.2\n")

        with self.assertRaisesRegex(ValueError, "exactly one top-level version; found 0"):
            MODULE.collection_version(path)

    def test_rejects_non_scalar_or_unsafe_versions(self) -> None:
        non_scalar = self.write_manifest("version: [3, 0, 2]\n")
        unsafe = self.write_manifest("version: 3/0/2\n")

        with self.assertRaisesRegex(ValueError, "version must be a scalar"):
            MODULE.collection_version(non_scalar)
        with self.assertRaisesRegex(ValueError, "is not safe"):
            MODULE.collection_version(unsafe)


class AnsibleDocJsonTests(unittest.TestCase):
    def write_document(self, document: object) -> Path:
        temporary = tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            newline="\n",
            suffix=".json",
            delete=False,
        )
        self.addCleanup(Path(temporary.name).unlink)
        with temporary:
            json.dump(document, temporary)
        return Path(temporary.name)

    def test_accepts_nested_release_refs_and_ignores_external_urls(self) -> None:
        path = self.write_document(
            {
                "plugin": {
                    "links": [
                        "https://github.com/stop-cran/namespace2xml/blob/"
                        "ansible-v3.0.2/docs/specification.md#18-scalar-inference",
                        {
                            "url": "https://github.com/stop-cran/namespace2xml/tree/"
                            "ansible-v3.0.2/ansible/plugins"
                        },
                        "https://github.com/example/namespace2xml/blob/master/README.md",
                    ]
                }
            }
        )

        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = MODULE.check_ansible_doc_json(
                path,
                "ansible-v3.0.2",
                "module ansible-doc",
            )

        self.assertEqual(result, 0)
        self.assertIn("all 2 same-repository link(s)", output.getvalue())

    def test_rejects_stale_and_missing_release_refs(self) -> None:
        stale = self.write_document(
            {
                "url": "https://github.com/stop-cran/namespace2xml/blob/"
                "master/docs/specification.md"
            }
        )
        missing = self.write_document(
            {"url": "https://github.com/stop-cran/namespace2xml/issues/127"}
        )

        stale_output = io.StringIO()
        with contextlib.redirect_stdout(stale_output):
            stale_result = MODULE.check_ansible_doc_json(
                stale,
                "ansible-v3.0.2",
                "filter ansible-doc",
            )
        missing_output = io.StringIO()
        with contextlib.redirect_stdout(missing_output):
            missing_result = MODULE.check_ansible_doc_json(
                missing,
                "ansible-v3.0.2",
                "filter ansible-doc",
            )

        self.assertEqual(stale_result, 1)
        self.assertIn("/blob/master/", stale_output.getvalue())
        self.assertEqual(missing_result, 1)
        self.assertIn("contains no same-repository", missing_output.getvalue())


class DiagnosticDocsTests(unittest.TestCase):
    RELEASE_REF = "ansible-v3.0.2"
    ABSOLUTE_BASE = (
        "https://github.com/stop-cran/namespace2xml/blob/ansible-v3.0.2/"
    )

    def write_bytes(self, data: bytes) -> Path:
        temporary = tempfile.NamedTemporaryFile(delete=False)
        self.addCleanup(Path(temporary.name).unlink)
        with temporary:
            temporary.write(data)
        return Path(temporary.name)

    def test_approved_link_bases_are_the_only_normalized_difference(self) -> None:
        root = self.write_bytes(
            b"[spec](specification.md#spec-6-4-3)\n"
            b"[schema](../spec/diagnostic-stream.schema.json)\n"
            b"[contributing](../CONTRIBUTING.md#feedback)\n"
        )
        ansible = self.write_bytes(
            (
                f"[spec]({self.ABSOLUTE_BASE}docs/specification.md#spec-6-4-3)\n"
                f"[schema]({self.ABSOLUTE_BASE}spec/diagnostic-stream.schema.json)\n"
                f"[contributing]({self.ABSOLUTE_BASE}CONTRIBUTING.md#feedback)\n"
            ).encode("ascii")
        )

        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = MODULE.check_diagnostic_docs(
                root,
                ansible,
                self.RELEASE_REF,
            )

        self.assertEqual(result, 0)
        self.assertIn("are equivalent", output.getvalue())

    def test_content_drift_is_not_normalized(self) -> None:
        root = self.write_bytes(
            b"[spec](specification.md)\n"
            b"[schema](../spec/diagnostic-stream.schema.json)\n"
            b"[contributing](../CONTRIBUTING.md)\n"
        )
        ansible = self.write_bytes(
            (
                f"[spec]({self.ABSOLUTE_BASE}docs/specification.md)\n"
                f"[schema]({self.ABSOLUTE_BASE}spec/diagnostic-stream.schema.json)\n"
                f"[contributing]({self.ABSOLUTE_BASE}CONTRIBUTING.md)\n"
                "changed prose\n"
            ).encode("ascii")
        )

        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = MODULE.check_diagnostic_docs(
                root,
                ansible,
                self.RELEASE_REF,
            )

        self.assertEqual(result, 1)
        self.assertIn("differ after approved", output.getvalue())

    def test_missing_approved_link_class_fails_closed(self) -> None:
        root = self.write_bytes(b"[spec](specification.md)\n")
        ansible = self.write_bytes(
            f"[spec]({self.ABSOLUTE_BASE}docs/specification.md)\n".encode("ascii")
        )

        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = MODULE.check_diagnostic_docs(
                root,
                ansible,
                self.RELEASE_REF,
            )

        self.assertEqual(result, 1)
        self.assertIn("missing approved link base", output.getvalue())


if __name__ == "__main__":
    unittest.main()
