from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.specgen.engine import ProjectState, check, generate
from tools.specgen.errors import DriftError

from tests.specgen_helpers import create_generated_project


class GenerationIntegrationTests(unittest.TestCase):
    def test_generation_is_idempotent_and_check_is_read_only(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            state = create_generated_project(root)
            before = {
                path.relative_to(root).as_posix(): path.read_bytes()
                for path in (root / "generated").rglob("*")
                if path.is_file()
            }
            result = generate(state)
            self.assertEqual((), result.written)
            self.assertEqual([], check(ProjectState.load(root)))
            after = {
                path.relative_to(root).as_posix(): path.read_bytes()
                for path in (root / "generated").rglob("*")
                if path.is_file()
            }
            self.assertEqual(before, after)

    def test_modified_missing_and_extra_files_detected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            create_generated_project(root)
            catalog = root / "generated" / "spec" / "catalog.md"
            catalog.write_text("manual edit\n", encoding="utf-8")
            (root / "generated" / "spec" / "source-coverage.md").unlink()
            (root / "generated" / "spec" / "manual.txt").write_text("x", encoding="utf-8")
            errors = check(ProjectState.load(root))
            joined = "\n".join(errors)
            self.assertIn("被手改", joined)
            self.assertIn("缺少生成文件", joined)
            self.assertIn("未知文件", joined)

    def test_source_change_blocks_generation_until_reviewed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            create_generated_project(root)
            prd = root / "docs" / "prd.md"
            prd.write_text(prd.read_text(encoding="utf-8").replace("保持隔离", "严格保持隔离"), encoding="utf-8")
            state = ProjectState.load(root)
            self.assertTrue(state.source_drifts)
            with self.assertRaises(DriftError):
                generate(state)

    def test_unknown_file_prevents_generator_from_deleting_or_overwriting(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            create_generated_project(root)
            manual = root / "generated" / "spec" / "manual.txt"
            manual.write_text("human", encoding="utf-8")
            with self.assertRaises(DriftError):
                generate(ProjectState.load(root))
            self.assertEqual("human", manual.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
