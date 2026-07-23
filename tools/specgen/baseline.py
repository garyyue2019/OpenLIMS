from __future__ import annotations

from typing import Any

from .models import SourceDrift, SourceScan


def empty_baseline() -> dict[str, Any]:
    return {"schema_version": 1, "documents": {}}


def compare_source_baseline(
    scans: dict[str, SourceScan], baseline: dict[str, Any]
) -> tuple[SourceDrift, ...]:
    baseline_documents = baseline.get("documents", {}) if isinstance(baseline, dict) else {}
    drifts: list[SourceDrift] = []
    all_documents = sorted(set(scans) | set(baseline_documents))
    for document_id in all_documents:
        current = scans.get(document_id)
        accepted = baseline_documents.get(document_id, {})
        accepted_items = accepted.get("items", {}) if isinstance(accepted, dict) else {}
        current_items = current.items if current else {}
        added = sorted(set(current_items) - set(accepted_items))
        removed = sorted(
            item_id
            for item_id in set(accepted_items) - set(current_items)
            if not bool(accepted_items[item_id].get("accepted_removed", False))
        )
        changed = sorted(
            item_id
            for item_id in set(current_items) & set(accepted_items)
            if current_items[item_id].fingerprint
            != str(accepted_items[item_id].get("fingerprint", ""))
        )
        document_changed = (
            current is None
            or not accepted
            or current.document_hash != str(accepted.get("document_hash", ""))
        )
        drift = SourceDrift(
            document=document_id,
            added=tuple(added),
            changed=tuple(changed),
            removed=tuple(removed),
            document_changed=document_changed,
        )
        if drift.has_drift:
            drifts.append(drift)
    return tuple(drifts)


def build_full_baseline(
    scans: dict[str, SourceScan],
    *,
    reviewer: str,
    reviewed_on: str,
    reason: str,
    acknowledgement: str,
) -> dict[str, Any]:
    documents: dict[str, Any] = {}
    for document_id, scan in sorted(scans.items()):
        documents[document_id] = {
            "path": scan.path,
            "format": scan.format,
            "document_hash": scan.document_hash,
            "acknowledgement": {
                "status": acknowledgement,
                "reviewed_by": reviewer,
                "reviewed_on": reviewed_on,
                "reason": reason,
            },
            "items": {
                item_id: {
                    "fingerprint": item.fingerprint,
                    "title": item.title,
                    "section": item.section,
                    "kind": item.kind,
                }
                for item_id, item in sorted(scan.items.items())
            },
        }
    return {"schema_version": 1, "documents": documents}


def accept_source_changes(
    scans: dict[str, SourceScan],
    baseline: dict[str, Any],
    *,
    document_id: str,
    item_ids: list[str],
    accept_all_items: bool,
    accept_document: bool,
    reviewer: str,
    reviewed_on: str,
    reason: str,
    acknowledgement: str,
) -> dict[str, Any]:
    """Return a new baseline with explicitly selected source changes acknowledged."""

    if document_id not in scans:
        raise ValueError(f"来源文档不存在：{document_id}")
    scan = scans[document_id]
    result: dict[str, Any] = {
        "schema_version": 1,
        "documents": {
            key: dict(value) for key, value in baseline.get("documents", {}).items()
        },
    }
    existing_document = dict(result["documents"].get(document_id, {}))
    existing_items = {
        key: dict(value) for key, value in existing_document.get("items", {}).items()
    }
    selected = set(scan.items) if accept_all_items else set(item_ids)
    metadata = {
        "status": acknowledgement,
        "reviewed_by": reviewer,
        "reviewed_on": reviewed_on,
        "reason": reason,
    }
    for item_id in sorted(selected):
        current = scan.items.get(item_id)
        if current is None:
            # Preserve a tombstone so historical specs retain a valid source anchor.
            previous = dict(existing_items.get(item_id, {}))
            if not previous:
                raise ValueError(f"无法确认未知的已删除来源条目：{item_id}")
            previous["accepted_removed"] = True
            previous["removal_acknowledgement"] = metadata
            existing_items[item_id] = previous
            continue
        existing_items[item_id] = {
            "fingerprint": current.fingerprint,
            "title": current.title,
            "section": current.section,
            "kind": current.kind,
            "acknowledgement": metadata,
        }
    existing_document.update(
        {
            "path": scan.path,
            "format": scan.format,
            "items": existing_items,
        }
    )
    if accept_document:
        existing_document["document_hash"] = scan.document_hash
        existing_document["acknowledgement"] = metadata
    result["documents"][document_id] = existing_document
    return result
