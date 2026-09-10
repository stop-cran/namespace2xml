from __future__ import annotations

import json
import re
import subprocess
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "sync-specification-navigation.ps1"
BEGIN = "<!-- BEGIN GENERATED SPECIFICATION CONTENTS -->"
END = "<!-- END GENERATED SPECIFICATION CONTENTS -->"
ANSI_ESCAPE = re.compile(r"\x1b\[[0-9;]*m")


def error_text(result: subprocess.CompletedProcess[str]) -> str:
    return " ".join(ANSI_ESCAPE.sub("", result.stderr).split())


class SpecificationNavigationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "docs").mkdir()
        (self.root / "spec").mkdir()

    def write_specification(self, body: str) -> None:
        (self.root / "docs" / "specification.md").write_text(
            "# Contract\n\n"
            "**Status:** test\n\n"
            f"{BEGIN}\n"
            f"{END}\n\n"
            f"{body.rstrip()}\n",
            encoding="utf-8",
            newline="\n",
        )

    def run_generator(self, expect_success: bool = True) -> subprocess.CompletedProcess[str]:
        result = subprocess.run(
            [
                "pwsh",
                "-NoProfile",
                "-File",
                str(SCRIPT),
                "-RepositoryRoot",
                str(self.root),
            ],
            check=False,
            capture_output=True,
            text=True,
        )
        if expect_success and result.returncode != 0:
            self.fail(f"generator failed:\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}")
        if not expect_success and result.returncode == 0:
            self.fail("generator unexpectedly succeeded")
        return result

    def generated_specification(self) -> str:
        return (self.root / "docs" / "specification.md").read_text(encoding="utf-8")

    def manifest(self) -> dict[str, object]:
        return json.loads(
            (self.root / "spec" / "specification-navigation.json").read_text(
                encoding="utf-8"
            )
        )

    def test_generates_hierarchical_decimal_and_appendix_navigation(self) -> None:
        self.write_specification(
            "## 1. Purpose\n\n"
            "### 1.1 Scope\n\n"
            "#### 1.1.1 Detail\n\n"
            "#### A type that names a format\n\n"
            "```markdown\n"
            "## 9. Example only\n"
            "```\n\n"
            "## Appendix A. Grammar\n\n"
            "### A.1 Tokens"
        )

        self.run_generator()
        document = self.generated_specification()
        clauses = self.manifest()["clauses"]

        self.assertEqual(
            [clause["section"] for clause in clauses],
            ["1", "1.1", "1.1.1", "A", "A.1"],
        )
        self.assertIn("- [1. Purpose](#spec-1)", document)
        self.assertIn("  - [1.1 Scope](#spec-1-1)", document)
        self.assertIn("    - [1.1.1 Detail](#spec-1-1-1)", document)
        self.assertIn("- [Appendix A. Grammar](#spec-a)", document)
        self.assertIn("  - [A.1 Tokens](#spec-a-1)", document)
        self.assertNotIn("#spec-9", document)
        self.assertNotIn("A type that names a format](", document)
        self.assertEqual(document.count('<a id="spec-1"></a>'), 1)
        self.assertEqual(document.count("](#spec-1)"), 1)

    def test_heading_title_edit_retains_the_same_anchor(self) -> None:
        self.write_specification("## 6. Original title")
        self.run_generator()
        original = self.generated_specification().replace("Original title", "Replacement title")
        (self.root / "docs" / "specification.md").write_text(
            original, encoding="utf-8", newline="\n"
        )

        self.run_generator()

        self.assertIn('<a id="spec-6"></a>\n## 6. Replacement title', self.generated_specification())

    def test_second_run_is_byte_identical_utf8_without_bom_and_lf_terminated(self) -> None:
        self.write_specification("## 1. Café\n\n## Appendix C. Fixtures\n\n### C.4 Portable")
        self.run_generator()
        specification = self.root / "docs" / "specification.md"
        manifest = self.root / "spec" / "specification-navigation.json"
        first = (specification.read_bytes(), manifest.read_bytes())

        self.run_generator()
        second = (specification.read_bytes(), manifest.read_bytes())

        self.assertEqual(first, second)
        for artifact in second:
            self.assertFalse(artifact.startswith(b"\xef\xbb\xbf"))
            self.assertNotIn(b"\r", artifact)
            self.assertTrue(artifact.endswith(b"\n"))

    def test_duplicate_sections_fail_closed(self) -> None:
        for duplicate in ("6.2", "C.4"):
            with self.subTest(duplicate=duplicate):
                if duplicate[0].isdigit():
                    body = (
                        "## 6. Parent\n\n"
                        f"### {duplicate} First\n\n"
                        f"### {duplicate} Second"
                    )
                else:
                    body = (
                        "## Appendix C. Parent\n\n"
                        f"### {duplicate} First\n\n"
                        f"### {duplicate} Second"
                    )
                self.write_specification(body)
                result = self.run_generator(expect_success=False)
                self.assertIn(
                    f"duplicate specification clause '{duplicate}'",
                    error_text(result),
                )

    def test_manual_or_duplicate_reserved_anchor_fails_closed(self) -> None:
        self.write_specification('<a id="spec-manual"></a>\n## 1. Purpose')
        result = self.run_generator(expect_success=False)
        self.assertIn("reserved anchor 'spec-manual'", error_text(result))

        self.write_specification("## 1. Purpose")
        self.run_generator()
        path = self.root / "docs" / "specification.md"
        document = path.read_text(encoding="utf-8")
        path.write_text(
            document.replace(
                '<a id="spec-1"></a>',
                '<a id="spec-1"></a>\n<a id="spec-1"></a>',
            ),
            encoding="utf-8",
            newline="\n",
        )
        result = self.run_generator(expect_success=False)
        self.assertIn("generated anchor 'spec-1'", error_text(result))
        self.assertIn("than once", error_text(result))

    def test_missing_duplicate_nested_and_reversed_markers_fail_closed(self) -> None:
        invalid_blocks = {
            "missing": "",
            "duplicate": f"{BEGIN}\n{END}\n{BEGIN}\n{END}",
            "nested": f"{BEGIN}\n{BEGIN}\n{END}\n{END}",
            "reversed": f"{END}\n{BEGIN}",
        }
        for name, markers in invalid_blocks.items():
            with self.subTest(name=name):
                (self.root / "docs" / "specification.md").write_text(
                    f"# Contract\n\n{markers}\n\n## 1. Purpose\n",
                    encoding="utf-8",
                    newline="\n",
                )
                result = self.run_generator(expect_success=False)
                self.assertIn("generated contents block", error_text(result))

    def test_marker_examples_inside_fences_are_not_generator_markers(self) -> None:
        self.write_specification(
            "## 1. Purpose\n\n"
            "```markdown\n"
            f"{BEGIN}\n"
            f"{END}\n"
            "```\n\n"
            "### 1.1 Scope"
        )

        self.run_generator()
        first = self.generated_specification()
        self.run_generator()

        self.assertEqual(self.generated_specification(), first)
        self.assertEqual(first.count("## Contents"), 1)
        self.assertIn(f"```markdown\n{BEGIN}\n{END}\n```", first)
        self.assertIn('<a id="spec-1-1"></a>\n### 1.1 Scope', first)

    def test_numbered_h5_clause_fails_closed(self) -> None:
        self.write_specification(
            "## 1. Purpose\n\n"
            "### 1.1 Scope\n\n"
            "#### 1.1.1 Detail\n\n"
            "##### 1.1.1.1 Too deep"
        )

        result = self.run_generator(expect_success=False)

        self.assertIn("clauses must use H2 through H4", error_text(result))

    def test_regeneration_restores_contents_and_adds_a_new_subsection(self) -> None:
        self.write_specification("## 1. Purpose\n\n### 1.1 Existing")
        self.run_generator()
        path = self.root / "docs" / "specification.md"
        mutated = path.read_text(encoding="utf-8")
        mutated = mutated.replace("](#spec-1-1)", "](#spec-mistyped)")
        mutated += "\n### 1.2 Added later\n"
        path.write_text(mutated, encoding="utf-8", newline="\n")

        self.run_generator()
        document = self.generated_specification()

        self.assertNotIn("#spec-mistyped", document)
        self.assertIn("  - [1.2 Added later](#spec-1-2)", document)
        self.assertIn('<a id="spec-1-2"></a>\n### 1.2 Added later', document)


if __name__ == "__main__":
    unittest.main()
