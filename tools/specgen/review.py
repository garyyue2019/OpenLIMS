from __future__ import annotations

import csv
import hashlib
import io
import re
from collections import Counter
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

from .errors import ConfigurationError
from .util import ID_RE, load_json, project_relative, resolve_within


REQUIRED_REVIEW_COLUMNS = (
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
    "notes",
)

ALLOWED_DECISIONS = {
    "PENDING",
    "ACCEPT",
    "ACCEPT_WITH_CONDITIONS",
    "REJECT",
    "ABSTAIN",
}

ALLOWED_RECORD_STATUSES = {"DRAFT", "VERIFIED", "SUPERSEDED"}

VERIFIED_RECORD_FIELDS = (
    "reviewer_identity_ref",
    "authority_scope",
    "authority_evidence_ref",
    "evidence_refs",
    "reviewed_at",
    "signature_or_approval_ref",
)


@dataclass(frozen=True)
class ReviewGateResult:
    change_set_id: str
    subject_ref: str
    subject_path: str
    subject_hash: str
    roster_path: str
    review_summary: dict[str, Any]
    version_lock_summary: dict[str, Any]
    blockers: tuple[dict[str, Any], ...]

    @property
    def ready(self) -> bool:
        return not self.blockers

    def to_payload(self) -> dict[str, Any]:
        return {
            "blockers": list(self.blockers),
            "change_set_id": self.change_set_id,
            "review_records": self.review_summary,
            "status": "EVIDENCE_READY" if self.ready else "BLOCKED",
            "subject": {
                "path": self.subject_path,
                "ref": self.subject_ref,
                "sha256": self.subject_hash,
            },
            "version_locks": self.version_lock_summary,
        }


def _load_roster(root: Path, change_set_id: str) -> tuple[Path, list[dict[str, str]]]:
    if not ID_RE.fullmatch(change_set_id):
        raise ConfigurationError(
            "change-set 必须使用稳定大写ID，例如 CHANGE-PLT-NEXT-VERSIONS-001"
        )
    roster_dir = resolve_within(
        root,
        "docs/decision-packets/review-records",
        label="review-records目录",
    )
    matches = sorted(roster_dir.glob(f"{change_set_id}__*.csv"))
    if not matches:
        raise ConfigurationError(f"找不到change-set评审清单：{change_set_id}")
    if len(matches) != 1:
        names = [project_relative(path, root) for path in matches]
        raise ConfigurationError(
            f"change-set必须且只能有一份活动评审清单：{change_set_id} -> {names}"
        )
    roster_path = matches[0]
    raw = roster_path.read_bytes()
    if raw.startswith(b"\xef\xbb\xbf"):
        raise ConfigurationError(f"评审CSV禁止UTF-8 BOM：{roster_path}")
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as exc:
        raise ConfigurationError(f"评审CSV不是有效UTF-8：{roster_path}: {exc}") from exc
    if "\r" in text:
        raise ConfigurationError(f"评审CSV必须使用LF换行：{roster_path}")

    reader = csv.DictReader(io.StringIO(text, newline=""))
    if reader.fieldnames is None:
        raise ConfigurationError(f"评审CSV缺少表头：{roster_path}")
    missing_columns = sorted(set(REQUIRED_REVIEW_COLUMNS) - set(reader.fieldnames))
    if missing_columns:
        raise ConfigurationError(
            f"评审CSV缺少必填列：{roster_path} -> {missing_columns}"
        )

    rows: list[dict[str, str]] = []
    for line_number, raw_row in enumerate(reader, start=2):
        if None in raw_row:
            raise ConfigurationError(
                f"评审CSV第{line_number}行包含未声明的多余列：{roster_path}"
            )
        row: dict[str, str] = {}
        for key in reader.fieldnames:
            value = raw_row.get(key)
            if value is None:
                raise ConfigurationError(
                    f"评审CSV第{line_number}行字段数量不足：{roster_path}"
                )
            row[key] = value.strip()
        rows.append(row)
    if not rows:
        raise ConfigurationError(f"评审CSV没有角色槽记录：{roster_path}")
    return roster_path, rows


def _resolve_subject(
    root: Path,
    change_set_id: str,
    rows: list[dict[str, str]],
) -> tuple[str, str, Path]:
    subject_refs = {row["subject_ref"] for row in rows}
    subject_hashes = {row["subject_hash"].lower() for row in rows}
    if subject_refs != {change_set_id}:
        raise ConfigurationError(
            f"评审清单subject_ref必须全部等于change-set ID：{sorted(subject_refs)}"
        )
    if len(subject_hashes) != 1:
        raise ConfigurationError(
            f"评审清单必须绑定唯一subject_hash：{sorted(subject_hashes)}"
        )
    subject_hash = next(iter(subject_hashes))
    if not re.fullmatch(r"[0-9a-f]{64}", subject_hash):
        raise ConfigurationError(f"subject_hash不是小写SHA-256：{subject_hash}")

    packet_root = resolve_within(
        root,
        "docs/decision-packets",
        label="decision-packets目录",
    )
    matches: list[Path] = []
    for sidecar in sorted(packet_root.glob("*.sha256")):
        try:
            sidecar_text = sidecar.read_text(encoding="ascii")
        except UnicodeDecodeError as exc:
            raise ConfigurationError(f"SHA侧车必须是ASCII：{sidecar}") from exc
        lines = [line.strip() for line in sidecar_text.splitlines() if line.strip()]
        if len(lines) != 1:
            raise ConfigurationError(f"SHA侧车必须只有一条非空记录：{sidecar}")
        parts = lines[0].split(maxsplit=1)
        if len(parts) != 2 or not re.fullmatch(r"[0-9a-f]{64}", parts[0]):
            raise ConfigurationError(f"SHA侧车格式无效：{sidecar}")
        digest, recorded_name = parts
        if digest != subject_hash:
            continue
        if Path(recorded_name).name != recorded_name:
            raise ConfigurationError(f"SHA侧车目标必须是同目录文件名：{sidecar}")
        target = resolve_within(
            root,
            Path("docs/decision-packets") / recorded_name,
            label="评审subject文件",
        )
        if not target.is_file():
            raise ConfigurationError(f"SHA侧车目标不存在：{target}")
        actual = hashlib.sha256(target.read_bytes()).hexdigest()
        if actual != digest:
            raise ConfigurationError(
                f"评审subject正文哈希与侧车不一致：{project_relative(target, root)}"
            )
        matches.append(target)
    if not matches:
        raise ConfigurationError(
            f"找不到与subject_hash匹配且正文有效的SHA侧车：{subject_hash}"
        )
    if len(matches) != 1:
        raise ConfigurationError(
            "subject_hash必须唯一对应一份变更集正文："
            + str([project_relative(path, root) for path in matches])
        )
    return change_set_id, subject_hash, matches[0]


def _validate_timestamp(value: str, *, record_id: str) -> None:
    normalized = value[:-1] + "+00:00" if value.endswith("Z") else value
    try:
        parsed = datetime.fromisoformat(normalized)
    except ValueError as exc:
        raise ConfigurationError(
            f"{record_id} reviewed_at必须是ISO-8601时间：{value}"
        ) from exc
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        raise ConfigurationError(
            f"{record_id} reviewed_at必须包含时区偏移：{value}"
        )


def _review_record_status(
    rows: list[dict[str, str]],
) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    record_ids: set[str] = set()
    slots: dict[tuple[str, str], list[dict[str, str]]] = {}
    for row in rows:
        record_id = row["review_record_id"]
        if not record_id or record_id in record_ids:
            raise ConfigurationError(f"review_record_id为空或重复：{record_id!r}")
        record_ids.add(record_id)
        if not row["review_item_id"] or not row["role_slot"]:
            raise ConfigurationError(f"{record_id}缺少review_item_id或role_slot")
        if row["decision"] not in ALLOWED_DECISIONS:
            raise ConfigurationError(
                f"{record_id} decision无效：{row['decision']!r}"
            )
        if row["record_status"] not in ALLOWED_RECORD_STATUSES:
            raise ConfigurationError(
                f"{record_id} record_status无效：{row['record_status']!r}"
            )
        if row["reviewed_at"]:
            _validate_timestamp(row["reviewed_at"], record_id=record_id)
        slots.setdefault((row["review_item_id"], row["role_slot"]), []).append(row)

    active_rows: list[dict[str, str]] = []
    for slot, slot_rows in sorted(slots.items()):
        active = [row for row in slot_rows if row["record_status"] != "SUPERSEDED"]
        if len(active) != 1:
            raise ConfigurationError(
                f"角色槽{slot[0]}/{slot[1]}必须且只能有一条活动记录，当前={len(active)}"
            )
        active_rows.append(active[0])

    blockers: list[dict[str, Any]] = []
    verified_acceptances = 0
    decision_counts = Counter(row["decision"] for row in active_rows)
    for row in sorted(active_rows, key=lambda item: item["review_record_id"]):
        record_id = row["review_record_id"]
        decision = row["decision"]
        if decision == "PENDING":
            if row["record_status"] != "DRAFT":
                raise ConfigurationError(
                    f"{record_id}为PENDING时record_status必须是DRAFT"
                )
            blockers.append(
                {
                    "category": "review_record",
                    "code": "REVIEW_PENDING",
                    "message": "责任角色尚未形成受控结论",
                    "ref": record_id,
                }
            )
            continue

        missing = [field for field in VERIFIED_RECORD_FIELDS if not row[field]]
        if row["record_status"] != "VERIFIED":
            missing.append("record_status=VERIFIED")
        if decision == "ACCEPT_WITH_CONDITIONS" and not row["conditions"]:
            missing.append("conditions")
        if decision == "REJECT" and not row["blocking_objections"]:
            missing.append("blocking_objections")
        if missing:
            blockers.append(
                {
                    "category": "review_record",
                    "code": "REVIEW_EVIDENCE_INCOMPLETE",
                    "message": "非PENDING结论缺少受控身份、授权、证据、时间、签名或状态",
                    "missing_fields": sorted(set(missing)),
                    "ref": record_id,
                }
            )

        if decision == "ACCEPT_WITH_CONDITIONS":
            blockers.append(
                {
                    "category": "review_record",
                    "code": "REVIEW_CONDITIONS_OPEN",
                    "message": "条件接受在条件闭合并形成明确ACCEPT证据前继续阻断",
                    "ref": record_id,
                }
            )
        elif decision == "REJECT":
            blockers.append(
                {
                    "category": "review_record",
                    "code": "REVIEW_REJECTED",
                    "message": "责任角色拒绝当前变更集",
                    "ref": record_id,
                }
            )
        elif decision == "ABSTAIN":
            blockers.append(
                {
                    "category": "review_record",
                    "code": "REVIEW_ABSTAINED",
                    "message": "弃权不计入必需角色批准闭包",
                    "ref": record_id,
                }
            )
        elif decision == "ACCEPT":
            if row["conditions"]:
                blockers.append(
                    {
                        "category": "review_record",
                        "code": "REVIEW_ACCEPT_HAS_CONDITIONS",
                        "message": "ACCEPT记录不得携带未解析条件",
                        "ref": record_id,
                    }
                )
            if row["blocking_objections"]:
                blockers.append(
                    {
                        "category": "review_record",
                        "code": "REVIEW_ACCEPT_HAS_OBJECTIONS",
                        "message": "ACCEPT记录不得同时保留阻塞性反对意见",
                        "ref": record_id,
                    }
                )
            record_blocked = any(
                blocker["ref"] == record_id for blocker in blockers
            )
            if not record_blocked:
                verified_acceptances += 1

    summary: dict[str, Any] = {
        "active_records": len(active_rows),
        "decision_counts": dict(sorted(decision_counts.items())),
        "required_role_slots": len(slots),
        "superseded_records": len(rows) - len(active_rows),
        "total_records": len(rows),
        "verified_acceptances": verified_acceptances,
    }
    return summary, blockers


def _binding_matches(value: str, bindings: set[str], basenames: set[str]) -> bool:
    normalized = value.replace("\\", "/").strip()
    return normalized in bindings or Path(normalized).name in basenames


def _version_lock_status(
    root: Path,
    subject_path: Path,
    roster_path: Path,
    change_set_id: str,
) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    subject_rel = project_relative(subject_path, root)
    roster_rel = project_relative(roster_path, root)
    bindings = {
        change_set_id,
        subject_rel,
        roster_rel,
        subject_path.name,
        roster_path.name,
    }
    basenames = {subject_path.name, roster_path.name}
    blockers: list[dict[str, Any]] = []
    linked_specs: list[str] = []
    seen_locks: set[tuple[str, str]] = set()
    total = 0
    verified = 0

    spec_root = resolve_within(root, "spec", label="spec目录")
    for path in sorted(spec_root.rglob("*.json")):
        item = load_json(path)
        if not isinstance(item, dict) or "id" not in item or "version" not in item:
            continue
        evidence_refs = item.get("evidence_refs", [])
        if not isinstance(evidence_refs, list):
            raise ConfigurationError(
                f"规格evidence_refs必须是数组：{project_relative(path, root)}"
            )
        linked = any(
            isinstance(value, str)
            and _binding_matches(value, bindings, basenames)
            for value in evidence_refs
        )
        locks = item.get("version_locks")
        if not linked or locks is None:
            continue
        if not isinstance(locks, list):
            raise ConfigurationError(
                f"规格version_locks必须是数组：{project_relative(path, root)}"
            )
        spec_ref = f"{item['id']}@{item['version']}"
        linked_specs.append(spec_ref)
        for lock in locks:
            if not isinstance(lock, dict):
                raise ConfigurationError(f"{spec_ref} version_locks元素必须是对象")
            lock_id = lock.get("lock_id")
            if not isinstance(lock_id, str) or not lock_id.strip():
                raise ConfigurationError(f"{spec_ref}存在空lock_id")
            lock_key = (spec_ref, lock_id)
            if lock_key in seen_locks:
                raise ConfigurationError(f"版本锁重复：{spec_ref}/{lock_id}")
            seen_locks.add(lock_key)
            total += 1

            missing: list[str] = []
            exact_value = lock.get("exact_value")
            if not isinstance(exact_value, str) or not exact_value.strip():
                missing.append("exact_value")
            if lock.get("status") != "VERIFIED":
                missing.append("status=VERIFIED")
            lock_evidence = lock.get("evidence_refs")
            if not isinstance(lock_evidence, list) or not lock_evidence or not all(
                isinstance(value, str) and value.strip() for value in lock_evidence
            ):
                missing.append("evidence_refs")
            if missing:
                blockers.append(
                    {
                        "category": "version_lock",
                        "code": "VERSION_LOCK_INCOMPLETE",
                        "message": "技术锁缺少精确值、核验状态或实际证据引用",
                        "missing_fields": sorted(missing),
                        "ref": f"{spec_ref}/{lock_id}",
                    }
                )
            else:
                verified += 1

    summary: dict[str, Any] = {
        "incomplete": total - verified,
        "linked_specs": sorted(set(linked_specs)),
        "total": total,
        "verified": verified,
    }
    return summary, blockers


def evaluate_review_gate(root: Path, change_set_id: str) -> ReviewGateResult:
    root = root.resolve()
    roster_path, rows = _load_roster(root, change_set_id)
    subject_ref, subject_hash, subject_path = _resolve_subject(
        root,
        change_set_id,
        rows,
    )
    for row in rows:
        if row["change_set_id"] != change_set_id:
            raise ConfigurationError(
                f"{row['review_record_id']} change_set_id不匹配：{row['change_set_id']}"
            )
        if row["subject_hash"].lower() != subject_hash:
            raise ConfigurationError(
                f"{row['review_record_id']} subject_hash不匹配"
            )

    review_summary, review_blockers = _review_record_status(rows)
    lock_summary, lock_blockers = _version_lock_status(
        root,
        subject_path,
        roster_path,
        change_set_id,
    )
    blockers = tuple(
        sorted(
            (*review_blockers, *lock_blockers),
            key=lambda item: (item["category"], item["ref"], item["code"]),
        )
    )
    return ReviewGateResult(
        change_set_id=change_set_id,
        subject_ref=subject_ref,
        subject_path=project_relative(subject_path, root),
        subject_hash=subject_hash,
        roster_path=project_relative(roster_path, root),
        review_summary=review_summary,
        version_lock_summary=lock_summary,
        blockers=blockers,
    )
