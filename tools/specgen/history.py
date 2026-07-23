from __future__ import annotations

import os
from pathlib import Path
from typing import Any

from .engine import ProjectState, check
from .errors import ConfigurationError, DriftError, ValidationError
from .models import SpecObject
from .util import (
    dump_json,
    load_json,
    parse_semver,
    project_relative,
    resolve_within,
    semantic_hash,
)


def _seal_hash(payload: dict[str, Any]) -> str:
    return semantic_hash({key: value for key, value in payload.items() if key != "seal_hash"})


def _exclusive_write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    try:
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o644)
    except FileExistsError as exc:
        raise ConfigurationError(f"Seal 已存在，禁止覆盖：{path}") from exc
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(content)
    except Exception:
        try:
            path.unlink(missing_ok=True)
        finally:
            raise


def _release_spec(state: ProjectState, release_key: str) -> SpecObject:
    spec = state.specs.get(release_key)
    if spec is None:
        raise ConfigurationError(f"发布基线不存在：{release_key}")
    if spec.kind != "release-baseline":
        raise ConfigurationError(f"对象不是 release-baseline：{release_key}")
    return spec


def _seal_dir(state: ProjectState, release: SpecObject) -> Path:
    return resolve_within(
        state.config.root, f"spec/seals/{release.id}", label="seal 目录"
    )


def _prior_seal(state: ProjectState, release: SpecObject) -> tuple[str, dict[str, Any]] | None:
    directory = _seal_dir(state, release)
    if not directory.exists():
        return None
    candidates: list[tuple[tuple[int, int, int], Path, dict[str, Any]]] = []
    for path in directory.glob("*.seal.json"):
        data = load_json(path)
        version = str(data.get("release_version", ""))
        try:
            parsed = parse_semver(version)
        except ValueError:
            continue
        if parsed < parse_semver(release.version):
            candidates.append((parsed, path, data))
    if not candidates:
        return None
    _, path, data = max(candidates, key=lambda item: item[0])
    return project_relative(path, state.config.root), data


def build_seal(
    state: ProjectState,
    *,
    release_key: str,
    sealed_by: str,
    sealed_on: str,
    reason: str,
) -> dict[str, Any]:
    state.require_valid()
    state.require_sources_current()
    drift_errors = check(state)
    if drift_errors:
        raise DriftError("Seal 前一致性检查失败：\n- " + "\n- ".join(drift_errors))
    release = _release_spec(state, release_key)
    if release.status != "approved":
        raise ValidationError([f"发布基线 {release_key} 状态为 {release.status}，只有 approved 可封存"])
    selected = list(release.data.get("selected_specs", []))
    problems: list[str] = []
    for key in selected:
        target = state.specs.get(key)
        if target is None:
            problems.append(f"发布选择不存在：{key}")
        elif target.status != "approved":
            problems.append(f"发布选择未批准：{key} ({target.status})")
        else:
            for dependency in target.dependencies:
                if dependency not in selected:
                    problems.append(f"发布未闭包：{key} 依赖 {dependency}，但未选择")
    if problems:
        raise ValidationError(problems)

    prior = _prior_seal(state, release)
    prior_payload = None
    if prior:
        prior_payload = {"path": prior[0], "seal_hash": prior[1].get("seal_hash")}
    payload: dict[str, Any] = {
        "schema_version": 1,
        "release": release.key,
        "release_id": release.id,
        "release_version": release.version,
        "release_fingerprint": release.fingerprint,
        "sealed_by": sealed_by,
        "sealed_on": sealed_on,
        "reason": reason,
        "previous_seal": prior_payload,
        "specs": {
            key: {
                "fingerprint": state.specs[key].fingerprint,
                "behavior_fingerprint": state.specs[key].behavior_fingerprint,
                "kind": state.specs[key].kind,
                "status": state.specs[key].status,
            }
            for key in selected
        },
        "generated_lock_fingerprint": semantic_hash(state.lock),
        "output_hashes": {
            path: metadata.get("sha256")
            for path, metadata in sorted(state.lock.get("outputs", {}).items())
        },
        "source_document_hashes": {
            document_id: scan.document_hash
            for document_id, scan in sorted(state.scans.items())
        },
    }
    payload["seal_hash"] = _seal_hash(payload)
    return payload


def create_seal(
    state: ProjectState,
    *,
    release_key: str,
    sealed_by: str,
    sealed_on: str,
    reason: str,
) -> str:
    release = _release_spec(state, release_key)
    payload = build_seal(
        state,
        release_key=release_key,
        sealed_by=sealed_by,
        sealed_on=sealed_on,
        reason=reason,
    )
    relative = f"spec/seals/{release.id}/{release.version}.seal.json"
    path = resolve_within(state.config.root, relative, label="seal 输出")
    _exclusive_write(path, dump_json(payload))
    return relative


def verify_history(state: ProjectState) -> list[str]:
    errors: list[str] = []
    seal_root = resolve_within(state.config.root, "spec/seals", label="seal 根目录")
    if not seal_root.exists():
        return errors
    by_path: dict[str, dict[str, Any]] = {}
    for path in sorted(seal_root.rglob("*.seal.json")):
        relative = project_relative(path, state.config.root)
        try:
            data = load_json(path)
        except ConfigurationError as exc:
            errors.append(str(exc))
            continue
        by_path[relative] = data
        expected_hash = _seal_hash(data)
        if data.get("seal_hash") != expected_hash:
            errors.append(f"Seal 哈希无效：{relative}")
        release_key = str(data.get("release", ""))
        release = state.specs.get(release_key)
        if release is None:
            errors.append(f"Seal 引用的发布规格已缺失：{relative} -> {release_key}")
        elif release.fingerprint != data.get("release_fingerprint"):
            errors.append(f"已封存发布规格被修改：{release_key}")
        for key, metadata in data.get("specs", {}).items():
            current = state.specs.get(key)
            if current is None:
                errors.append(f"已封存规格被删除：{key}（{relative}）")
            elif current.fingerprint != metadata.get("fingerprint"):
                errors.append(f"已封存规格被原地修改：{key}（{relative}）")

    for relative, data in by_path.items():
        previous = data.get("previous_seal")
        if not previous:
            continue
        previous_path = str(previous.get("path", ""))
        prior_data = by_path.get(previous_path)
        if prior_data is None:
            errors.append(f"Seal 链前驱缺失：{relative} -> {previous_path}")
        elif prior_data.get("seal_hash") != previous.get("seal_hash"):
            errors.append(f"Seal 链前驱哈希不匹配：{relative}")
    return errors


def gate_against_seal(
    state: ProjectState, *, from_seal: Path, release_key: str
) -> list[str]:
    state.require_valid()
    old = load_json(from_seal)
    release = _release_spec(state, release_key)
    selected = set(release.data.get("selected_specs", []))
    approvals = set(release.data.get("breaking_change_approvals", []))
    errors: list[str] = []
    old_specs: dict[str, Any] = old.get("specs", {})

    for key, metadata in old_specs.items():
        if key in state.specs and state.specs[key].fingerprint != metadata.get("fingerprint"):
            errors.append(f"历史篡改：同版本 {key} 的完整哈希已变化")

    old_by_id: dict[str, tuple[str, dict[str, Any]]] = {}
    for key, metadata in old_specs.items():
        logical, _, _version = key.partition("@")
        old_by_id[logical] = (key, metadata)
    new_by_id: dict[str, SpecObject] = {
        state.specs[key].id: state.specs[key]
        for key in selected
        if key in state.specs
    }

    for logical_id, (old_key, old_metadata) in sorted(old_by_id.items()):
        current = new_by_id.get(logical_id)
        if current is None:
            if logical_id not in approvals and old_key not in approvals:
                errors.append(
                    f"Breaking：发布移除 {old_key}，但 breaking_change_approvals 未批准"
                )
            continue
        if current.key == old_key:
            continue
        old_version = old_key.partition("@")[2]
        try:
            old_semver = parse_semver(old_version)
            new_semver = parse_semver(current.version)
        except ValueError:
            errors.append(f"版本格式无效：{old_key} -> {current.key}")
            continue
        if new_semver <= old_semver:
            errors.append(f"版本未递增：{old_key} -> {current.key}")
            continue
        behavior_changed = (
            current.behavior_fingerprint != old_metadata.get("behavior_fingerprint")
        )
        if new_semver[0] == old_semver[0] and new_semver[1] == old_semver[1] and behavior_changed:
            errors.append(f"PATCH 版本改变行为：{old_key} -> {current.key}")
        if current.data.get("change_class") == "major" and behavior_changed:
            if logical_id not in approvals and current.key not in approvals:
                errors.append(
                    f"Breaking：{old_key} -> {current.key} 缺少发布级批准"
                )
    return errors
