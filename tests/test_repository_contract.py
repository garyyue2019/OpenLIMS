from __future__ import annotations

import hashlib
import csv
import json
import os
import re
import subprocess
import sys
import unittest
from pathlib import Path

from tools.specgen.engine import ProjectState, check
from tools.specgen.util import load_json


ROOT = Path(__file__).resolve().parents[1]


class RepositoryContractTests(unittest.TestCase):
    def test_current_repository_is_consistent(self) -> None:
        state = ProjectState.load(ROOT)
        self.assertEqual([], state.validation.errors)
        self.assertEqual((), state.source_drifts)
        self.assertEqual([], check(state))

    def test_cli_output_is_utf8_when_windows_locale_is_not(self) -> None:
        env = os.environ.copy()
        env["PYTHONUTF8"] = "0"
        env["PYTHONIOENCODING"] = "cp1252"
        result = subprocess.run(
            [sys.executable, "-m", "tools.specgen", "validate", "--strict-warnings"],
            cwd=ROOT,
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        self.assertEqual(0, result.returncode, result.stderr.decode("utf-8", errors="replace"))
        self.assertIn("201 个规格版本", result.stdout.decode("utf-8"))

    def test_git_checkout_keeps_deterministic_lf_bytes(self) -> None:
        attributes = (ROOT / ".gitattributes").read_text(encoding="utf-8")
        self.assertIn("* text=auto eol=lf", attributes)
        self.assertIn("*.pptx -text", attributes)

    def test_expected_bootstrap_artifacts_exist(self) -> None:
        tasks = sorted((ROOT / "generated" / "spec" / "tasks").glob("*.md"))
        features = sorted((ROOT / "generated" / "spec" / "features").glob("*.feature"))
        expected_tasks = {
            *(f"ATC-PLT-000__v{version}.md" for version in ("0.1.0", "1.0.0")),
            "ATC-PLT-003__v1.0.0.md",
            *(
                f"ATC-REC-{number:03d}__v{version}.md"
                for version in ("0.1.0", "1.0.0")
                for number in range(1, 7)
            ),
            "ATC-REC-001__v2.0.0.md",
            "ATC-REC-002__v2.0.0.md",
            "ATC-REC-003__v2.0.0.md",
            "ATC-REC-005__v2.0.0.md",
            "ATC-REC-006__v2.0.0.md",
            "ATC-SCP-001__v1.0.0.md",
            "ATC-QTY-001__v1.0.0.md",
            "ATC-ALLOC-001__v1.0.0.md",
            "ATC-TEX-001__v1.0.0.md",
            "ATC-TEX-003__v1.0.0.md",
            "ATC-TEX-004__v1.0.0.md",
            "ATC-BATCH-001__v1.0.0.md",
            "ATC-WEB-001__v1.0.0.md",
            "ATC-WEB-002__v1.0.0.md",
            "ATC-WEB-003__v1.0.0.md",
            "ATC-WEB-004__v1.0.0.md",
            "ATC-WEB-005__v1.0.0.md",
            "ATC-RESULT-001__v1.0.0.md",
            "ATC-BILL-001__v1.0.0.md",
            "ATC-AI-001__v1.0.0.md",
            "ATC-PLT-002__v1.0.0.md",
            "ATC-PLT-001__v1.0.0.md",
            "ATC-GOV-001__v1.0.0.md",
            "ATC-INST-001__v1.0.0.md",
            "ATC-QC-001__v1.0.0.md",
            "ATC-RPT-001__v1.0.0.md",
            "ATC-RPT-002__v1.0.0.md",
            "ATC-TOY-001__v1.0.0.md",
            "ATC-TOY-002__v0.1.0.md",
            "ATC-TOY-003__v0.1.0.md",
            "ATC-TOY-004__v0.1.0.md",
            "ATC-TOY-002__v1.0.0.md",
            "ATC-TOY-003__v1.0.0.md",
            "ATC-TOY-004__v1.0.0.md",
            "ATC-TOY-005__v1.0.0.md",
        }
        self.assertEqual(expected_tasks, {path.name for path in tasks})
        self.assertEqual(80, len(features))
        self.assertTrue(
            {
                "ATC-PLT-000__v0.1.0.feature",
                "ATC-PLT-000__v1.0.0.feature",
                "AC-ID-001__v1.0.0.feature",
                "AC-REC-001__v1.0.0.feature",
                "ATC-REC-003__v2.0.0.feature",
                "ATC-REC-005__v2.0.0.feature",
                "ATC-REC-006__v2.0.0.feature",
                "AC-SCOPE-001__v1.0.0.feature",
                "ATC-SCP-001__v1.0.0.feature",
                "AC-QTY-001__v1.0.0.feature",
                "ATC-QTY-001__v1.0.0.feature",
                "AC-ELEC-003__v1.0.0.feature",
                "ATC-ALLOC-001__v1.0.0.feature",
                "AC-TEXTILE-001__v1.0.0.feature",
                "ATC-TEX-001__v1.0.0.feature",
                "AC-TEXTILE-003__v1.0.0.feature",
                "ATC-TEX-003__v1.0.0.feature",
                "AC-TEXTILE-004__v1.0.0.feature",
                "ATC-TEX-004__v1.0.0.feature",
                "AC-BATCH-001__v1.0.0.feature",
                "ATC-BATCH-001__v1.0.0.feature",
                "ATC-WEB-001__v1.0.0.feature",
                "ATC-WEB-002__v1.0.0.feature",
                "ATC-WEB-003__v1.0.0.feature",
                "ATC-WEB-004__v1.0.0.feature",
                "ATC-WEB-005__v1.0.0.feature",
                "AC-RETEST-001__v1.0.0.feature",
                "ATC-RESULT-001__v1.0.0.feature",
                "AC-BILL-001__v1.0.0.feature",
                "ATC-BILL-001__v1.0.0.feature",
                "AC-AI-003__v1.0.0.feature",
                "ATC-AI-001__v1.0.0.feature",
                "ATC-PLT-002__v1.0.0.feature",
                "ATC-PLT-001__v1.0.0.feature",
                "ATC-GOV-001__v1.0.0.feature",
                "ATC-INST-001__v1.0.0.feature",
                "ATC-QC-001__v1.0.0.feature",
                "AC-QC-001__v1.0.0.feature",
                "ATC-RPT-001__v1.0.0.feature",
                "AC-RPT-001__v1.0.0.feature",
                "AC-ACC-001__v1.0.0.feature",
                "AC-TRACE-001__v1.0.0.feature",
                "ATC-RPT-002__v1.0.0.feature",
                "AC-RPT-002__v1.0.0.feature",
                "AC-TOY-001__v1.0.0.feature",
                "ATC-TOY-001__v1.0.0.feature",
                "ATC-TOY-004__v1.0.0.feature",
                "ATC-TOY-005__v1.0.0.feature",
            }.issubset({path.name for path in features})
        )
        self.assertFalse(any(path.name.startswith("R1-REC-") for path in (*tasks, *features)))

    def test_generated_text_is_utf8_lf_and_has_generated_marker(self) -> None:
        for path in (ROOT / "generated" / "spec").rglob("*"):
            if not path.is_file():
                continue
            raw = path.read_bytes()
            self.assertNotIn(b"\r\n", raw, str(path))
            text = raw.decode("utf-8")
            if path.name != ".specgen-lock.json" and path.suffix != ".csv":
                self.assertIn("generated", text.lower(), str(path))

    def test_textile_runtime_is_registered_with_versioned_openapi_and_projects(self) -> None:
        api_program = (
            ROOT / "src" / "host" / "api" / "OpenLIMS.Api" / "Program.cs"
        ).read_text(encoding="utf-8")
        worker_program = (
            ROOT / "src" / "host" / "worker" / "OpenLIMS.Worker" / "Program.cs"
        ).read_text(encoding="utf-8")
        solution = (ROOT / "OpenLIMS.slnx").read_text(encoding="utf-8")
        migration = (
            ROOT
            / "src"
            / "modules"
            / "textile"
            / "OpenLIMS.Modules.Textile"
            / "TextileMigration.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("new TextileModule(", api_program)
        self.assertIn("new TextileModule(", worker_program)
        for operation in (
            "calculateTextileSampleRequirement",
            "createTextileCuttingPlan",
            "approveTextileCuttingPlan",
            "getTextileCuttingPlan",
        ):
            self.assertIn(f'operationId = "{operation}"', api_program)
        for project in (
            "src/modules/textile/OpenLIMS.Modules.Textile/OpenLIMS.Modules.Textile.csproj",
            "tests/unit/textile/OpenLIMS.Textile.UnitTests/OpenLIMS.Textile.UnitTests.csproj",
            "tests/integration/textile/OpenLIMS.Textile.IntegrationTests/OpenLIMS.Textile.IntegrationTests.csproj",
        ):
            self.assertIn(project, solution)
        self.assertIn("20260728_001_textile_runtime", migration)
        self.assertIn("reject_textile_mutation", migration)

    def test_local_markdown_links_in_ai_manual_and_decision_packets_exist(self) -> None:
        pattern = re.compile(r"\[[^\]]+\]\(([^)]+)\)")
        errors: list[str] = []
        documents = [
            *(ROOT / "docs" / "ai-development").rglob("*.md"),
            *(ROOT / "docs" / "decision-packets").rglob("*.md"),
        ]
        for document in documents:
            for target in pattern.findall(document.read_text(encoding="utf-8")):
                if target.startswith(("http://", "https://", "#")):
                    continue
                cleaned = target.strip("<>").split("#", 1)[0]
                if not cleaned:
                    continue
                resolved = (document.parent / cleaned).resolve()
                if not resolved.exists():
                    errors.append(f"{document.relative_to(ROOT)} -> {target}")
        self.assertEqual([], errors)

    def test_all_repository_json_is_strictly_loadable(self) -> None:
        paths = list((ROOT / "spec").rglob("*.json"))
        paths.extend((ROOT / "generated" / "spec").rglob("*.json"))
        self.assertGreater(len(paths), 0)
        for path in paths:
            with self.subTest(path=path.relative_to(ROOT)):
                load_json(path)

    def test_group_multi_organization_mode_is_pinned(self) -> None:
        decision = load_json(ROOT / "spec" / "decisions" / "OD-002__v1.0.0.json")
        self.assertEqual("approved", decision["status"])
        self.assertEqual("decided", decision["decision_state"])
        self.assertEqual("共享 SaaS 多租户数据平面", decision["rejected_direction"])
        self.assertIn("一个生产部署及其数据平面只绑定一个 OrganizationGroup", decision["decision"])
        self.assertIn("送检客户不是租户", decision["decision"])

        topology = load_json(ROOT / "spec" / "decisions" / "OD-020__v0.1.0.json")
        self.assertIn("OD-002@1.0.0", topology["depends_on"])
        self.assertFalse(any("共享" in option or "多租户" in option for option in topology["options"]))

        release = load_json(
            ROOT / "spec" / "releases" / "REL-R1-RECEIVING-PILOT__v0.1.0.json"
        )
        required = {
            "OD-002@1.0.0",
            "ORG-STRUCT-001@0.1.0",
            "ORG-COLLAB-001@0.1.0",
            "SEC-DEPLOY-001@0.1.0",
            "AC-SEC-001@0.1.0",
            "AC-DEPLOY-001@0.1.0",
        }
        self.assertTrue(required.issubset(set(release["selected_specs"])))

    def test_platform_engineering_skeleton_contract_is_pinned(self) -> None:
        story = load_json(
            ROOT / "spec" / "stories" / "ATC-PLT-000__v0.1.0.json"
        )
        self.assertEqual("proposed", story["status"])
        self.assertEqual("blocked", story["body"]["readiness"])

        expected_dependencies = {
            "ED-001@0.1.0",
            "OD-002@1.0.0",
            "OD-020@0.1.0",
            "OD-025@0.1.0",
            "SEC-DEPLOY-001@0.1.0",
            "SEC-AUD-001@0.1.0",
            "NFR-ARCH-001@0.1.0",
            "NFR-ARCH-002@0.1.0",
            "AC-DEPLOY-001@0.1.0",
        }
        self.assertEqual(expected_dependencies, set(story["depends_on"]))
        self.assertTrue(
            all(re.fullmatch(r"[A-Z][A-Z0-9-]*@\d+\.\d+\.\d+", ref) for ref in story["depends_on"])
        )

        platform_ref = "ATC-PLT-000@0.1.0"
        receiving_stories = sorted(
            (ROOT / "spec" / "stories").glob("ATC-REC-*__v0.1.0.json")
        )
        self.assertEqual(6, len(receiving_stories))
        for path in receiving_stories:
            with self.subTest(story=path.name):
                dependencies = load_json(path)["depends_on"]
                self.assertEqual(1, dependencies.count(platform_ref))

        release = load_json(
            ROOT / "spec" / "releases" / "REL-R1-RECEIVING-PILOT__v0.1.0.json"
        )
        self.assertIn(platform_ref, release["depends_on"])
        self.assertIn(platform_ref, release["selected_specs"])

        forbidden_roots = ("spec", "generated", "src/modules", "src/packs")
        for raw_path in story["body"]["allowed_paths"]:
            normalized = raw_path.replace("\\", "/").lstrip("./")
            with self.subTest(allowed_path=raw_path):
                self.assertFalse(
                    any(
                        normalized == root or normalized.startswith(f"{root}/")
                        for root in forbidden_roots
                    )
                )

        commands = story["body"]["verification_commands"]
        self.assertGreater(len(commands), 0)
        placeholder_markers = (
            "PLACEHOLDER",
            "TODO",
            "TBD",
            "REQUIRED_BY",
            "_REQUIRED",
        )
        for command in commands:
            with self.subTest(command=command):
                self.assertTrue(command.strip())
                upper = command.upper()
                self.assertFalse(any(marker in upper for marker in placeholder_markers))
                self.assertIsNone(re.search(r"<[^>]+>", command))

        stable_script_commands = {
            "pwsh -NoProfile -File scripts/verify.ps1 -Profile task -Module platform",
            "pwsh -NoProfile -File scripts/verify.ps1 -Profile architecture",
            "pwsh -NoProfile -File scripts/verify.ps1 -Profile contracts",
            "pwsh -NoProfile -File scripts/verify.ps1 -Profile all",
            "bash scripts/verify.sh --profile task --module platform",
            "bash scripts/verify.sh --profile architecture",
            "bash scripts/verify.sh --profile contracts",
            "bash scripts/verify.sh --profile all",
        }
        self.assertTrue(stable_script_commands.issubset(set(commands)))

        repository_gate_commands = {
            "python -m tools.specgen validate --strict-warnings",
            "python -m tools.specgen source-status",
            "python -m tools.specgen verify-history",
            "python -m tools.specgen generate",
            "python -m tools.specgen check",
            "python -m unittest discover -s tests -p test_*.py",
        }
        self.assertTrue(repository_gate_commands.issubset(set(commands)))
        self.assertIn("tests/unit/platform/**", story["body"]["allowed_paths"])

        error_codes = story["body"]["api_contract"]["errorCodes"]
        self.assertEqual(
            {
                "PLT.GROUP_CONTEXT_OVERRIDE_FORBIDDEN",
                "AUTH.ORGANIZATION_GROUP_MISMATCH",
                "PLT.CONFIGURATION_INVALID",
                "PLT.DEPENDENCY_UNREADY",
            },
            set(error_codes),
        )
        observability = " ".join(story["body"]["observability"])
        self.assertIn("每个集团", observability)
        self.assertIn("不批准共享可观测性数据平面", observability)

        test_types = {case["type"] for case in story["body"]["test_cases"]}
        required_test_types = {
            "positive",
            "negative",
            "boundary",
            "permission",
            "concurrency",
            "audit",
            "architecture",
            "security",
            "deployment-isolation",
            "transaction",
            "idempotency",
            "recovery",
            "migration",
            "supply-chain",
            "cross-platform",
        }
        self.assertTrue(required_test_types.issubset(test_types))

        engineering = load_json(
            ROOT / "spec" / "decisions" / "ED-001__v0.1.0.json"
        )["engineering_skeleton_task"]
        self.assertEqual(platform_ref, engineering["task_ref"])
        self.assertEqual("SPEC_CREATED_PROPOSED_BLOCKED", engineering["status"])
        self.assertEqual(
            "docs/decision-packets/ATC-PLT-000-ENGINEERING-SKELETON-REVIEW.md",
            engineering["review_ref"],
        )

    def test_platform_major_machine_drafts_are_unapproved_and_dependency_scoped(self) -> None:
        planned_refs = {
            "ED-001@1.0.0",
            "ED-002@1.0.0",
            "SEC-DEPLOY-001@1.0.0",
            "SEC-AUD-001@1.0.0",
            "NFR-ARCH-001@1.0.0",
            "NFR-ARCH-002@1.0.0",
            "AC-DEPLOY-001@1.0.0",
            "ATC-PLT-000@1.0.0",
            "ATC-PLT-003@1.0.0",
            "REL-R1-RECEIVING-PILOT@1.0.0",
            *(f"ATC-REC-{number:03d}@1.0.0" for number in range(1, 7)),
        }
        objects: dict[str, dict] = {}
        for path in (ROOT / "spec").rglob("*.json"):
            item = load_json(path)
            if not isinstance(item, dict) or "id" not in item or "version" not in item:
                continue
            objects[f'{item["id"]}@{item["version"]}'] = item

        self.assertTrue(planned_refs.issubset(objects))
        approved_delivery_v1_refs = {
            "AC-ID-001@1.0.0",
            "AC-REC-001@1.0.0",
            "AC-SEC-001@1.0.0",
            "OD-035@1.0.0",
            "OD-009@1.0.0",
            "OPS-IDENTITY-001@1.0.0",
            "OPS-IDENTITY-002@1.0.0",
            "OPS-IDENTITY-003@1.0.0",
            "OPS-RECEIPT-001@1.0.0",
            "OPS-RECEIPT-003@1.0.0",
            "ORG-COLLAB-001@1.0.0",
            "ORG-STRUCT-001@1.0.0",
            "SEC-AUTH-001@1.0.0",
            "OD-031@1.0.0",
            "OPS-RECEIPT-002@1.0.0",
            "OD-005@1.0.0",
            "OPS-EXC-001@1.0.0",
            "OPS-EXC-002@1.0.0",
            "OD-027@1.0.0",
            "BUS-SCOPE-001@1.0.0",
            "BUS-SCOPE-002@1.0.0",
            "BUS-SCOPE-003@1.0.0",
            "AC-SCOPE-001@1.0.0",
            "ATC-SCP-001@1.0.0",
            "OD-010@1.0.0",
            "BUS-QTY-001@1.0.0",
            "BUS-QTY-002@1.0.0",
            "BUS-QTY-003@1.0.0",
            "AC-QTY-001@1.0.0",
            "ATC-QTY-001@1.0.0",
            "BUS-ALLOC-001@1.0.0",
            "BUS-ALLOC-002@1.0.0",
            "BUS-ALLOC-003@1.0.0",
            "AC-ELEC-003@1.0.0",
            "ATC-ALLOC-001@1.0.0",
            "BUS-TEX-001@1.0.0",
            "BUS-TEX-002@1.0.0",
            "BUS-TEX-003@1.0.0",
            "AC-TEXTILE-001@1.0.0",
            "ATC-TEX-001@1.0.0",
            "BUS-TEX-004@1.0.0",
            "BUS-TEX-005@1.0.0",
            "AC-TEXTILE-003@1.0.0",
            "ATC-TEX-003@1.0.0",
            "OD-036@1.0.0",
            "BUS-TEX-006@1.0.0",
            "BUS-TEX-007@1.0.0",
            "BUS-TEX-008@1.0.0",
            "AC-TEXTILE-004@1.0.0",
            "ATC-TEX-004@1.0.0",
            "OD-030@1.0.0",
            "BUS-BATCH-001@1.0.0",
            "BUS-BATCH-002@1.0.0",
            "BUS-BATCH-003@1.0.0",
            "AC-BATCH-001@1.0.0",
            "ATC-BATCH-001@1.0.0",
            "ATC-WEB-001@1.0.0",
            "ATC-WEB-002@1.0.0",
            "ATC-WEB-003@1.0.0",
            "ATC-WEB-004@1.0.0",
            "ATC-WEB-005@1.0.0",
            "BUS-RES-001@1.0.0",
            "BUS-RES-002@1.0.0",
            "BUS-RES-003@1.0.0",
            "AC-RETEST-001@1.0.0",
            "ATC-RESULT-001@1.0.0",
            "BUS-BILL-001@1.0.0",
            "BUS-BILL-002@1.0.0",
            "BUS-BILL-003@1.0.0",
            "AC-BILL-001@1.0.0",
            "ATC-BILL-001@1.0.0",
            "BUS-AI-001@1.0.0",
            "BUS-AI-002@1.0.0",
            "BUS-AI-003@1.0.0",
            "AC-AI-003@1.0.0",
            "ATC-AI-001@1.0.0",
            "BUS-PLT-001@1.0.0",
            "ATC-PLT-002@1.0.0",
            "BUS-PLT-002@1.0.0",
            "ATC-PLT-001@1.0.0",
            "OD-001@1.0.0",
            "BUS-GOV-001@1.0.0",
            "ATC-GOV-001@1.0.0",
            "BUS-INST-001@1.0.0",
            "BUS-INST-002@1.0.0",
            "BUS-INST-003@1.0.0",
            "ATC-INST-001@1.0.0",
            "BUS-QC-001@1.0.0",
            "BUS-QC-002@1.0.0",
            "BUS-QC-003@1.0.0",
            "AC-QC-001@1.0.0",
            "ATC-QC-001@1.0.0",
            "OD-011@1.0.0",
            "OD-022@1.0.0",
            "OD-029@1.0.0",
            "BUS-RPT-001@1.0.0",
            "BUS-RPT-002@1.0.0",
            "BUS-RPT-003@1.0.0",
            "AC-RPT-001@1.0.0",
            "AC-ACC-001@1.0.0",
            "AC-TRACE-001@1.0.0",
            "ATC-RPT-001@1.0.0",
            "BUS-RPT-004@1.0.0",
            "BUS-RPT-005@1.0.0",
            "AC-RPT-002@1.0.0",
            "ATC-RPT-002@1.0.0",
            "BUS-TOY-001@1.0.0",
            "BUS-TOY-002@1.0.0",
            "AC-TOY-001@1.0.0",
            "ATC-TOY-001@1.0.0",
            "BUS-TOY-003@1.0.0",
            "BUS-TOY-004@1.0.0",
            "BUS-TOY-005@1.0.0",
            "OD-034@1.0.0",
            "BUS-TOY-006@1.0.0",
            "AC-TOY-002@1.0.0",
            "AC-TOY-003@1.0.0",
            "AC-TOY-004@1.0.0",
            "ATC-TOY-002@1.0.0",
            "ATC-TOY-003@1.0.0",
            "ATC-TOY-004@1.0.0",
            "ATC-TOY-005@1.0.0",
        }
        self.assertEqual(
            planned_refs | approved_delivery_v1_refs,
            {
                ref
                for ref in objects
                if ref.endswith("@1.0.0") and ref != "OD-002@1.0.0"
            },
        )
        approval_evidence_sources = {
            "AC-TOY-002@1.0.0": "ATC-TOY-004@1.0.0",
        }
        for ref in approved_delivery_v1_refs:
            with self.subTest(approved_delivery_dependency=ref):
                self.assertEqual("approved", objects[ref]["status"])
                evidence_source_ref = approval_evidence_sources.get(ref, ref)
                evidence_source = objects[evidence_source_ref]
                self.assertEqual("approved", evidence_source["status"])
                if evidence_source_ref != ref:
                    self.assertIn(ref, evidence_source["depends_on"])
                approval_evidence = evidence_source.get(
                    "approval_evidence",
                    evidence_source.get("body", {}).get("approval_evidence", ""),
                )
                self.assertIn("用户", approval_evidence)

        decision_states = {
            "ED-001@1.0.0": ("proposed", "open"),
            "ED-002@1.0.0": ("proposed", "open"),
        }
        for ref, (status, decision_state) in decision_states.items():
            with self.subTest(decision=ref):
                self.assertEqual(status, objects[ref]["status"])
                self.assertEqual(decision_state, objects[ref]["decision_state"])
                self.assertFalse(objects[ref]["evidence_state"]["implementation_authorized"])
                self.assertEqual([], objects[ref]["evidence_state"]["verified_review_record_refs"])

        for ref in (
            "SEC-DEPLOY-001@1.0.0",
            "SEC-AUD-001@1.0.0",
            "NFR-ARCH-001@1.0.0",
            "NFR-ARCH-002@1.0.0",
            "AC-DEPLOY-001@1.0.0",
        ):
            with self.subTest(in_review=ref):
                self.assertEqual("in_review", objects[ref]["status"])

        for ref in planned_refs - {"ATC-PLT-003@1.0.0"}:
            with self.subTest(no_false_approval=ref):
                item = objects[ref]
                self.assertNotEqual("approved", item["status"])
                self.assertNotEqual("decided", item.get("decision_state"))
                self.assertNotEqual("ready", item.get("body", {}).get("readiness"))

        approved_module_onboarding = objects["ATC-PLT-003@1.0.0"]
        self.assertEqual("approved", approved_module_onboarding["status"])
        self.assertEqual("ready", approved_module_onboarding["body"]["readiness"])
        self.assertEqual("DEV-002", approved_module_onboarding["body"]["implementation_task_id"])

        dev003 = objects["ATC-REC-001@2.0.0"]
        self.assertEqual("approved", dev003["status"])
        self.assertEqual("ready", dev003["body"]["readiness"])
        self.assertEqual("DEV-003", dev003["body"]["implementation_task_id"])
        self.assertNotIn("ATC-PLT-000@1.0.0", dev003["depends_on"])
        self.assertIn("ATC-PLT-003@1.0.0", dev003["depends_on"])
        for dependency in dev003["depends_on"]:
            with self.subTest(dev003_dependency=dependency):
                self.assertEqual("approved", objects[dependency]["status"])
        self.assertIn("用户", dev003["body"]["approval_evidence"])

        dev005 = objects["ATC-REC-003@2.0.0"]
        self.assertEqual("approved", dev005["status"])
        self.assertEqual("ready", dev005["body"]["readiness"])
        self.assertEqual("DEV-005", dev005["body"]["implementation_task_id"])
        self.assertIn("OD-035@1.0.0", dev005["depends_on"])
        self.assertNotIn("OPS-EXC-001@0.1.0", dev005["depends_on"])
        for dependency in dev005["depends_on"]:
            with self.subTest(dev005_dependency=dependency):
                self.assertEqual("approved", objects[dependency]["status"])
        self.assertIn("用户", dev005["body"]["approval_evidence"])

        dev008 = objects["ATC-SCP-001@1.0.0"]
        self.assertEqual("approved", dev008["status"])
        self.assertEqual("ready", dev008["body"]["readiness"])
        self.assertEqual("DEV-008", dev008["body"]["implementation_task_id"])
        self.assertIn("OD-027@1.0.0", dev008["depends_on"])
        self.assertEqual(
            {"scope.approve"},
            {
                capability
                for permission in dev008["body"]["permissions"]
                for capability in re.findall(r"scope\.[a-z]+", permission)
            },
        )
        for dependency in dev008["depends_on"]:
            with self.subTest(dev008_dependency=dependency):
                self.assertEqual("approved", objects[dependency]["status"])
        self.assertIn("用户", dev008["body"]["approval_evidence"])

        expected_platform_dependencies = {
            "ED-002@1.0.0": {"OD-002@1.0.0"},
            "ED-001@1.0.0": {"OD-002@1.0.0", "ED-002@1.0.0"},
            "SEC-DEPLOY-001@1.0.0": {"OD-002@1.0.0"},
            "SEC-AUD-001@1.0.0": set(),
            "NFR-ARCH-001@1.0.0": {"ED-002@1.0.0"},
            "NFR-ARCH-002@1.0.0": {
                "NFR-ARCH-001@1.0.0",
                "SEC-AUD-001@1.0.0",
            },
            "AC-DEPLOY-001@1.0.0": {
                "OD-002@1.0.0",
                "SEC-DEPLOY-001@1.0.0",
            },
            "ATC-PLT-000@1.0.0": {
                "ED-001@1.0.0",
                "ED-002@1.0.0",
                "OD-002@1.0.0",
                "SEC-DEPLOY-001@1.0.0",
                "SEC-AUD-001@1.0.0",
                "NFR-ARCH-001@1.0.0",
                "NFR-ARCH-002@1.0.0",
                "AC-DEPLOY-001@1.0.0",
            },
        }
        for ref, expected in expected_platform_dependencies.items():
            with self.subTest(platform_dependency=ref):
                self.assertEqual(expected, set(objects[ref].get("depends_on", [])))

        graph = {ref: set(item.get("depends_on", [])) for ref, item in objects.items()}
        pending = list(graph["ATC-PLT-000@1.0.0"])
        closure: set[str] = set()
        while pending:
            ref = pending.pop()
            if ref in closure:
                continue
            closure.add(ref)
            pending.extend(graph.get(ref, set()))
        self.assertFalse(
            any(ref.startswith(("OD-020@", "OD-025@")) for ref in closure),
            closure,
        )

        engineering = objects["ED-001@1.0.0"]
        locks = engineering["version_locks"]
        self.assertEqual(15, len(locks))
        self.assertTrue(all(lock["exact_value"] is None for lock in locks))
        self.assertTrue(all(lock["status"] == "PENDING_VERIFICATION" for lock in locks))
        self.assertEqual(
            "PENDING_REVIEW_FOR_ENGINEERING_SKELETON_ONLY",
            engineering["candidate_stack"]["status"],
        )

        platform_story = objects["ATC-PLT-000@1.0.0"]
        self.assertEqual("proposed", platform_story["status"])
        self.assertEqual("blocked", platform_story["body"]["readiness"])
        self.assertEqual(
            "REL-R1-RECEIVING-PILOT@1.0.0",
            platform_story["target_release"],
        )
        source_items = {ref["item"] for ref in platform_story["source_refs"]}
        self.assertTrue({"OD-020", "OD-025"}.isdisjoint(source_items))
        preconditions = " ".join(platform_story["body"]["preconditions"])
        self.assertNotIn("OD-020", preconditions)
        self.assertNotIn("OD-025", preconditions)
        envelope = platform_story["body"]["non_production_verification_envelope"]
        self.assertEqual(
            {"合成依赖", "健康", "故障", "恢复", "并发", "测试证据"},
            set(envelope["scope"]),
        )
        exclusions = " ".join(envelope["does_not_prove"])
        for excluded_claim in ("500单", "SLA", "RPO", "RTO", "生产拓扑"):
            self.assertIn(excluded_claim, exclusions)

        old_release = objects["REL-R1-RECEIVING-PILOT@0.1.0"]
        release = objects["REL-R1-RECEIVING-PILOT@1.0.0"]
        self.assertEqual("proposed", release["status"])
        self.assertEqual(
            {
                "ED-001@1.0.0",
                "ED-002@1.0.0",
                "OD-001@0.1.0",
                "OD-002@1.0.0",
                "OD-005@0.1.0",
                "OD-009@0.1.0",
                "ATC-PLT-000@1.0.0",
                *(f"ATC-REC-{number:03d}@1.0.0" for number in range(1, 7)),
            },
            set(release["depends_on"]),
        )
        replacements = {
            "ED-001@0.1.0": "ED-001@1.0.0",
            "SEC-DEPLOY-001@0.1.0": "SEC-DEPLOY-001@1.0.0",
            "SEC-AUD-001@0.1.0": "SEC-AUD-001@1.0.0",
            "NFR-ARCH-001@0.1.0": "NFR-ARCH-001@1.0.0",
            "NFR-ARCH-002@0.1.0": "NFR-ARCH-002@1.0.0",
            "AC-DEPLOY-001@0.1.0": "AC-DEPLOY-001@1.0.0",
            "ATC-PLT-000@0.1.0": "ATC-PLT-000@1.0.0",
            **{
                f"ATC-REC-{number:03d}@0.1.0": f"ATC-REC-{number:03d}@1.0.0"
                for number in range(1, 7)
            },
        }
        expected_selected = {
            replacements.get(ref, ref) for ref in old_release["selected_specs"]
        }
        expected_selected.add("ED-002@1.0.0")
        self.assertEqual(expected_selected, set(release["selected_specs"]))
        self.assertEqual(len(expected_selected), len(release["selected_specs"]))
        self.assertTrue(
            {"OD-020@0.1.0", "OD-025@0.1.0"}.issubset(release["selected_specs"])
        )

        for number in range(1, 7):
            story_id = f"ATC-REC-{number:03d}"
            old_story = objects[f"{story_id}@0.1.0"]
            new_story = objects[f"{story_id}@1.0.0"]
            with self.subTest(receiving_story=story_id):
                self.assertEqual("proposed", new_story["status"])
                self.assertEqual("blocked", new_story["body"]["readiness"])
                self.assertEqual(
                    "REL-R1-RECEIVING-PILOT@1.0.0",
                    new_story["target_release"],
                )
                self.assertEqual(
                    [replacements.get(ref, ref) for ref in old_story["depends_on"]],
                    new_story["depends_on"],
                )
                for key in old_story:
                    if key not in {"version", "target_release", "depends_on"}:
                        self.assertEqual(old_story[key], new_story[key], key)

    def test_platform_joint_approval_packet_is_draft_and_evidence_ready(self) -> None:
        packet_path = (
            ROOT
            / "docs"
            / "decision-packets"
            / "ATC-PLT-000-JOINT-APPROVAL-PACKET.md"
        )
        packet = packet_path.read_text(encoding="utf-8")
        self.assertIn("DRAFT / NOT APPROVED / DO NOT IMPLEMENT", packet)
        self.assertIn("CHANGE-PLT-DEPENDENCY-SCOPE-001", packet)
        self.assertIn("ATC-PLT-000@0.1.0`继续保持`proposed/blocked", packet)
        self.assertIn("OD-020@0.1.0", packet)
        self.assertIn("OD-025@0.1.0", packet)
        self.assertIn("继续由Release、生产验证和业务包决策阻断", packet)
        self.assertIn("15个`1.0.0`未批准机器草案", packet)
        self.assertIn("创建草案不属于批准状态变更", packet)
        self.assertIn("没有任何新Decision、NFR、Acceptance或Story被标为`approved/decided/ready`", packet)
        self.assertIn(
            "USER_CONFIRMED_PENDING_CONTROLLED_IDENTITY_AND_ROLE_APPROVAL",
            packet,
        )
        self.assertIn("对应变更集：`CHANGE-PLT-NEXT-VERSIONS-001`", packet)
        self.assertIn("受控身份引用：未提供", packet)
        self.assertIn(
            "33条活动记录继续保持`decision=PENDING`和`record_status=DRAFT`",
            packet,
        )
        sponsor_choices = {
            "RV-PLT-001 DEPENDENCY_SCOPE_SPLIT": "ACCEPT",
            "RV-PLT-002 STACK_CANDIDATE": "ACCEPT",
            "RV-PLT-003 MODULE_BOUNDARY": "ACCEPT",
            "RV-PLT-004 NON_PRODUCTION_ENV": "ACCEPT",
            "RV-PLT-005 GROUP_ISOLATION": "ACCEPT",
            "RV-PLT-006 AUDIT_MODEL": "OPTION_A",
            "RV-PLT-007 SUPPLY_CHAIN_GATES": "ACCEPT",
            "RV-PLT-008 TASK_SCOPE": "ACCEPT",
        }
        for review_item, choice in sponsor_choices.items():
            with self.subTest(sponsor_choice=review_item):
                self.assertIn(f"`{review_item}`", packet)
                self.assertRegex(
                    packet,
                    rf"\| `{re.escape(review_item)}` \| `{choice}`",
                )
        for review_item in range(1, 9):
            with self.subTest(review_item=review_item):
                self.assertIn(f"RV-PLT-{review_item:03d}", packet)
        for conclusion in (
            "ACCEPT",
            "ACCEPT_WITH_CONDITIONS",
            "REJECT",
            "ABSTAIN",
            "PENDING",
        ):
            self.assertIn(f"`{conclusion}`", packet)

        digest_path = packet_path.with_suffix(".sha256")
        expected_digest, recorded_name = digest_path.read_text(encoding="ascii").split()
        self.assertEqual(packet_path.name, recorded_name)
        self.assertEqual(
            expected_digest,
            hashlib.sha256(packet_path.read_bytes()).hexdigest(),
            "评审包变化后必须重新生成哈希并重新收集签署，不能沿用旧批准",
        )

        template_path = (
            ROOT
            / "docs"
            / "decision-packets"
            / "templates"
            / "atc-plt-000-review-record.csv"
        )
        rows = template_path.read_text(encoding="utf-8").splitlines()
        self.assertEqual(1, len(rows), "模板不得预填或伪造任何批准记录")
        header = set(rows[0].split(","))
        required_fields = {
            "review_record_id",
            "change_set_id",
            "subject_ref",
            "subject_hash",
            "review_item_id",
            "role_slot",
            "reviewer_identity_ref",
            "authority_scope",
            "authority_evidence_ref",
            "decision",
            "conditions",
            "blocking_objections",
            "evidence_refs",
            "reviewed_at",
            "signature_or_approval_ref",
            "record_status",
        }
        self.assertTrue(required_fields.issubset(header))

    def test_platform_next_version_changeset_and_pending_roster_are_locked(self) -> None:
        packet_dir = ROOT / "docs" / "decision-packets"
        changeset_path = packet_dir / "ATC-PLT-000-NEXT-VERSION-CHANGESET.md"
        changeset = changeset_path.read_text(encoding="utf-8")
        self.assertIn("DRAFT / NOT APPROVED / DO NOT APPLY", changeset)
        self.assertIn("CHANGE-PLT-NEXT-VERSIONS-001", changeset)
        self.assertIn("OD-020@0.1.0`和`OD-025@0.1.0`必须继续保留", changeset)
        self.assertIn("所有锁值当前状态均为`PENDING_VERIFICATION`", changeset)
        self.assertIn("15个`1.0.0`未批准机器草案", changeset)
        self.assertIn("15个新Major机器草案已经创建", changeset)
        self.assertIn("不存在新`approved/decided/ready`对象", changeset)

        planned_versions = {
            "ED-001@1.0.0",
            "ED-002@1.0.0",
            "SEC-DEPLOY-001@1.0.0",
            "SEC-AUD-001@1.0.0",
            "NFR-ARCH-001@1.0.0",
            "NFR-ARCH-002@1.0.0",
            "AC-DEPLOY-001@1.0.0",
            "ATC-PLT-000@1.0.0",
            "REL-R1-RECEIVING-PILOT@1.0.0",
            *(f"ATC-REC-{number:03d}@1.0.0" for number in range(1, 7)),
        }
        for version_ref in planned_versions:
            with self.subTest(version_ref=version_ref):
                self.assertIn(f"`{version_ref}`", changeset)

        digest_path = changeset_path.with_suffix(".sha256")
        expected_digest, recorded_name = digest_path.read_text(encoding="ascii").split()
        self.assertEqual(changeset_path.name, recorded_name)
        actual_digest = hashlib.sha256(changeset_path.read_bytes()).hexdigest()
        self.assertEqual(expected_digest, actual_digest)

        roster_path = (
            packet_dir
            / "review-records"
            / "CHANGE-PLT-NEXT-VERSIONS-001__draft.csv"
        )
        with roster_path.open("r", encoding="utf-8", newline="") as handle:
            rows = list(csv.DictReader(handle))
        self.assertEqual(33, len(rows))
        self.assertEqual(33, len({row["review_record_id"] for row in rows}))
        self.assertTrue(all(row["change_set_id"] == "CHANGE-PLT-NEXT-VERSIONS-001" for row in rows))
        self.assertTrue(all(row["subject_ref"] == "CHANGE-PLT-NEXT-VERSIONS-001" for row in rows))
        self.assertTrue(all(row["subject_hash"] == actual_digest for row in rows))
        self.assertTrue(all(row["decision"] == "PENDING" for row in rows))
        self.assertTrue(all(row["record_status"] == "DRAFT" for row in rows))
        for field in (
            "reviewer_identity_ref",
            "authority_scope",
            "authority_evidence_ref",
            "reviewed_at",
            "signature_or_approval_ref",
        ):
            with self.subTest(blank_field=field):
                self.assertTrue(all(not row[field] for row in rows))

        expected_roles = {
            "RV-PLT-001": {"PRODUCT_OWNER", "ARCHITECTURE_OWNER", "ENGINEERING_OWNER"},
            "RV-PLT-002": {"ARCHITECTURE_OWNER", "ENGINEERING_OWNER", "SECURITY_OWNER", "OPERATIONS_OWNER"},
            "RV-PLT-003": {"ARCHITECTURE_OWNER", "ENGINEERING_OWNER", "QA_OWNER"},
            "RV-PLT-004": {"ENGINEERING_OWNER", "OPERATIONS_OWNER", "QA_OWNER", "SECURITY_OWNER"},
            "RV-PLT-005": {"ARCHITECTURE_OWNER", "SECURITY_OWNER", "OPERATIONS_OWNER", "QA_OWNER"},
            "RV-PLT-006": {"QUALITY_OWNER", "AUDIT_OWNER", "SECURITY_OWNER", "ARCHITECTURE_OWNER", "OPERATIONS_OWNER"},
            "RV-PLT-007": {"ENGINEERING_OWNER", "SECURITY_OWNER", "OPERATIONS_OWNER", "QA_OWNER"},
            "RV-PLT-008": {"PRODUCT_OWNER", "ARCHITECTURE_OWNER", "ENGINEERING_OWNER", "SECURITY_OWNER", "OPERATIONS_OWNER", "QA_OWNER"},
        }
        actual_roles: dict[str, set[str]] = {}
        for row in rows:
            actual_roles.setdefault(row["review_item_id"], set()).add(row["role_slot"])
        self.assertEqual(expected_roles, actual_roles)

    def test_review_status_reports_real_pending_evidence_without_writes(self) -> None:
        packet_dir = ROOT / "docs" / "decision-packets"
        protected_paths = [
            packet_dir / "ATC-PLT-000-NEXT-VERSION-CHANGESET.md",
            packet_dir / "ATC-PLT-000-NEXT-VERSION-CHANGESET.sha256",
            packet_dir
            / "review-records"
            / "CHANGE-PLT-NEXT-VERSIONS-001__draft.csv",
            ROOT / "spec" / "decisions" / "ED-001__v1.0.0.json",
        ]
        before = {path: path.read_bytes() for path in protected_paths}
        command = [
            sys.executable,
            "-m",
            "tools.specgen",
            "review-status",
            "--change-set",
            "CHANGE-PLT-NEXT-VERSIONS-001",
        ]

        result = subprocess.run(
            command,
            cwd=ROOT,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        self.assertEqual(4, result.returncode, result.stderr.decode("utf-8"))
        output = result.stdout.decode("utf-8")
        self.assertIn("REVIEW BLOCKED CHANGE-PLT-NEXT-VERSIONS-001", output)
        self.assertIn("RECORDS active=33 accepted=0 required=33", output)
        self.assertIn("VERSION LOCKS verified=0 total=15", output)
        self.assertIn("不会批准规格", output)

        json_result = subprocess.run(
            [*command, "--json"],
            cwd=ROOT,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        self.assertEqual(4, json_result.returncode, json_result.stderr.decode("utf-8"))
        payload = json.loads(json_result.stdout.decode("utf-8"))
        self.assertEqual("BLOCKED", payload["status"])
        self.assertEqual(33, payload["review_records"]["required_role_slots"])
        self.assertEqual(0, payload["review_records"]["verified_acceptances"])
        self.assertEqual(15, payload["version_locks"]["total"])
        self.assertEqual(0, payload["version_locks"]["verified"])
        self.assertEqual(48, len(payload["blockers"]))
        self.assertEqual(
            before,
            {path: path.read_bytes() for path in protected_paths},
        )

    def test_release1_intake_is_normalized_without_false_approval(self) -> None:
        decision_paths = {
            decision_id: ROOT / "spec" / "decisions" / f"{decision_id}__v0.1.0.json"
            for decision_id in ("OD-001", "OD-020", "OD-025", "ED-001")
        }
        decisions = {
            decision_id: load_json(path)
            for decision_id, path in decision_paths.items()
        }
        for decision_id, payload in decisions.items():
            with self.subTest(decision=decision_id):
                self.assertEqual("proposed", payload["status"])
                self.assertEqual("open", payload["decision_state"])
                self.assertIsNone(payload["decision"])

        scope = decisions["OD-001"]
        intake = scope["intake_snapshot"]
        self.assertEqual("玩具婴童产品", intake["release1_industry_direction"])
        self.assertEqual(["中国内销", "欧盟", "美国"], intake["target_market_candidates"])
        self.assertEqual(["物理机械", "分析化学"], intake["candidate_technical_packs"])
        self.assertEqual(["微生物/生物"], intake["release1_excluded_technical_capabilities"])
        confirmation = scope["scope_choice_confirmation"]
        self.assertEqual("ACCEPTED", confirmation["product_eligibility"]["user_response"])
        self.assertEqual(
            ["R1.0 中国内销", "后续欧盟版本增量", "后续美国版本增量"],
            confirmation["market_sequence"]["value"],
        )
        self.assertEqual("ACCEPTED", confirmation["market_sequence"]["user_response"])
        self.assertEqual("分析化学", confirmation["primary_technical_pack"]["value"])
        self.assertEqual("物理机械", confirmation["deferred_technical_pack"])
        self.assertEqual("分析化学", scope["technical_pack_governance"]["selected_release1_primary"])
        self.assertEqual("物理机械", scope["technical_pack_governance"]["deferred_candidate"])
        self.assertEqual("GS-TOY-CHEM-001", scope["golden_scenario_candidate"]["id"])
        self.assertEqual("SYNTHETIC_SANDBOX_ONLY", scope["lighthouse_context"]["current_evidence_treatment"])
        self.assertEqual("NOT_PROVIDED", scope["lighthouse_context"]["paying_lighthouse_evidence"])
        self.assertEqual(4, len(scope["stakeholder_slots"]))
        self.assertTrue(
            all(slot["assignee_ref"] == "PENDING_CONTROLLED_ROSTER" for slot in scope["stakeholder_slots"])
        )
        serialized_scope = json.dumps(scope, ensure_ascii=False)
        self.assertNotIn("纺织常规面料", serialized_scope)
        self.assertNotIn("纺织常规成衣", serialized_scope)
        self.assertNotIn("GS-TOY-PHY-001", serialized_scope)

        capacity = decisions["OD-020"]["capacity_intake"]
        self.assertEqual("500订单/日", capacity["normalized_value"])
        self.assertEqual("ORDER", capacity["unit"])
        self.assertEqual("DAILY_AVERAGE", capacity["statistical_scope"])
        self.assertEqual("NOT_IDENTIFIED", capacity["production_site_status"])
        load = decisions["OD-020"]["provisional_test_envelope"]
        self.assertIn("分析化学", load["method_profiles"][0])
        self.assertIn("物理机械Release 1生产负载", load["explicit_exclusions"])

        pack = decisions["OD-025"]
        self.assertEqual(
            "EXCLUDED_FROM_RELEASE_1_CONFIRMED_BY_USER",
            pack["microbiology_candidate_review"]["release1_role"],
        )
        self.assertEqual(
            "分析化学（用户明确选择，待正式责任角色批准）",
            pack["intake_snapshot"]["release1_primary_technical_pack_selection"],
        )
        self.assertEqual("分析化学", pack["release1_pack_boundary_candidate"]["primary_technical_pack"])
        self.assertEqual("物理机械", pack["release1_pack_boundary_candidate"]["deferred_pack"])

        engineering = decisions["ED-001"]
        self.assertEqual(
            "无硬性限制，由你提出推荐方案。",
            engineering["intake_snapshot"]["technology_constraints_input"],
        )
        self.assertEqual("CANDIDATE_NOT_APPROVED", engineering["candidate_stack"]["status"])
        self.assertEqual(
            "PLANNED_NOT_EXECUTABLE_UNTIL_ENGINEERING_SKELETON_EXISTS",
            engineering["candidate_verification_commands"]["status"],
        )

    def test_decision_evidence_templates_keep_provenance_and_usage_approval(self) -> None:
        templates = sorted((ROOT / "docs" / "decision-packets" / "templates").glob("*.csv"))
        required = {
            "release1-analytical-chemistry-qc-inventory.csv",
            "release1-analytical-sample-map.csv",
            "release1-market-protocol-inventory.csv",
            "release1-method-inventory.csv",
            "release1-scenario-inventory.csv",
            "release1-stakeholder-roster.csv",
            "release1-toy-scope-eligibility.csv",
            "release1-volume-baseline.csv",
        }
        by_name = {path.name: path for path in templates}
        self.assertTrue(required.issubset(by_name))
        for name in sorted(required):
            path = by_name[name]
            header = path.read_text(encoding="utf-8").splitlines()[0].split(",")
            with self.subTest(path=path.name):
                self.assertIn("evidence_class", header)
                self.assertIn("provenance_ref", header)
                self.assertIn("usage_approval_ref", header)
                self.assertIn("review_status", header)

        method_header = by_name["release1-method-inventory.csv"].read_text(encoding="utf-8").splitlines()[0].split(",")
        self.assertIn("analyte_or_parameter_group", method_header)
        self.assertIn("lod_rule_ref", method_header)
        self.assertIn("qc_plan_ref", method_header)

        sample_header = by_name["release1-analytical-sample-map.csv"].read_text(encoding="utf-8").splitlines()[0].split(",")
        self.assertIn("accessibility_basis_ref", sample_header)
        self.assertIn("minimum_required_quantity", sample_header)
        self.assertIn("allow_pooling", sample_header)

        qc_header = by_name["release1-analytical-chemistry-qc-inventory.csv"].read_text(encoding="utf-8").splitlines()[0].split(",")
        self.assertIn("qc_failure_scope", qc_header)
        self.assertIn("parser_validation_dataset_ref", qc_header)

    def test_stories_do_not_reintroduce_client_selected_tenant_context(self) -> None:
        for path in sorted((ROOT / "spec" / "stories").glob("*.json")):
            payload = load_json(path)
            serialized = json.dumps(payload, ensure_ascii=False)
            with self.subTest(path=path.name):
                self.assertIn("OD-002@1.0.0", payload["depends_on"])
                self.assertNotIn("tenantId", serialized)
                self.assertNotIn("tenantScoped", serialized)
                contract = payload.get("body", {}).get("data_contract", {})
                client_fields = [
                    *contract.get("required", []),
                    *contract.get("input", []),
                ]
                self.assertNotIn("organizationGroupId", client_fields)

    def test_shared_saas_multi_tenant_is_only_a_rejected_direction(self) -> None:
        prd = (
            ROOT / "docs" / "AI原生第三方产品检测LIMS产品需求文档.md"
        ).read_text(encoding="utf-8")
        self.assertIn("集团多机构、每集团独立部署", prd)
        self.assertIn("EX-014", prd)
        self.assertNotIn("Tenant、LegalEntity", prd)
        self.assertNotIn("AC-SEC-001：跨租户隔离", prd)

        checked_paths = [
            ROOT / "docs" / "AI原生第三方产品检测LIMS产品需求文档.md",
            *(ROOT / "spec" / "decisions").glob("*.json"),
            *(ROOT / "spec" / "requirements").glob("*.json"),
            *(ROOT / "spec" / "acceptance").glob("*.json"),
            *(ROOT / "spec" / "stories").glob("*.json"),
            *(ROOT / "spec" / "releases").glob("*.json"),
        ]
        rejection_markers = ("禁止", "不提供", "不实现", "不包含", "不得", "排除", "拒绝", "rejected", "| EX-")
        for path in checked_paths:
            for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
                if "共享 SaaS 多租户" not in line and "共享SaaS" not in line:
                    continue
                with self.subTest(path=path.name, line=number):
                    self.assertTrue(any(marker in line for marker in rejection_markers), line)


    def test_r1_applicability_baseline_is_frozen_and_consistent(self) -> None:
        decided = load_json(ROOT / "spec" / "decisions" / "OD-001__v1.0.0.json")
        self.assertEqual("approved", decided["status"])
        self.assertEqual("decided", decided["decision_state"])
        self.assertIn("用户", decided["approval_evidence"])
        self.assertIn("物理机械", decided["decision"])
        pilot = decided["pilot_slice"]
        self.assertEqual("玩具婴童产品", pilot["industry_pack"])
        self.assertEqual("物理机械", pilot["primary_technical_pack"])
        self.assertEqual("分析化学", pilot["deferred_technical_pack"])
        self.assertEqual(["微生物/生物"], pilot["excluded_technical_capabilities"])
        self.assertIn("中国内销", pilot["target_market"])
        proposed = load_json(ROOT / "spec" / "decisions" / "OD-001__v0.1.0.json")
        self.assertEqual("proposed", proposed["status"])
        self.assertEqual("open", proposed["decision_state"])

        with (ROOT / "generated" / "spec" / "traceability.csv").open(encoding="utf-8") as handle:
            rows = {row["spec_key"]: row for row in csv.DictReader(handle)}
        textile_refs = {f"BUS-TEX-{number:03d}@1.0.0" for number in range(1, 6)}
        ai_refs = {f"BUS-AI-{number:03d}@1.0.0" for number in range(1, 4)}
        for ref in textile_refs:
            with self.subTest(textile=ref):
                self.assertEqual("enabled_pack", rows[ref]["activation_mode"])
                self.assertEqual("DISABLED", rows[ref]["applicability"])
        for ref in ai_refs:
            with self.subTest(ai=ref):
                self.assertEqual("conditional", rows[ref]["activation_mode"])
                self.assertEqual("DISABLED", rows[ref]["applicability"])
        self.assertEqual("core", rows["BUS-GOV-001@1.0.0"]["activation_mode"])
        self.assertEqual("ENABLED", rows["BUS-GOV-001@1.0.0"]["applicability"])
        for key, row in rows.items():
            if row["status"] != "approved" or row["activation_mode"] == "":
                continue
            with self.subTest(no_unknown_applicability=key):
                self.assertNotEqual("UNKNOWN", row["applicability"])
            if row["activation_mode"] == "core":
                with self.subTest(core_enabled=key):
                    self.assertEqual("ENABLED", row["applicability"])

        snapshot = load_json(ROOT / "spec" / "baselines" / "r1-applicability-baseline.lock.json")
        lock = load_json(ROOT / "generated" / "spec" / ".specgen-lock.json")
        for ref in ("OD-001@1.0.0", "BUS-GOV-001@1.0.0", "ATC-GOV-001@1.0.0"):
            with self.subTest(snapshot_ref=ref):
                self.assertIn(ref, snapshot["specs"])
                self.assertEqual(lock["specs"][ref], snapshot["specs"][ref])
        self.assertEqual(lock["config_fingerprint"], snapshot["config_fingerprint"])


if __name__ == "__main__":
    unittest.main()
