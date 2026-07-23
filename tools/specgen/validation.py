from __future__ import annotations

from collections import defaultdict
from pathlib import Path
from typing import Any

from .graph import dependency_cycles
from .models import ProjectConfig, SourceScan, SpecObject, ValidationResult
from .util import ID_RE, SEMVER_RE


ALLOWED_KINDS = {
    "requirement",
    "acceptance",
    "rule",
    "nfr",
    "decision",
    "story",
    "release-baseline",
    "lifecycle-event",
}
ALLOWED_STATUSES = {"proposed", "in_review", "approved", "deprecated", "retired"}
ALLOWED_CHANGE_CLASSES = {"patch", "minor", "major"}
ALLOWED_PRIORITIES = {"Must", "Should", "Could", "WontNow"}
ALLOWED_ACTIVATION_MODES = {
    "core",
    "enabled_pack",
    "conditional",
    "business_ops",
    "release",
}
ALLOWED_APPLICABILITY = {"ENABLED", "DISABLED", "UNKNOWN"}
ALLOWED_DECISION_STATES = {"open", "decided", "deferred", "rejected"}
ALLOWED_READINESS = {"blocked", "draft", "ready", "in_progress", "done"}

BASE_REQUIRED = {
    "schema_version",
    "kind",
    "id",
    "version",
    "status",
    "title",
    "summary",
    "owners",
    "source_refs",
    "depends_on",
    "affects",
    "change_class",
}

STORY_BODY_REQUIRED = {
    "readiness",
    "business_outcome",
    "actor",
    "preconditions",
    "trigger",
    "happy_path",
    "failure_paths",
    "invariants",
    "data_contract",
    "api_contract",
    "state_transitions",
    "permissions",
    "audit",
    "ui_states",
    "observability",
    "test_cases",
    "non_goals",
    "allowed_paths",
    "verification_commands",
    "definition_of_done",
}


def _is_string_list(value: Any) -> bool:
    return isinstance(value, list) and all(isinstance(item, str) and item for item in value)


def _validate_base(spec: SpecObject, result: ValidationResult) -> None:
    data = spec.data
    missing = sorted(BASE_REQUIRED - data.keys())
    if missing:
        result.errors.append(f"{spec.relative_path} 缺少字段：{', '.join(missing)}")
        return
    if data.get("schema_version") != 1:
        result.errors.append(f"{spec.relative_path} 仅支持 schema_version=1")
    if not ID_RE.fullmatch(spec.id):
        result.errors.append(f"{spec.relative_path} 的 id 格式无效：{spec.id}")
    if not SEMVER_RE.fullmatch(spec.version):
        result.errors.append(f"{spec.relative_path} 的 version 必须是 SemVer：{spec.version}")
    if spec.kind not in ALLOWED_KINDS:
        result.errors.append(f"{spec.relative_path} 的 kind 无效：{spec.kind}")
    if spec.status not in ALLOWED_STATUSES:
        result.errors.append(f"{spec.relative_path} 的 status 无效：{spec.status}")
    if data.get("change_class") not in ALLOWED_CHANGE_CLASSES:
        result.errors.append(
            f"{spec.relative_path} 的 change_class 必须是 patch/minor/major"
        )
    for field in ("owners", "depends_on", "affects"):
        if not _is_string_list(data.get(field)):
            result.errors.append(f"{spec.relative_path} 的 {field} 必须是非空字符串数组")
    if not isinstance(data.get("source_refs"), list):
        result.errors.append(f"{spec.relative_path} 的 source_refs 必须是数组")
    expected_name = f"{spec.id}__v{spec.version}.json"
    if spec.path.name != expected_name:
        result.errors.append(
            f"{spec.relative_path} 文件名必须是一版本一文件格式：{expected_name}"
        )


def _validate_requirement(spec: SpecObject, result: ValidationResult) -> None:
    data = spec.data
    if data.get("priority") not in ALLOWED_PRIORITIES:
        result.errors.append(f"{spec.relative_path} 缺少有效 priority")
    activation = data.get("activation")
    if not isinstance(activation, dict):
        result.errors.append(f"{spec.relative_path} 缺少 activation 对象")
        return
    if activation.get("mode") not in ALLOWED_ACTIVATION_MODES:
        result.errors.append(f"{spec.relative_path} activation.mode 无效")
    if activation.get("applicability") not in ALLOWED_APPLICABILITY:
        result.errors.append(f"{spec.relative_path} activation.applicability 无效")
    if (
        spec.status == "approved"
        and activation.get("applicability") == "UNKNOWN"
        and activation.get("mode") == "core"
    ):
        result.errors.append(
            f"{spec.relative_path} 已批准 Core 需求的适用性不能为 UNKNOWN"
        )


def _validate_acceptance(spec: SpecObject, result: ValidationResult) -> None:
    scenario = spec.data.get("scenario")
    if not isinstance(scenario, dict):
        result.errors.append(f"{spec.relative_path} 缺少 scenario 对象")
        return
    for field in ("given", "when", "then"):
        if not _is_string_list(scenario.get(field)):
            result.errors.append(f"{spec.relative_path} scenario.{field} 必须是字符串数组")


def _validate_decision(spec: SpecObject, result: ValidationResult) -> None:
    state = spec.data.get("decision_state")
    if state not in ALLOWED_DECISION_STATES:
        result.errors.append(f"{spec.relative_path} decision_state 无效")
    if state == "decided" and spec.status != "approved":
        result.errors.append(
            f"{spec.relative_path} decision_state=decided 时 status 必须为 approved"
        )
    if state != "decided" and spec.status == "approved":
        result.errors.append(
            f"{spec.relative_path} 未决定事项不得标记为 approved"
        )


def _validate_story(spec: SpecObject, result: ValidationResult) -> None:
    for field in ("target_release", "epic_id", "feature_id"):
        if not isinstance(spec.data.get(field), str) or not spec.data.get(field):
            result.errors.append(f"{spec.relative_path} 缺少 {field}")
    if not spec.id.startswith("ATC-"):
        result.errors.append(
            f"{spec.relative_path} Story 稳定 ID 应使用 ATC-*，不得把 Release 编入 ID"
        )
    body = spec.data.get("body")
    if not isinstance(body, dict):
        result.errors.append(f"{spec.relative_path} 缺少 body 对象")
        return
    missing = sorted(STORY_BODY_REQUIRED - body.keys())
    if missing:
        result.errors.append(f"{spec.relative_path} body 缺少字段：{', '.join(missing)}")
        return
    if body.get("readiness") not in ALLOWED_READINESS:
        result.errors.append(f"{spec.relative_path} body.readiness 无效")
    for field in STORY_BODY_REQUIRED - {
        "readiness",
        "business_outcome",
        "actor",
        "trigger",
        "data_contract",
        "api_contract",
    }:
        if not isinstance(body.get(field), list):
            result.errors.append(f"{spec.relative_path} body.{field} 必须是数组")
    if not isinstance(body.get("data_contract"), dict):
        result.errors.append(f"{spec.relative_path} body.data_contract 必须是对象")
    if not isinstance(body.get("api_contract"), dict):
        result.errors.append(f"{spec.relative_path} body.api_contract 必须是对象")
    for index, case in enumerate(body.get("test_cases", []), start=1):
        if not isinstance(case, dict):
            result.errors.append(f"{spec.relative_path} test_cases[{index}] 必须是对象")
            continue
        missing_case = [key for key in ("id", "type", "given", "when", "then") if key not in case]
        if missing_case:
            result.errors.append(
                f"{spec.relative_path} test_cases[{index}] 缺少：{', '.join(missing_case)}"
            )


def _validate_release_baseline(spec: SpecObject, result: ValidationResult) -> None:
    selected = spec.data.get("selected_specs")
    if not _is_string_list(selected):
        result.errors.append(f"{spec.relative_path} selected_specs 必须是版本固定数组")
    if spec.data.get("runtime_resolution") != "pinned_only":
        result.errors.append(
            f"{spec.relative_path} runtime_resolution 必须为 pinned_only"
        )


def validate_project(
    config: ProjectConfig,
    specs: dict[str, SpecObject],
    scans: dict[str, SourceScan],
    baseline: dict[str, Any] | None = None,
    load_errors: list[str] | None = None,
) -> ValidationResult:
    result = ValidationResult(errors=list(load_errors or []))
    logical_versions: dict[str, list[SpecObject]] = defaultdict(list)

    for key in sorted(specs):
        spec = specs[key]
        _validate_base(spec, result)
        logical_versions[spec.id].append(spec)
        if spec.kind in {"requirement", "nfr", "rule"}:
            _validate_requirement(spec, result)
        if spec.kind == "acceptance":
            _validate_acceptance(spec, result)
        if spec.kind == "decision":
            _validate_decision(spec, result)
        if spec.kind == "story":
            _validate_story(spec, result)
        if spec.kind == "release-baseline":
            _validate_release_baseline(spec, result)

        for dependency in spec.dependencies:
            if "@" not in dependency:
                result.errors.append(
                    f"{spec.relative_path} 依赖必须固定版本（ID@x.y.z）：{dependency}"
                )
            elif dependency not in specs:
                result.errors.append(f"{spec.relative_path} 引用了不存在的依赖：{dependency}")

        if spec.kind == "release-baseline":
            for selected in spec.data.get("selected_specs", []):
                if "@" not in selected:
                    result.errors.append(
                        f"{spec.relative_path} selected_specs 必须固定版本：{selected}"
                    )
                elif selected not in specs:
                    result.errors.append(
                        f"{spec.relative_path} selected_specs 引用不存在：{selected}"
                    )
        if spec.kind == "story":
            target_release = str(spec.data.get("target_release", ""))
            target = specs.get(target_release)
            if target is None:
                result.errors.append(
                    f"{spec.relative_path} target_release 不存在：{target_release}"
                )
            elif target.kind != "release-baseline":
                result.errors.append(
                    f"{spec.relative_path} target_release 不是 release-baseline：{target_release}"
                )

        for index, reference in enumerate(spec.source_refs, start=1):
            if not isinstance(reference, dict):
                result.errors.append(
                    f"{spec.relative_path} source_refs[{index}] 必须是对象"
                )
                continue
            document = str(reference.get("document", ""))
            item = str(reference.get("item", ""))
            if document not in scans:
                result.errors.append(
                    f"{spec.relative_path} source_refs[{index}] 来源文档不存在：{document}"
                )
            elif item not in scans[document].items:
                baseline_item = (
                    (baseline or {})
                    .get("documents", {})
                    .get(document, {})
                    .get("items", {})
                    .get(item)
                )
                if not baseline_item:
                    result.errors.append(
                        f"{spec.relative_path} source_refs[{index}] 来源条目不存在：{document}#{item}"
                    )
                elif not baseline_item.get("accepted_removed", False):
                    result.warnings.append(
                        f"{spec.relative_path} 来源条目当前已删除但尚未确认移除：{document}#{item}"
                    )

    for cycle in dependency_cycles(specs):
        result.errors.append("规格依赖存在循环：" + " -> ".join(cycle))

    for spec in specs.values():
        if spec.status != "approved":
            continue
        for dependency in spec.dependencies:
            target = specs.get(dependency)
            if target and target.status not in {"approved", "deprecated", "retired"}:
                result.errors.append(
                    f"{spec.relative_path} 已批准，但依赖 {dependency} 状态为 {target.status}"
                )

    for logical_id, versions in sorted(logical_versions.items()):
        approved = [spec for spec in versions if spec.status == "approved"]
        if len(approved) > 1:
            result.warnings.append(
                f"{logical_id} 同时存在多个 approved 版本；发布基线必须显式选择且不得运行时解析"
            )

    return result


def validate_generated_paths(config: ProjectConfig, outputs: dict[str, str]) -> list[str]:
    errors: list[str] = []
    root = Path(config.generated_root)
    lock = Path(config.lock_path)
    for path in outputs:
        candidate = Path(path)
        if candidate.is_absolute() or ".." in candidate.parts:
            errors.append(f"生成路径不安全：{path}")
        if root not in (candidate, *candidate.parents):
            errors.append(f"生成路径不在 generated_root 下：{path}")
        if candidate == lock:
            errors.append("渲染输出不得包含 lock_path，锁文件由引擎单独维护")
    return errors
