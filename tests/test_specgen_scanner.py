from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.specgen.scanner import scan_openlims_prd


class ScannerTests(unittest.TestCase):
    def test_extracts_table_rows_and_acceptance_blocks(self) -> None:
        text = """# PRD

## 需求

| ID | 需求 |
|---|---|
| REQ-TEST-001 | 必须保持隔离 |

## 验收

**AC-TEST-001：隔离门禁**  
给定对象未放行，当请求制样时，系统必须拒绝。
"""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "prd.md"
            path.write_text(text, encoding="utf-8", newline="\n")
            scan = scan_openlims_prd("PRD", "prd.md", path)
        self.assertEqual({"REQ-TEST-001", "AC-TEST-001"}, set(scan.items))
        self.assertEqual("table-row", scan.items["REQ-TEST-001"].kind)
        self.assertEqual("acceptance-block", scan.items["AC-TEST-001"].kind)
        self.assertIn("系统必须拒绝", scan.items["AC-TEST-001"].content)

    def test_line_movement_does_not_change_item_fingerprint(self) -> None:
        first = "# PRD\n\n| REQ-TEST-001 | 必须保持隔离 |\n"
        second = "# PRD\n\n\n\n| REQ-TEST-001 | 必须保持隔离 |\n"
        with tempfile.TemporaryDirectory() as directory:
            a = Path(directory) / "a.md"
            b = Path(directory) / "b.md"
            a.write_text(first, encoding="utf-8")
            b.write_text(second, encoding="utf-8")
            scan_a = scan_openlims_prd("PRD", "prd.md", a)
            scan_b = scan_openlims_prd("PRD", "prd.md", b)
        self.assertEqual(
            scan_a.items["REQ-TEST-001"].fingerprint,
            scan_b.items["REQ-TEST-001"].fingerprint,
        )


if __name__ == "__main__":
    unittest.main()
