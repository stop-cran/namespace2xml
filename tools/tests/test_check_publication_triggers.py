from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "check-publication-triggers.py"
SPEC = importlib.util.spec_from_file_location("check_publication_triggers", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"could not load {MODULE_PATH}")
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class PublicationActionReferenceTests(unittest.TestCase):
    def check(self, reference: str, *, reusable: bool = False) -> list[str]:
        if reusable:
            job = f"    uses: {reference}\n"
        else:
            job = (
                "    runs-on: ubuntu-latest\n"
                "    steps:\n"
                f"      - uses: {reference}\n"
                "      - run: dotnet nuget push package.nupkg\n"
            )

        workflow = (
            "name: Release\n"
            "on:\n"
            "  push:\n"
            "    tags: ['v*']\n"
            "jobs:\n"
            "  publish:\n"
            f"{job}"
            "    permissions:\n"
            "      contents: write\n"
        )
        if reusable:
            workflow += (
                "  evidence:\n"
                "    runs-on: ubuntu-latest\n"
                "    steps:\n"
                "      - run: dotnet nuget push package.nupkg\n"
            )

        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "release.yml"
            path.write_text(workflow, encoding="utf-8", newline="\n")
            return MODULE.check(str(path))

    def test_accepts_full_lowercase_commit_sha(self) -> None:
        self.assertEqual(
            [],
            self.check(
                "actions/checkout@11d5960a326750d5838078e36cf38b85af677262"
            ),
        )

    def test_accepts_local_action(self) -> None:
        self.assertEqual([], self.check("./.github/actions/package"))

    def test_rejects_floating_tag(self) -> None:
        errors = self.check("actions/checkout@v4")
        self.assertTrue(any("40-character lowercase commit SHA" in error for error in errors))

    def test_rejects_short_or_uppercase_sha(self) -> None:
        for reference in (
            "actions/checkout@11d5960",
            "actions/checkout@11D5960A326750D5838078E36CF38B85AF677262",
        ):
            with self.subTest(reference=reference):
                self.assertNotEqual([], self.check(reference))

    def test_checks_reusable_workflow_references(self) -> None:
        errors = self.check(
            "owner/repo/.github/workflows/publish.yml@v1",
            reusable=True,
        )
        self.assertTrue(any("publish.yml@v1" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
