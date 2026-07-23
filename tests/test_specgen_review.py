from __future__ import annotations

import csv
import hashlib
import io
import tempfile
import unittest
from pathlib import Path

from tools.specgen.errors import ConfigurationError
from tools.specgen.review import REQUIRED_REVIEW_COLUMNS, evaluate_review_gate
from tools.specgen.util import dump_json


CHANGE_SET_ID = "CHANGE-TEST-001"


class ReviewGateTests(unittest.TestCase):
    def _write_gate(
        self,
        root: Path,
        *,
        decision: str = "PENDING",
        record_status: str = "DRAFT",
        complete_record_evidence: bool = False,
        conditions: str = "",
        blocking_objections: str = "",
        reviewed_at: str = "2026-07-23T10:00:00+08:00",
        lock_verified: bool = False,
        duplicate_active_slot: bool = False,
    ) -> dict[str, Path]:
        packet_dir = root / "docs" / "decision-packets"
        roster_dir = packet_dir / "review-records"
        roster_dir.mkdir(parents=True)
        subject = packet_dir / "subject.md"
        subject.write_text("# Test change set\n", encoding="utf-8", newline="\n")
        digest = hashlib.sha256(subject.read_bytes()).hexdigest()
        sidecar = packet_dir / "subject.sha256"
        sidecar.write_text(
            f"{digest}  {subject.name}\n",
            encoding="ascii",
            newline="\n",
        )

        row = {column: "" for column in REQUIRED_REVIEW_COLUMNS}
        row.update(
            {
                "change_set_id": CHANGE_SET_ID,
                "decision": decision,
                "evidence_refs": subject.name,
                "record_status": record_status,
                "review_item_id": "RV-TEST-001",
                "review_record_id": "RR-TEST-001-OWNER",
                "role_slot": "TEST_OWNER",
                "subject_hash": digest,
                "subject_ref": CHANGE_SET_ID,
                "conditions": conditions,
                "blocking_objections": blocking_objections,
            }
        )
        if complete_record_evidence:
            row.update(
                {
                    "authority_evidence_ref": "authority://test-owner",
                    "authority_scope": "RV-TEST-001",
                    "evidence_refs": f"{subject.name};evidence://review/test-owner",
                    "reviewed_at": reviewed_at,
                    "reviewer_identity_ref": "identity://test-owner",
                    "signature_or_approval_ref": "signature://test-owner",
                }
            )

        rows = [row]
        if duplicate_active_slot:
            duplicate = dict(row)
            duplicate["review_record_id"] = "RR-TEST-001-OWNER-SECOND"
            rows.append(duplicate)

        buffer = io.StringIO(newline="")
        writer = csv.DictWriter(
            buffer,
            fieldnames=list(REQUIRED_REVIEW_COLUMNS),
            lineterminator="\n",
        )
        writer.writeheader()
        writer.writerows(rows)
        roster = roster_dir / f"{CHANGE_SET_ID}__draft.csv"
        roster.write_text(buffer.getvalue(), encoding="utf-8", newline="\n")

        lock_spec = root / "spec" / "decisions" / "ED-TEST-001__v1.0.0.json"
        lock_spec.parent.mkdir(parents=True)
        lock_spec.write_text(
            dump_json(
                {
                    "id": "ED-TEST-001",
                    "version": "1.0.0",
                    "evidence_refs": [
                        "docs/decision-packets/subject.md",
                        f"docs/decision-packets/review-records/{roster.name}",
                    ],
                    "version_locks": [
                        {
                            "lock_id": "PIN-TEST",
                            "exact_value": "1.2.3" if lock_verified else None,
                            "required_evidence": "exact test version",
                            "status": "VERIFIED" if lock_verified else "PENDING_VERIFICATION",
                            **(
                                {"evidence_refs": ["evidence://lock/test"]}
                                if lock_verified
                                else {}
                            ),
                        }
                    ],
                }
            ),
            encoding="utf-8",
            newline="\n",
        )
        return {
            "lock_spec": lock_spec,
            "roster": roster,
            "sidecar": sidecar,
            "subject": subject,
        }

    def test_pending_records_and_unverified_locks_block_without_writes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            paths = self._write_gate(root)
            before = {name: path.read_bytes() for name, path in paths.items()}

            result = evaluate_review_gate(root, CHANGE_SET_ID)

            self.assertFalse(result.ready)
            self.assertEqual(1, result.review_summary["required_role_slots"])
            self.assertEqual(0, result.review_summary["verified_acceptances"])
            self.assertEqual(1, result.version_lock_summary["total"])
            self.assertEqual(0, result.version_lock_summary["verified"])
            self.assertEqual(
                {"REVIEW_PENDING", "VERSION_LOCK_INCOMPLETE"},
                {blocker["code"] for blocker in result.blockers},
            )
            after = {name: path.read_bytes() for name, path in paths.items()}
            self.assertEqual(before, after)

    def test_verified_accept_and_verified_lock_make_evidence_ready(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._write_gate(
                root,
                decision="ACCEPT",
                record_status="VERIFIED",
                complete_record_evidence=True,
                lock_verified=True,
            )

            result = evaluate_review_gate(root, CHANGE_SET_ID)

            self.assertTrue(result.ready)
            self.assertEqual(1, result.review_summary["verified_acceptances"])
            self.assertEqual(1, result.version_lock_summary["verified"])
            self.assertEqual((), result.blockers)

    def test_accept_with_conditions_remains_blocked(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._write_gate(
                root,
                decision="ACCEPT_WITH_CONDITIONS",
                record_status="VERIFIED",
                complete_record_evidence=True,
                conditions="补齐兼容性证据",
                lock_verified=True,
            )

            result = evaluate_review_gate(root, CHANGE_SET_ID)

            self.assertFalse(result.ready)
            self.assertIn(
                "REVIEW_CONDITIONS_OPEN",
                {blocker["code"] for blocker in result.blockers},
            )

    def test_reject_and_abstain_never_close_required_role_slot(self) -> None:
        for decision, expected_code in (
            ("REJECT", "REVIEW_REJECTED"),
            ("ABSTAIN", "REVIEW_ABSTAINED"),
        ):
            with self.subTest(decision=decision), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                self._write_gate(
                    root,
                    decision=decision,
                    record_status="VERIFIED",
                    complete_record_evidence=True,
                    blocking_objections=("拒绝当前方案" if decision == "REJECT" else ""),
                    lock_verified=True,
                )

                result = evaluate_review_gate(root, CHANGE_SET_ID)

                self.assertFalse(result.ready)
                self.assertIn(
                    expected_code,
                    {blocker["code"] for blocker in result.blockers},
                )

    def test_accept_missing_identity_and_signature_is_blocked(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._write_gate(
                root,
                decision="ACCEPT",
                record_status="VERIFIED",
                lock_verified=True,
            )

            result = evaluate_review_gate(root, CHANGE_SET_ID)

            blocker = next(
                item
                for item in result.blockers
                if item["code"] == "REVIEW_EVIDENCE_INCOMPLETE"
            )
            self.assertIn("reviewer_identity_ref", blocker["missing_fields"])
            self.assertIn("signature_or_approval_ref", blocker["missing_fields"])

    def test_subject_tamper_is_invalid_instead_of_merely_blocked(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            paths = self._write_gate(root)
            paths["subject"].write_text(
                "# Tampered change set\n",
                encoding="utf-8",
                newline="\n",
            )

            with self.assertRaisesRegex(ConfigurationError, "哈希"):
                evaluate_review_gate(root, CHANGE_SET_ID)

    def test_reviewed_at_without_timezone_is_invalid(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._write_gate(
                root,
                decision="ACCEPT",
                record_status="VERIFIED",
                complete_record_evidence=True,
                reviewed_at="2026-07-23T10:00:00",
                lock_verified=True,
            )

            with self.assertRaisesRegex(ConfigurationError, "时区"):
                evaluate_review_gate(root, CHANGE_SET_ID)

    def test_duplicate_active_role_slot_is_invalid(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._write_gate(root, duplicate_active_slot=True)

            with self.assertRaisesRegex(ConfigurationError, "只能有一条活动记录"):
                evaluate_review_gate(root, CHANGE_SET_ID)


if __name__ == "__main__":
    unittest.main()
