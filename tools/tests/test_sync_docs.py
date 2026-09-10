from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "sync-docs.ps1"
ANSI_ESCAPE = re.compile(r"\x1b\[[0-9;]*m")


def error_text(result: subprocess.CompletedProcess[str]) -> str:
    return " ".join(ANSI_ESCAPE.sub("", result.stderr).split())


class DiagnosticDocumentationGeneratorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        for relative in ("spec", "docs", "ansible/docs"):
            (self.root / relative).mkdir(parents=True)

        tools = self.root / "tools"
        tools.mkdir()
        self.script = tools / SCRIPT.name
        shutil.copy2(SCRIPT, self.script)
        shutil.copy2(
            SCRIPT.parent / "sync-ansible-doc-links.py",
            tools / "sync-ansible-doc-links.py",
        )
        (self.root / "ansible" / "galaxy.yml").write_text(
            "namespace: stop_cran\n"
            "name: namespace2xml\n"
            "version: 3.0.2\n",
            encoding="utf-8",
            newline="\n",
        )
        (self.root / "spec" / "contract-bundle.json").write_text(
            json.dumps({"revision": "r1+000000000000"}) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        self.write_navigation(["22", "6.4.3", "B"])
        self.write_registry(["TEST001"])

    def write_navigation(self, sections: list[str]) -> None:
        navigation = {
            "generatedBy": "tools/sync-specification-navigation.ps1",
            "clauses": [
                {
                    "section": section,
                    "anchor": f"spec-{section.lower().replace('.', '-')}",
                    "level": 2,
                    "heading": section,
                }
                for section in sections
            ],
        }
        (self.root / "spec" / "specification-navigation.json").write_text(
            json.dumps(navigation) + "\n",
            encoding="utf-8",
            newline="\n",
        )

    def write_registry(self, codes: list[str]) -> None:
        registry = {
            "authoritativeFor": ["code", "severity", "cardinality", "fields"],
            "notAuthoritativeFor": ["phase", "spec", "message"],
            "codes": [
                {
                    "code": code,
                    "severity": "error",
                    "cardinality": "once per test",
                    "condition": f"Condition for {code}",
                    "fields": ["path"],
                    "mappings": [f"Mapping for {code}"],
                }
                for code in codes
            ],
        }
        (self.root / "spec" / "diagnostics.registry.json").write_text(
            json.dumps(registry) + "\n",
            encoding="utf-8",
            newline="\n",
        )

    def run_generator(
        self, expect_success: bool = True
    ) -> subprocess.CompletedProcess[str]:
        result = subprocess.run(
            [
                "pwsh",
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                (
                    "$ErrorActionPreference = 'Stop'; "
                    "try { & $env:NAMESPACE2XML_TEST_SCRIPT "
                    "-RepositoryRoot $env:NAMESPACE2XML_TEST_ROOT -DiagnosticsOnly } "
                    "catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }"
                ),
            ],
            capture_output=True,
            text=True,
            check=False,
            env={
                **os.environ,
                "NAMESPACE2XML_TEST_SCRIPT": str(self.script),
                "NAMESPACE2XML_TEST_ROOT": str(self.root),
            },
        )
        if expect_success:
            self.assertEqual(result.returncode, 0, result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0, result.stdout)
        return result

    def read_docs(self) -> tuple[str, str]:
        return (
            (self.root / "docs" / "diagnostics.md").read_text(encoding="utf-8"),
            (self.root / "ansible" / "docs" / "diagnostics.md").read_text(
                encoding="utf-8"
            ),
        )

    def test_generates_stable_code_and_specification_links(self) -> None:
        self.run_generator()
        root, ansible = self.read_docs()

        self.assertIn("[`TEST001`](#diagnostic-test001)", root)
        self.assertIn('<a id="diagnostic-test001"></a>', root)
        self.assertEqual(root.count("](#diagnostic-test001)"), 1)
        self.assertEqual(root.count('<a id="diagnostic-test001"></a>'), 1)
        self.assertIn("[Section 22](specification.md#spec-22)", root)
        self.assertIn("[Appendix B](specification.md#spec-b)", root)
        self.assertIn("[`§6.4.3`](specification.md#spec-6-4-3)", root)
        self.assertIn("selected for each diagnostic occurrence", root)
        self.assertIn(
            "https://github.com/stop-cran/namespace2xml/blob/ansible-v",
            ansible,
        )
        self.assertNotIn("](specification.md", ansible)

    def test_duplicate_code_derived_detail_anchor_fails_closed(self) -> None:
        self.write_registry(["TEST001", "test001"])

        result = self.run_generator(expect_success=False)

        self.assertIn(
            "duplicate detail anchor 'diagnostic-test001'",
            error_text(result),
        )

    def test_missing_linked_specification_clause_fails_closed(self) -> None:
        self.write_navigation(["22", "6.4.3"])

        result = self.run_generator(expect_success=False)

        self.assertIn(
            "specification-navigation.json does not contain clause 'B'",
            error_text(result),
        )

    def test_collection_version_change_updates_pinned_diagnostic_links(self) -> None:
        self.run_generator()
        _, ansible = self.read_docs()
        self.assertIn("/blob/ansible-v3.0.2/", ansible)

        (self.root / "ansible" / "galaxy.yml").write_text(
            "namespace: stop_cran\n"
            "name: namespace2xml\n"
            "version: 3.0.3\n",
            encoding="utf-8",
            newline="\n",
        )
        self.run_generator()

        _, updated = self.read_docs()
        self.assertIn("/blob/ansible-v3.0.3/", updated)
        self.assertNotIn("/blob/ansible-v3.0.2/", updated)

    def test_new_registry_code_automatically_gains_both_links(self) -> None:
        self.run_generator()
        self.write_registry(["TEST001", "ADDED002"])

        self.run_generator()
        root, ansible = self.read_docs()

        for document in (root, ansible):
            self.assertIn("[`ADDED002`](#diagnostic-added002)", document)
            self.assertIn('<a id="diagnostic-added002"></a>', document)

    def test_output_is_idempotent_utf8_without_bom_and_lf_terminated(self) -> None:
        self.run_generator()
        paths = (
            self.root / "docs" / "diagnostics.md",
            self.root / "ansible" / "docs" / "diagnostics.md",
        )
        first = [path.read_bytes() for path in paths]

        self.run_generator()
        second = [path.read_bytes() for path in paths]

        self.assertEqual(second, first)
        for data in second:
            self.assertFalse(data.startswith(b"\xef\xbb\xbf"))
            self.assertNotIn(b"\r\n", data)
            self.assertTrue(data.endswith(b"\n"))


if __name__ == "__main__":
    unittest.main()
