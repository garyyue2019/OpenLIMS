from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.specgen.baseline import (
    accept_source_changes,
    build_full_baseline,
    compare_source_baseline,
)
from tools.specgen.scanner import scan_openlims_prd


class SourceBaselineTests(unittest.TestCase):
    def test_accepted_removal_keeps_tombstone_for_historical_references(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            original = root / "original.md"
            current = root / "current.md"
            original.write_text(
                "# PRD\n\n| REQ-OLD-001 | 原需求 |\n", encoding="utf-8"
            )
            current.write_text("# PRD\n\n已移除。\n", encoding="utf-8")
            original_scan = scan_openlims_prd("PRD", "prd.md", original)
            current_scan = scan_openlims_prd("PRD", "prd.md", current)
            baseline = build_full_baseline(
                {"PRD": original_scan},
                reviewer="test",
                reviewed_on="2026-07-23",
                reason="initial",
                acknowledgement="approved",
            )
            self.assertEqual(("REQ-OLD-001",), compare_source_baseline({"PRD": current_scan}, baseline)[0].removed)
            accepted = accept_source_changes(
                {"PRD": current_scan},
                baseline,
                document_id="PRD",
                item_ids=["REQ-OLD-001"],
                accept_all_items=False,
                accept_document=True,
                reviewer="test",
                reviewed_on="2026-07-23",
                reason="approved removal",
                acknowledgement="approved",
            )
            self.assertEqual((), compare_source_baseline({"PRD": current_scan}, accepted))
            tombstone = accepted["documents"]["PRD"]["items"]["REQ-OLD-001"]
            self.assertTrue(tombstone["accepted_removed"])
            self.assertIn("fingerprint", tombstone)


if __name__ == "__main__":
    unittest.main()
