from __future__ import annotations

import json
import re
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

    def test_expected_bootstrap_artifacts_exist(self) -> None:
        tasks = sorted((ROOT / "generated" / "spec" / "tasks").glob("*.md"))
        features = sorted((ROOT / "generated" / "spec" / "features").glob("*.feature"))
        self.assertEqual(6, len(tasks))
        self.assertEqual(10, len(features))
        self.assertFalse(any(path.name.startswith("R1-REC-") for path in (*tasks, *features)))
        self.assertTrue(all(path.name.startswith("ATC-REC-") for path in tasks))

    def test_generated_text_is_utf8_lf_and_has_generated_marker(self) -> None:
        for path in (ROOT / "generated" / "spec").rglob("*"):
            if not path.is_file():
                continue
            raw = path.read_bytes()
            self.assertNotIn(b"\r\n", raw, str(path))
            text = raw.decode("utf-8")
            if path.name != ".specgen-lock.json" and path.suffix != ".csv":
                self.assertIn("generated", text.lower(), str(path))

    def test_local_markdown_links_in_ai_manual_exist(self) -> None:
        pattern = re.compile(r"\[[^\]]+\]\(([^)]+)\)")
        errors: list[str] = []
        for document in (ROOT / "docs" / "ai-development").rglob("*.md"):
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


if __name__ == "__main__":
    unittest.main()
