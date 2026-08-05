from __future__ import annotations

import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


class RepositoryEngineeringContractTests(unittest.TestCase):
    def test_development_governance_is_not_active(self) -> None:
        self.assertFalse((ROOT / ".github" / "workflows" / "spec-governance.yml").exists())

        active_paths = (
            ROOT / "AGENTS.md",
            ROOT / ".github" / "workflows" / "application-ci.yml",
            ROOT / "scripts" / "verify.ps1",
            ROOT / "scripts" / "verify.sh",
            ROOT / "docs" / "engineering" / "development-workflow.md",
        )
        forbidden = (
            "tools.specgen",
            "source-status",
            "verify-history",
            "ready --story",
            "allowed_paths",
        )
        for path in active_paths:
            text = path.read_text(encoding="utf-8")
            with self.subTest(path=path.relative_to(ROOT)):
                for marker in forbidden:
                    self.assertNotIn(marker, text)

    def test_locked_toolchain_versions_are_explicit(self) -> None:
        global_json = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))
        package_json = json.loads((ROOT / "package.json").read_text(encoding="utf-8"))

        self.assertEqual("10.0.302", global_json["sdk"]["version"])
        self.assertEqual("disable", global_json["sdk"]["rollForward"])
        self.assertEqual("24.14.1", package_json["engines"]["node"])
        self.assertEqual("10.34.5", package_json["engines"]["pnpm"])
        self.assertEqual("pnpm@10.34.5", package_json["packageManager"])

    def test_required_engineering_surfaces_exist(self) -> None:
        required = (
            "OpenLIMS.slnx",
            "src/host/api/OpenLIMS.Api/OpenLIMS.Api.csproj",
            "src/host/worker/OpenLIMS.Worker/OpenLIMS.Worker.csproj",
            "apps/web/package.json",
            "deploy/compose/compose.yaml",
            "scripts/verify.ps1",
            "scripts/verify.sh",
            ".github/workflows/application-ci.yml",
        )
        for relative in required:
            with self.subTest(path=relative):
                self.assertTrue((ROOT / relative).is_file())

    def test_all_module_projects_are_registered_in_the_solution(self) -> None:
        solution = (ROOT / "OpenLIMS.slnx").read_text(encoding="utf-8").replace("\\", "/")
        modules_root = ROOT / "src" / "modules"
        module_projects = sorted(modules_root.glob("*/OpenLIMS.Modules.*/*.csproj"))

        self.assertGreater(len(module_projects), 0)
        for project in module_projects:
            relative = project.relative_to(ROOT).as_posix()
            with self.subTest(project=relative):
                self.assertIn(relative, solution)

    def test_runtime_modules_are_registered_in_api_and_worker(self) -> None:
        api = (ROOT / "src/host/api/OpenLIMS.Api/Program.cs").read_text(encoding="utf-8")
        worker = (ROOT / "src/host/worker/OpenLIMS.Worker/Program.cs").read_text(encoding="utf-8")
        module_names = {
            path.name.title().replace("-", "") + "Module"
            for path in (ROOT / "src" / "modules").iterdir()
            if path.is_dir()
        }

        for module_name in sorted(module_names):
            with self.subTest(module=module_name):
                self.assertIn(f"new {module_name}(", api)
                self.assertIn(f"new {module_name}(", worker)

    def test_repository_json_files_are_strictly_loadable(self) -> None:
        ignored_parts = {".git", "node_modules", "bin", "obj", "dist"}
        paths = [
            path
            for path in ROOT.rglob("*.json")
            if ignored_parts.isdisjoint(path.relative_to(ROOT).parts)
        ]

        self.assertGreater(len(paths), 0)
        for path in paths:
            with self.subTest(path=path.relative_to(ROOT)):
                json.loads(path.read_text(encoding="utf-8"))

    def test_verification_scripts_keep_engineering_quality_gates(self) -> None:
        required_markers = (
            "dotnet restore",
            "dotnet build",
            "dotnet test",
            "pnpm lint",
            "pnpm typecheck",
            "pnpm unit tests",
            "pnpm build",
            "docker compose config",
            "repository engineering checks",
        )
        for relative in ("scripts/verify.ps1", "scripts/verify.sh"):
            text = (ROOT / relative).read_text(encoding="utf-8")
            with self.subTest(path=relative):
                for marker in required_markers:
                    self.assertIn(marker, text)

    def test_application_ci_keeps_build_test_security_and_smoke_checks(self) -> None:
        workflow = (ROOT / ".github/workflows/application-ci.yml").read_text(encoding="utf-8")
        required = (
            "dotnet restore OpenLIMS.slnx --locked-mode",
            "dotnet build OpenLIMS.slnx",
            "dotnet test OpenLIMS.slnx",
            "python -m unittest tests.test_repository_contract -v",
            "pnpm audit --prod --audit-level critical",
            "docker compose",
            "Smoke test migration, readiness, and trusted identity",
        )
        for marker in required:
            self.assertIn(marker, workflow)

    def test_git_checkout_keeps_deterministic_text_and_binary_rules(self) -> None:
        attributes = (ROOT / ".gitattributes").read_text(encoding="utf-8")
        self.assertIn("* text=auto eol=lf", attributes)
        self.assertIn("*.pptx -text", attributes)
        self.assertIsNone(re.search(r"(?m)^\\s*\\*\\s+-text\\s*$", attributes))


if __name__ == "__main__":
    unittest.main()
