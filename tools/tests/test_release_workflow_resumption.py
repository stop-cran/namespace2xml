from __future__ import annotations

import unittest
from pathlib import Path


WORKFLOW = (
    Path(__file__).resolve().parents[2] / ".github" / "workflows" / "release.yml"
)


class ReleaseWorkflowResumptionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")

    def test_candidate_is_retained_before_nuget_authentication(self) -> None:
        retain = self.workflow.index(
            "- name: Retain the candidate outside the workflow-run artifact lifecycle"
        )
        validate = self.workflow.index(
            "- name: Validate draft assets before publication"
        )
        oidc = self.workflow.index(
            "- name: Exchange this run's OIDC identity for a short-lived nuget.org key"
        )

        self.assertLess(retain, validate)
        self.assertLess(validate, oidc)
        retain_block = self.workflow[retain:oidc]
        self.assertIn("candidate-retention-redownload.zip", retain_block)
        self.assertIn("https://uploads.github.com/repos/", retain_block)
        self.assertIn("Retained candidate file", retain_block)

    def test_conflicting_draft_assets_fail_before_nuget_publication(self) -> None:
        validate = self.workflow.index(
            "- name: Validate draft assets before publication"
        )
        upload = self.workflow.index("- name: Upload the immutable release candidate")
        publish = self.workflow.index("- name: Publish only absent NuGet artifacts")
        validate_block = self.workflow[validate:upload]

        self.assertLess(validate, publish)
        self.assertIn("$nugetPublicationIncomplete", validate_block)
        self.assertIn("$finalAssets.Count -ne 0", validate_block)
        self.assertIn("unexpected asset", validate_block)
        self.assertIn("duplicate assets", validate_block)
        self.assertIn("differs from the immutable candidate", validate_block)
        self.assertIn(
            "$nugetPublicationIncomplete -or\n"
            "                      -not [bool] $release.draft -or",
            validate_block,
        )
        self.assertIn(
            "$name -ceq $env:CANDIDATE_RETENTION_NAME", validate_block
        )
        self.assertIn(
            "Final reconciliation deletes and replaces interrupted expected draft assets",
            validate_block,
        )

    def test_rerun_can_restore_outside_the_run_artifact_lifecycle(self) -> None:
        discover = self.workflow.index(
            "- name: Discover the immutable candidate and public release state"
        )
        restore = self.workflow.index(
            "- name: Restore the immutable candidate on reconciliation"
        )
        discovery_block = self.workflow[discover:restore]

        self.assertIn("candidate_source=release-retention", discovery_block)
        self.assertIn("candidate_source=release-assets", discovery_block)
        self.assertIn("download_release_asset", discovery_block)
        self.assertIn("interrupted retention asset", discovery_block)
        self.assertLess(
            discovery_block.index('if [ "$complete_core" = true ]'),
            discovery_block.index(
                "interrupted retention asset in non-rebuildable state"
            ),
        )
        self.assertNotIn("a re-run cannot rebuild", discovery_block)

    def test_creation_uses_the_write_response_as_release_identity(self) -> None:
        retain = self.workflow.index(
            "- name: Retain the candidate outside the workflow-run artifact lifecycle"
        )
        upload = self.workflow.index("- name: Upload the immutable release candidate")
        retain_block = self.workflow[retain:upload]

        create = retain_block.index("gh api --method POST")
        direct_identity = retain_block.index(
            '$release = ($createdJson -join "`n") | ConvertFrom-Json'
        )
        self.assertLess(create, direct_identity)
        self.assertIn("foreach ($delay in @(1, 2, 4, 8, 16))", retain_block)
        self.assertNotIn("gh release create", retain_block)

    def test_final_reconciliation_is_bound_to_release_id(self) -> None:
        reconcile = self.workflow.index(
            "- name: Create or reconcile the GitHub release from the same candidate"
        )
        evidence = self.workflow.index("- name: Upload publication evidence")
        reconcile_block = self.workflow[reconcile:evidence]

        cleanup = reconcile_block.index(
            "Failed to remove the internal candidate-retention asset."
        )
        publish = reconcile_block.index("gh api --method PATCH")
        self.assertLess(cleanup, publish)
        self.assertIn(
            "repos/$env:GITHUB_REPOSITORY/releases/$env:GITHUB_RELEASE_ID",
            reconcile_block,
        )
        self.assertIn("Wait-ForReleaseEndpoint", reconcile_block)
        self.assertIn("releases/tags/$env:GITHUB_REF_NAME", reconcile_block)
        self.assertIn("releases/latest", reconcile_block)
        self.assertNotIn("gh release edit", reconcile_block)
        self.assertNotIn("gh release upload", reconcile_block)

    def test_provenance_does_not_depend_on_ephemeral_artifact_id(self) -> None:
        provenance = self.workflow.index("- name: Record release provenance")
        smoke = self.workflow.index(
            "- name: Install and smoke-test the stable package from nuget.org"
        )
        provenance_block = self.workflow[provenance:smoke]

        self.assertIn("candidateRetentionReleaseId", provenance_block)
        self.assertNotIn("candidateArtifactId", provenance_block)

    def test_release_asset_requests_use_the_workflow_token(self) -> None:
        headers = [
            line.strip()
            for line in self.workflow.splitlines()
            if '--header "Authorization:' in line
        ]

        self.assertEqual(6, len(headers))
        self.assertTrue(all("Authorization: Bearer " in header for header in headers))
        self.assertNotIn("******", "\n".join(headers))
        self.assertEqual(5, sum("$env:GH_TOKEN" in header for header in headers))
        self.assertEqual(1, sum("$GH_TOKEN" in header for header in headers))


if __name__ == "__main__":
    unittest.main()
