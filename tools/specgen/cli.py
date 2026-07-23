from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

from .baseline import accept_source_changes, build_full_baseline
from .engine import ProjectState, check, generate
from .errors import ConfigurationError, DriftError, SpecgenError, ValidationError
from .graph import reverse_dependencies, source_ref_index
from .history import create_seal, gate_against_seal, verify_history
from .loader import DEFAULT_CONFIG, find_project_root
from .util import atomic_write_text, dump_json, resolve_within


EXIT_INVALID = 2
EXIT_DRIFT = 3
EXIT_BLOCKED = 4


def _configure_utf8_stdio() -> None:
    """Make deterministic CLI output independent of the Windows system locale."""
    for stream_name in ("stdout", "stderr"):
        stream = getattr(sys, stream_name, None)
        reconfigure = getattr(stream, "reconfigure", None)
        if callable(reconfigure):
            reconfigure(encoding="utf-8", errors="backslashreplace")


def _root_from_args(args: argparse.Namespace) -> Path:
    if args.root:
        return Path(args.root).resolve()
    return find_project_root(Path.cwd(), args.config)


def _load(args: argparse.Namespace) -> ProjectState:
    return ProjectState.load(_root_from_args(args), args.config)


def _print_validation(state: ProjectState) -> int:
    for warning in state.validation.warnings:
        print(f"WARN: {warning}")
    if state.validation.errors:
        for error in state.validation.errors:
            print(f"ERROR: {error}")
        return EXIT_INVALID
    print(
        f"VALID: {len(state.specs)} 个规格版本，"
        f"{sum(len(scan.items) for scan in state.scans.values())} 个 PRD 来源条目"
    )
    return 0


def command_validate(args: argparse.Namespace) -> int:
    state = _load(args)
    code = _print_validation(state)
    if code == 0 and args.strict_warnings and state.validation.warnings:
        return EXIT_INVALID
    return code


def _source_drift_payload(state: ProjectState) -> list[dict[str, Any]]:
    return [
        {
            "document": drift.document,
            "added": list(drift.added),
            "changed": list(drift.changed),
            "removed": list(drift.removed),
            "document_changed": drift.document_changed,
        }
        for drift in state.source_drifts
    ]


def command_source_status(args: argparse.Namespace) -> int:
    state = _load(args)
    payload = _source_drift_payload(state)
    if args.json:
        print(json.dumps(payload, ensure_ascii=False, sort_keys=True, indent=2))
    elif not payload:
        print("SOURCE CURRENT: PRD 与已确认来源基线一致")
    else:
        print("SOURCE DRIFT:")
        for item in payload:
            print(
                f"- {item['document']}: added={item['added']} changed={item['changed']} "
                f"removed={item['removed']} document_changed={item['document_changed']}"
            )
    return EXIT_DRIFT if payload else 0


def _validate_review_metadata(args: argparse.Namespace) -> None:
    if not args.reviewer.strip() or not args.reason.strip():
        raise ConfigurationError("source-accept 必须提供非空 reviewer 和 reason")
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", args.reviewed_on):
        raise ConfigurationError("reviewed-on 必须使用 YYYY-MM-DD")


def command_source_accept(args: argparse.Namespace) -> int:
    state = _load(args)
    state.require_valid()
    _validate_review_metadata(args)
    baseline_path = resolve_within(
        state.config.root,
        state.config.source_baseline_path,
        label="source_baseline_path",
    )
    if args.bootstrap:
        if state.baseline.get("documents") and not args.force:
            raise ConfigurationError("来源基线已存在；bootstrap 需要 --force 才能重建")
        new_baseline = build_full_baseline(
            state.scans,
            reviewer=args.reviewer,
            reviewed_on=args.reviewed_on,
            reason=args.reason,
            acknowledgement=args.acknowledgement,
        )
    else:
        if not args.document:
            raise ConfigurationError("非 bootstrap 模式必须指定 --document")
        if not args.item and not args.all_items and not args.accept_document:
            raise ConfigurationError(
                "至少指定 --item、--all-items 或 --accept-document 之一"
            )
        if not args.waive_spec_change:
            impact = state.impact
            selected = set(args.item or [])
            changed_source_items = {
                item
                for drift in impact.source_drifts
                if drift.document == args.document
                for item in (*drift.changed, *drift.removed)
                if args.all_items or item in selected
            }
            changed_specs = set(impact.added_specs) | set(impact.changed_specs)
            ref_index = source_ref_index(state.specs)
            unresolved = []
            for item in sorted(changed_source_items):
                linked = ref_index.get((args.document, item), set())
                if linked and not (linked & changed_specs):
                    unresolved.append(f"{item} -> {sorted(linked)}")
            if unresolved:
                raise ConfigurationError(
                    "以下来源语义已变化，但其直接关联结构化规格尚无版本/内容变化："
                    + "; ".join(unresolved)
                    + "。先更新关联规格，或经批准使用 --waive-spec-change"
                )
        new_baseline = accept_source_changes(
            state.scans,
            state.baseline,
            document_id=args.document,
            item_ids=args.item or [],
            accept_all_items=args.all_items,
            accept_document=args.accept_document,
            reviewer=args.reviewer,
            reviewed_on=args.reviewed_on,
            reason=args.reason,
            acknowledgement=args.acknowledgement,
        )
    changed = atomic_write_text(baseline_path, dump_json(new_baseline))
    print(
        f"SOURCE BASELINE {'UPDATED' if changed else 'UNCHANGED'}: "
        f"{state.config.source_baseline_path}"
    )
    print("注意：确认来源只表示完成语义审阅，不等于业务或生产发布批准。")
    return 0


def _impact_payload(state: ProjectState) -> dict[str, Any]:
    report = state.impact
    return {
        "added_specs": list(report.added_specs),
        "changed_specs": list(report.changed_specs),
        "removed_specs": list(report.removed_specs),
        "source_drifts": _source_drift_payload(state),
        "directly_impacted": list(report.directly_impacted),
        "transitively_impacted": list(report.transitively_impacted),
        "major_changes": list(report.major_changes),
    }


def command_impact(args: argparse.Namespace) -> int:
    state = _load(args)
    state.require_valid()
    payload = _impact_payload(state)
    if args.json:
        print(json.dumps(payload, ensure_ascii=False, sort_keys=True, indent=2))
    else:
        for key, value in payload.items():
            print(f"{key}: {value}")
    if args.fail_on_major and payload["major_changes"]:
        return EXIT_BLOCKED
    return 0


def command_generate(args: argparse.Namespace) -> int:
    state = _load(args)
    result = generate(state, allow_source_drift=args.allow_source_drift)
    print(
        f"GENERATED: written={len(result.written)} unchanged={len(result.unchanged)} "
        f"removed={len(result.removed)}"
    )
    for path in result.written:
        print(f"WRITE {path}")
    for path in result.removed:
        print(f"REMOVE {path}")
    return 0


def command_check(args: argparse.Namespace) -> int:
    state = _load(args)
    errors = check(state)
    if errors:
        print("CHECK FAILED:")
        for error in errors:
            print(f"- {error}")
        return EXIT_DRIFT
    print("CHECK PASSED: 来源、规格、派生文件和锁文件一致")
    return 0


def _story_blockers(state: ProjectState, key: str) -> list[str]:
    spec = state.specs[key]
    blockers: list[str] = []
    if spec.kind != "story":
        return [f"{key} 不是 story"]
    if state.source_drifts:
        blockers.append("存在未确认 PRD 来源漂移")
    if spec.status != "approved":
        blockers.append(f"Story 状态为 {spec.status}")
    readiness = spec.data.get("body", {}).get("readiness")
    if readiness not in {"ready", "in_progress", "done"}:
        blockers.append(f"Story readiness={readiness}")
    for dependency in spec.dependencies:
        target = state.specs.get(dependency)
        if target is None:
            blockers.append(f"依赖不存在：{dependency}")
        elif target.status != "approved":
            blockers.append(f"依赖未批准：{dependency} ({target.status})")
        elif target.kind == "decision" and target.data.get("decision_state") != "decided":
            blockers.append(f"决策未关闭：{dependency}")
    return blockers


def command_ready(args: argparse.Namespace) -> int:
    state = _load(args)
    state.require_valid()
    stories = [
        key
        for key, spec in sorted(state.specs.items())
        if spec.kind == "story" and (not args.story or key == args.story)
    ]
    if args.story and not stories:
        raise ConfigurationError(f"Story 不存在：{args.story}")
    blocked = False
    for key in stories:
        blockers = _story_blockers(state, key)
        if blockers:
            blocked = True
            print(f"BLOCKED {key}")
            for blocker in blockers:
                print(f"  - {blocker}")
        else:
            print(f"READY {key}")
    return EXIT_BLOCKED if blocked else 0


def command_explain(args: argparse.Namespace) -> int:
    state = _load(args)
    state.require_valid()
    if args.key not in state.specs:
        raise ConfigurationError(f"规格不存在：{args.key}")
    spec = state.specs[args.key]
    reverse = reverse_dependencies(state.specs)
    payload = {
        "key": spec.key,
        "path": spec.relative_path,
        "kind": spec.kind,
        "status": spec.status,
        "title": spec.data["title"],
        "fingerprint": spec.fingerprint,
        "source_refs": spec.source_refs,
        "depends_on": spec.dependencies,
        "depended_on_by": sorted(reverse.get(spec.key, set())),
        "affects": spec.data["affects"],
    }
    print(json.dumps(payload, ensure_ascii=False, sort_keys=True, indent=2))
    return 0


def _scaffold_data(kind: str, item_id: str, version: str) -> dict[str, Any]:
    base: dict[str, Any] = {
        "schema_version": 1,
        "kind": kind,
        "id": item_id,
        "version": version,
        "status": "proposed",
        "title": "TODO",
        "summary": "TODO",
        "owners": ["TODO"],
        "source_refs": [],
        "depends_on": [],
        "affects": ["TODO"],
        "change_class": "minor",
    }
    if kind in {"requirement", "nfr", "rule"}:
        base.update(
            {
                "priority": "Must",
                "activation": {
                    "mode": "conditional",
                    "applicability": "UNKNOWN",
                    "condition": "TODO",
                },
            }
        )
    if kind == "decision":
        base.update({"decision_state": "open", "options": [], "decision": None})
    if kind == "acceptance":
        base["scenario"] = {"given": ["TODO"], "when": ["TODO"], "then": ["TODO"]}
    return base


def command_scaffold(args: argparse.Namespace) -> int:
    state = _load(args)
    kind_dir = {
        "requirement": "requirements",
        "nfr": "nfr",
        "rule": "rules",
        "decision": "decisions",
        "acceptance": "acceptance",
        "story": "stories",
        "release-baseline": "releases",
    }.get(args.kind)
    if not kind_dir:
        raise ConfigurationError(f"scaffold 暂不支持 kind={args.kind}")
    relative = f"spec/{kind_dir}/{args.id}__v{args.version}.json"
    path = resolve_within(state.config.root, relative, label="scaffold 输出")
    if path.exists():
        raise ConfigurationError(f"拒绝覆盖已有规格版本：{relative}")
    data = _scaffold_data(args.kind, args.id, args.version)
    atomic_write_text(path, dump_json(data))
    print(f"SCAFFOLDED {relative}")
    return 0


def command_snapshot(args: argparse.Namespace) -> int:
    state = _load(args)
    state.require_valid()
    state.require_sources_current()
    if not state.lock:
        raise ConfigurationError("当前没有生成锁；先运行 generate")
    if not re.fullmatch(r"[A-Za-z0-9._-]+", args.name):
        raise ConfigurationError("snapshot name 只能包含字母、数字、点、下划线和连字符")
    relative = f"spec/baselines/{args.name}.lock.json"
    path = resolve_within(state.config.root, relative, label="snapshot 输出")
    if path.exists():
        raise ConfigurationError(f"快照已存在，拒绝覆盖：{relative}")
    atomic_write_text(path, dump_json(state.lock))
    print(f"SNAPSHOT CREATED {relative}")
    return 0


def command_seal(args: argparse.Namespace) -> int:
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", args.sealed_on):
        raise ConfigurationError("sealed-on 必须使用 YYYY-MM-DD")
    if not args.sealed_by.strip() or not args.reason.strip():
        raise ConfigurationError("seal 必须提供 sealed-by 和 reason")
    state = _load(args)
    relative = create_seal(
        state,
        release_key=args.release,
        sealed_by=args.sealed_by,
        sealed_on=args.sealed_on,
        reason=args.reason,
    )
    print(f"SEALED {relative}")
    print("本地哈希链用于发现意外修改；法规级防篡改仍需受保护分支、签名或 WORM。")
    return 0


def command_verify_history(args: argparse.Namespace) -> int:
    state = _load(args)
    state.require_valid()
    errors = verify_history(state)
    if errors:
        print("HISTORY FAILED:")
        for error in errors:
            print(f"- {error}")
        return EXIT_BLOCKED
    print("HISTORY PASSED: 所有已封存版本和 Seal 链保持一致")
    return 0


def command_gate(args: argparse.Namespace) -> int:
    state = _load(args)
    path = resolve_within(state.config.root, args.from_seal, label="from-seal")
    if not path.is_file():
        raise ConfigurationError(f"from-seal 不存在：{args.from_seal}")
    errors = gate_against_seal(state, from_seal=path, release_key=args.release)
    if errors:
        print("BREAKING GATE FAILED:")
        for error in errors:
            print(f"- {error}")
        return EXIT_BLOCKED
    print("BREAKING GATE PASSED")
    return 0


def command_list(args: argparse.Namespace) -> int:
    state = _load(args)
    for key, spec in sorted(state.specs.items()):
        if args.kind and spec.kind != args.kind:
            continue
        print(f"{key}\t{spec.kind}\t{spec.status}\t{spec.data.get('title', '')}")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="openlims-specgen",
        description="OpenLIMS 需求编译、来源漂移、影响分析和派生物一致性工具",
    )
    parser.add_argument("--root", help="项目根目录；默认从当前目录向上查找")
    parser.add_argument("--config", default=DEFAULT_CONFIG, help="相对项目根的配置路径")
    sub = parser.add_subparsers(dest="command", required=True)

    validate_parser = sub.add_parser("validate", help="校验配置、规格、引用和依赖图")
    validate_parser.add_argument("--strict-warnings", action="store_true")
    validate_parser.set_defaults(handler=command_validate)

    status_parser = sub.add_parser("source-status", help="检查 PRD 来源是否偏离已确认基线")
    status_parser.add_argument("--json", action="store_true")
    status_parser.set_defaults(handler=command_source_status)

    accept_parser = sub.add_parser("source-accept", help="显式确认经过审阅的 PRD 来源变化")
    accept_parser.add_argument("--bootstrap", action="store_true", help="建立完整初始基线")
    accept_parser.add_argument("--force", action="store_true", help="允许重建已有基线")
    accept_parser.add_argument("--document")
    accept_parser.add_argument("--item", action="append", default=[])
    accept_parser.add_argument("--all-items", action="store_true")
    accept_parser.add_argument("--accept-document", action="store_true")
    accept_parser.add_argument("--waive-spec-change", action="store_true")
    accept_parser.add_argument("--reviewer", required=True)
    accept_parser.add_argument("--reviewed-on", required=True)
    accept_parser.add_argument("--reason", required=True)
    accept_parser.add_argument(
        "--acknowledgement",
        default="reviewed",
        choices=["bootstrap", "reviewed", "approved"],
    )
    accept_parser.set_defaults(handler=command_source_accept)

    impact_parser = sub.add_parser("impact", help="计算规格和来源变化的直接/传递影响")
    impact_parser.add_argument("--json", action="store_true")
    impact_parser.add_argument("--fail-on-major", action="store_true")
    impact_parser.set_defaults(handler=command_impact)

    generate_parser = sub.add_parser("generate", help="生成全部派生物并更新生成锁")
    generate_parser.add_argument(
        "--allow-source-drift",
        action="store_true",
        help="仅限诊断；不建议在 CI 或正式生成中使用",
    )
    generate_parser.set_defaults(handler=command_generate)

    check_parser = sub.add_parser("check", help="只读检查来源、派生物和生成锁一致性")
    check_parser.set_defaults(handler=command_check)

    ready_parser = sub.add_parser("ready", help="检查 Story 是否具备交给 AI 开发的条件")
    ready_parser.add_argument("--story", help="版本固定键，例如 R1-REC-003@0.1.0")
    ready_parser.set_defaults(handler=command_ready)

    explain_parser = sub.add_parser("explain", help="解释一个规格的来源、依赖和反向影响")
    explain_parser.add_argument("key")
    explain_parser.set_defaults(handler=command_explain)

    scaffold_parser = sub.add_parser("scaffold", help="一次性创建新规格版本骨架，拒绝覆盖")
    scaffold_parser.add_argument("--kind", required=True)
    scaffold_parser.add_argument("--id", required=True)
    scaffold_parser.add_argument("--version", required=True)
    scaffold_parser.set_defaults(handler=command_scaffold)

    snapshot_parser = sub.add_parser("snapshot", help="保存不可覆盖的 requirements lock 快照")
    snapshot_parser.add_argument("--name", required=True)
    snapshot_parser.set_defaults(handler=command_snapshot)

    seal_parser = sub.add_parser("seal", help="封存一个已批准发布及其精确规格/输出哈希，禁止覆盖")
    seal_parser.add_argument("--release", required=True, help="发布规格版本键")
    seal_parser.add_argument("--sealed-by", required=True)
    seal_parser.add_argument("--sealed-on", required=True)
    seal_parser.add_argument("--reason", required=True)
    seal_parser.set_defaults(handler=command_seal)

    history_parser = sub.add_parser("verify-history", help="验证已封存规格未被删除或原地修改")
    history_parser.set_defaults(handler=command_verify_history)

    gate_parser = sub.add_parser("gate", help="对比历史 Seal 与候选发布的破坏性变化")
    gate_parser.add_argument("--from-seal", required=True)
    gate_parser.add_argument("--release", required=True)
    gate_parser.set_defaults(handler=command_gate)

    list_parser = sub.add_parser("list", help="列出结构化规格")
    list_parser.add_argument("--kind")
    list_parser.set_defaults(handler=command_list)
    return parser


def main(argv: list[str] | None = None) -> int:
    _configure_utf8_stdio()
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return int(args.handler(args))
    except ValidationError as exc:
        for message in exc.messages:
            print(f"ERROR: {message}", file=sys.stderr)
        return EXIT_INVALID
    except DriftError as exc:
        print(f"DRIFT: {exc}", file=sys.stderr)
        return EXIT_DRIFT
    except (ConfigurationError, SpecgenError, ValueError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return EXIT_INVALID


if __name__ == "__main__":
    raise SystemExit(main())
