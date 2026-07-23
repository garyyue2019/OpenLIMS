from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.specgen.engine import ProjectState, generate
from tools.specgen.errors import ConfigurationError
from tools.specgen.history import create_seal, gate_against_seal, verify_history

from tests.specgen_helpers import base_requirement, create_generated_project, release, write_json


class HistoryTests(unittest.TestCase):
    def test_seal_is_exclusive_and_detects_same_version_tampering(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            state = create_generated_project(root)
            relative = create_seal(
                state,
                release_key="REL-TEST@1.0.0",
                sealed_by="unit-test",
                sealed_on="2026-07-23",
                reason="history test",
            )
            self.assertTrue((root / relative).is_file())
            with self.assertRaises(ConfigurationError):
                create_seal(
                    ProjectState.load(root),
                    release_key="REL-TEST@1.0.0",
                    sealed_by="unit-test",
                    sealed_on="2026-07-23",
                    reason="must not overwrite",
                )
            requirement_path = root / "spec" / "requirements" / "REQ-ONE-001__v1.0.0.json"
            changed = base_requirement(summary="同版本被静默修改")
            write_json(requirement_path, changed)
            errors = verify_history(ProjectState.load(root))
            self.assertTrue(any("原地修改" in error for error in errors))

    def test_patch_behavior_change_is_blocked(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            state = create_generated_project(root)
            seal_relative = create_seal(
                state,
                release_key="REL-TEST@1.0.0",
                sealed_by="unit-test",
                sealed_on="2026-07-23",
                reason="breaking gate test",
            )
            write_json(
                root / "spec" / "requirements" / "REQ-ONE-001__v1.0.1.json",
                base_requirement(version="1.0.1", summary="PATCH 却改变了行为语义"),
            )
            write_json(
                root / "spec" / "releases" / "REL-TEST__v1.1.0.json",
                release("1.1.0", ["REQ-ONE-001@1.0.1"]),
            )
            candidate = ProjectState.load(root)
            errors = gate_against_seal(
                candidate,
                from_seal=root / seal_relative,
                release_key="REL-TEST@1.1.0",
            )
            self.assertTrue(any("PATCH 版本改变行为" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
