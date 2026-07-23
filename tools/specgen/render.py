from __future__ import annotations

import csv
import io
import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from . import __version__
from .baseline import compare_source_baseline
from .models import ProjectConfig, SourceScan, SpecObject
from .util import canonical_json, dump_json, semantic_hash


@dataclass(frozen=True)
class RenderResult:
    outputs: dict[str, str]
    owners: dict[str, tuple[str, ...]]


def _generated_header(source_keys: list[str] | tuple[str, ...] = ()) -> str:
    sources = ", ".join(source_keys) if source_keys else "project-spec-catalog"
    return (
        "<!-- GENERATED FILE — DO NOT EDIT.\n"
        f"Generator: openlims-specgen@{__version__}\n"
        f"Sources: {sources}\n"
        "Edit files under spec/ and run `python -m tools.specgen generate`.\n"
        "-->\n"
    )


def _display(value: Any) -> str:
    if value is None:
        return "—"
    if isinstance(value, bool):
        return "是" if value else "否"
    if isinstance(value, (dict, list)):
        return f"`{canonical_json(value)}`"
    text = str(value).strip()
    return text or "—"


def _bullet_lines(items: Any, empty: str = "- 无") -> list[str]:
    if not isinstance(items, list) or not items:
        return [empty]
    lines: list[str] = []
    for item in items:
        if isinstance(item, dict):
            lines.append(f"- `{canonical_json(item)}`")
        else:
            lines.append(f"- {item}")
    return lines


def _table_escape(value: Any) -> str:
    text = str(value if value is not None else "")
    return text.replace("|", "\\|").replace("\n", "<br>")


def _story_output_name(spec: SpecObject) -> str:
    return f"{spec.id}__v{spec.version}"


def render_task_card(spec: SpecObject) -> str:
    body = spec.data["body"]
    references = [
        f"{ref.get('document')}#{ref.get('item')}" for ref in spec.source_refs
    ]
    lines = [
        _generated_header([spec.key]).rstrip(),
        "",
        f"# {spec.id}：{spec.data['title']}",
        "",
        "## 元数据",
        "",
        "| 字段 | 值 |",
        "|---|---|",
        f"| 规格版本 | `{spec.version}` |",
        f"| 评审状态 | `{spec.status}` |",
        f"| 目标发布 | `{spec.data['target_release']}` |",
        f"| Epic | `{spec.data['epic_id']}` |",
        f"| Feature | `{spec.data['feature_id']}` |",
        f"| 开发就绪度 | `{body['readiness']}` |",
        f"| 变更级别 | `{spec.data['change_class']}` |",
        f"| 负责人角色 | {_table_escape(', '.join(spec.data['owners']))} |",
        f"| 影响模块 | {_table_escape(', '.join(spec.data['affects']))} |",
        f"| 来源 | {_table_escape(', '.join(references))} |",
        f"| 固定依赖 | {_table_escape(', '.join(spec.dependencies))} |",
        f"| 规格指纹 | `{spec.fingerprint}` |",
        "",
        "## 业务结果",
        "",
        str(body["business_outcome"]),
        "",
        "## 主要参与者",
        "",
        str(body["actor"]),
        "",
        "## 触发条件",
        "",
        str(body["trigger"]),
        "",
        "## 前置条件",
        "",
        *_bullet_lines(body["preconditions"]),
        "",
        "## 正常路径",
        "",
        *_bullet_lines(body["happy_path"]),
        "",
        "## 失败路径",
        "",
        *_bullet_lines(body["failure_paths"]),
        "",
        "## 领域不变量",
        "",
        *_bullet_lines(body["invariants"]),
        "",
        "## 数据契约",
        "",
        "```json",
        json.dumps(body["data_contract"], ensure_ascii=False, sort_keys=True, indent=2),
        "```",
        "",
        "## API / 命令契约",
        "",
        "```json",
        json.dumps(body["api_contract"], ensure_ascii=False, sort_keys=True, indent=2),
        "```",
        "",
        "## 状态转换",
        "",
        *_bullet_lines(body["state_transitions"]),
        "",
        "## 权限与职责分离",
        "",
        *_bullet_lines(body["permissions"]),
        "",
        "## 审计要求",
        "",
        *_bullet_lines(body["audit"]),
        "",
        "## UX 状态",
        "",
        *_bullet_lines(body["ui_states"]),
        "",
        "## 可观测性",
        "",
        *_bullet_lines(body["observability"]),
        "",
        "## 测试场景",
        "",
        "| ID | 类型 | Given | When | Then |",
        "|---|---|---|---|---|",
    ]
    for case in body["test_cases"]:
        lines.append(
            "| {id} | {type} | {given} | {when} | {then} |".format(
                id=_table_escape(case.get("id", "")),
                type=_table_escape(case.get("type", "")),
                given=_table_escape("；".join(_as_list(case.get("given")))),
                when=_table_escape("；".join(_as_list(case.get("when")))),
                then=_table_escape("；".join(_as_list(case.get("then")))),
            )
        )
    lines.extend(
        [
            "",
            "## 明确非目标",
            "",
            *_bullet_lines(body["non_goals"]),
            "",
            "## 允许修改路径",
            "",
            *_bullet_lines([f"`{item}`" for item in body["allowed_paths"]]),
            "",
            "## 验证命令",
            "",
            *_bullet_lines([f"`{item}`" for item in body["verification_commands"]]),
            "",
            "## 完成定义",
            "",
            *_bullet_lines(body["definition_of_done"]),
            "",
            "## AI 执行约束",
            "",
            "- 不得修改本文件；它由结构化规格生成。",
            "- 不得把待决策项自行解释为默认业务规则。",
            "- 不得访问其他模块私有表；必须使用批准的端口或事件契约。",
            "- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。",
        ]
    )
    return "\n".join(lines) + "\n"


def _as_list(value: Any) -> list[str]:
    if isinstance(value, list):
        return [str(item) for item in value]
    return [str(value)]


def _gherkin_step(keyword: str, values: Any) -> list[str]:
    items = _as_list(values)
    if not items:
        return []
    lines = [f"    {keyword} {items[0]}"]
    lines.extend(f"    And {item}" for item in items[1:])
    return lines


def render_feature(spec: SpecObject) -> str:
    body = spec.data["body"]
    lines = [
        f"# GENERATED FILE — DO NOT EDIT. openlims-specgen@{__version__}",
        f"# Source: {spec.key}",
        f"# Spec-Fingerprint: {spec.fingerprint}",
        f"Feature: {spec.id} {spec.data['title']}",
        f"  {body['business_outcome']}",
        "",
    ]
    for case in body["test_cases"]:
        tags = ["@generated", f"@{spec.id.lower()}"]
        case_type = re.sub(r"[^a-z0-9_-]+", "-", str(case.get("type", "case")).lower())
        tags.append(f"@{case_type}")
        lines.append("  " + " ".join(tags))
        lines.append(f"  Scenario: {case.get('id')} {case.get('title', case.get('type'))}")
        lines.extend(_gherkin_step("Given", case.get("given", [])))
        lines.extend(_gherkin_step("When", case.get("when", [])))
        lines.extend(_gherkin_step("Then", case.get("then", [])))
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def render_acceptance_feature(spec: SpecObject) -> str:
    scenario = spec.data["scenario"]
    lines = [
        f"# GENERATED FILE — DO NOT EDIT. openlims-specgen@{__version__}",
        f"# Source: {spec.key}",
        f"# Spec-Fingerprint: {spec.fingerprint}",
        f"Feature: {spec.id} {spec.data['title']}",
        f"  {spec.data['summary']}",
        "",
        "  @generated @prd-acceptance",
        f"  Scenario: {spec.id} {spec.data['title']}",
        *_gherkin_step("Given", scenario.get("given", [])),
        *_gherkin_step("When", scenario.get("when", [])),
        *_gherkin_step("Then", scenario.get("then", [])),
        "",
    ]
    return "\n".join(lines)


def _render_catalog(specs: dict[str, SpecObject]) -> str:
    lines = [
        _generated_header().rstrip(),
        "",
        "# 结构化规格目录",
        "",
        "| 版本键 | 类型 | 状态 | 标题 | 变更级别 | 影响模块 | 指纹 |",
        "|---|---|---|---|---|---|---|",
    ]
    for key, spec in sorted(specs.items()):
        lines.append(
            f"| `{key}` | `{spec.kind}` | `{spec.status}` | {_table_escape(spec.data['title'])} | "
            f"`{spec.data['change_class']}` | {_table_escape(', '.join(spec.data['affects']))} | `{spec.fingerprint[:12]}` |"
        )
    return "\n".join(lines) + "\n"


def _render_lifecycle_index(specs: dict[str, SpecObject]) -> str:
    grouped: dict[str, list[SpecObject]] = {}
    for spec in specs.values():
        grouped.setdefault(spec.id, []).append(spec)
    lines = [
        _generated_header().rstrip(),
        "",
        "# 规格版本与生命周期索引",
        "",
        "> 每个版本保存在独立源文件中。本索引只是派生视图，不得通过改写旧文件表达 superseded。",
        "",
        "| 逻辑 ID | 版本 | 状态 | 文件 |",
        "|---|---|---|---|",
    ]
    for logical_id, versions in sorted(grouped.items()):
        for spec in sorted(versions, key=lambda item: item.version):
            lines.append(
                f"| `{logical_id}` | `{spec.version}` | `{spec.status}` | `{spec.relative_path}` |"
            )
    return "\n".join(lines) + "\n"


def _render_dependency_graph(specs: dict[str, SpecObject]) -> str:
    lines = [
        f"%% GENERATED FILE — DO NOT EDIT. openlims-specgen@{__version__}",
        "flowchart LR",
    ]
    node_names: dict[str, str] = {}
    for index, key in enumerate(sorted(specs), start=1):
        node = f"N{index}"
        node_names[key] = node
        label = f"{key}\\n{specs[key].kind}/{specs[key].status}".replace('"', "'")
        lines.append(f'  {node}["{label}"]')
    for key, spec in sorted(specs.items()):
        for dependency in sorted(spec.dependencies):
            if dependency in node_names:
                lines.append(f"  {node_names[dependency]} --> {node_names[key]}")
    return "\n".join(lines) + "\n"


def _render_traceability_csv(specs: dict[str, SpecObject]) -> str:
    stream = io.StringIO(newline="")
    writer = csv.writer(stream, lineterminator="\n")
    writer.writerow(
        [
            "spec_key",
            "kind",
            "status",
            "title",
            "source_refs",
            "depends_on",
            "affects",
            "priority",
            "activation_mode",
            "applicability",
            "test_case_ids",
            "fingerprint",
        ]
    )
    for key, spec in sorted(specs.items()):
        activation = spec.data.get("activation", {})
        test_cases = spec.data.get("body", {}).get("test_cases", [])
        writer.writerow(
            [
                key,
                spec.kind,
                spec.status,
                spec.data["title"],
                ";".join(
                    f"{ref.get('document')}#{ref.get('item')}" for ref in spec.source_refs
                ),
                ";".join(spec.dependencies),
                ";".join(spec.data["affects"]),
                spec.data.get("priority", ""),
                activation.get("mode", ""),
                activation.get("applicability", ""),
                ";".join(str(case.get("id", "")) for case in test_cases),
                spec.fingerprint,
            ]
        )
    return stream.getvalue()


def _traceability_json(specs: dict[str, SpecObject]) -> str:
    return dump_json(
        {
            "_generated": {
                "generator": f"openlims-specgen@{__version__}",
                "do_not_edit": True,
            },
            "specs": {
                key: {
                    "kind": spec.kind,
                    "status": spec.status,
                    "title": spec.data["title"],
                    "source_refs": spec.source_refs,
                    "depends_on": spec.dependencies,
                    "affects": spec.data["affects"],
                    "fingerprint": spec.fingerprint,
                }
                for key, spec in sorted(specs.items())
            },
        }
    )


def _render_source_inventory(
    scans: dict[str, SourceScan], specs: dict[str, SpecObject]
) -> str:
    mapped: dict[tuple[str, str], list[str]] = {}
    for spec in specs.values():
        for ref in spec.source_refs:
            key = (str(ref.get("document", "")), str(ref.get("item", "")))
            mapped.setdefault(key, []).append(spec.key)
    stream = io.StringIO(newline="")
    writer = csv.writer(stream, lineterminator="\n")
    writer.writerow(
        [
            "document",
            "source_id",
            "kind",
            "section",
            "line",
            "title",
            "fingerprint",
            "curated_specs",
        ]
    )
    for document_id, scan in sorted(scans.items()):
        for item_id, item in sorted(scan.items.items()):
            writer.writerow(
                [
                    document_id,
                    item_id,
                    item.kind,
                    item.section,
                    item.line,
                    item.title,
                    item.fingerprint,
                    ";".join(sorted(mapped.get((document_id, item_id), []))),
                ]
            )
    return stream.getvalue()


def _render_source_coverage(
    scans: dict[str, SourceScan], specs: dict[str, SpecObject]
) -> str:
    mapped = {
        (str(ref.get("document", "")), str(ref.get("item", "")))
        for spec in specs.values()
        for ref in spec.source_refs
    }
    total = sum(len(scan.items) for scan in scans.values())
    covered = sum(
        1
        for document_id, scan in scans.items()
        for item_id in scan.items
        if (document_id, item_id) in mapped
    )
    percent = 0.0 if total == 0 else covered * 100.0 / total
    lines = [
        _generated_header().rstrip(),
        "",
        "# PRD 来源覆盖报告",
        "",
        f"- 已扫描带 ID 来源条目：**{total}**",
        f"- 已由结构化规格引用：**{covered}**",
        f"- 当前结构化覆盖率：**{percent:.1f}%**",
        "",
        "> 覆盖率低不表示 PRD 缺失；本仓库当前只精化首批纵向切片。未引用条目仍由 source-drift 扫描纳入变化监控。",
        "",
        "## 按来源文档",
        "",
        "| 文档 | 路径 | 条目数 | 已引用 | 文档哈希 |",
        "|---|---|---:|---:|---|",
    ]
    for document_id, scan in sorted(scans.items()):
        document_covered = sum(
            1 for item_id in scan.items if (document_id, item_id) in mapped
        )
        lines.append(
            f"| `{document_id}` | `{scan.path}` | {len(scan.items)} | {document_covered} | `{scan.document_hash[:12]}` |"
        )
    return "\n".join(lines) + "\n"


def _story_blockers(spec: SpecObject, specs: dict[str, SpecObject]) -> list[str]:
    blockers: list[str] = []
    body = spec.data.get("body", {})
    if spec.status != "approved":
        blockers.append(f"规格状态为 {spec.status}，尚未批准")
    if body.get("readiness") not in {"ready", "in_progress", "done"}:
        blockers.append(f"开发就绪度为 {body.get('readiness', 'missing')}")
    for dependency in spec.dependencies:
        target = specs.get(dependency)
        if target is None:
            blockers.append(f"依赖不存在：{dependency}")
        elif target.status != "approved":
            blockers.append(f"依赖 {dependency} 状态为 {target.status}")
        elif target.kind == "decision" and target.data.get("decision_state") != "decided":
            blockers.append(f"决策 {dependency} 尚未形成 decided 结论")
    return blockers


def _render_readiness(specs: dict[str, SpecObject], source_drift_count: int) -> str:
    stories = [spec for spec in specs.values() if spec.kind == "story"]
    lines = [
        _generated_header().rstrip(),
        "",
        "# AI 开发就绪报告",
        "",
        f"- 未批准 PRD 来源漂移：**{source_drift_count}** 个文档",
        f"- Story 数量：**{len(stories)}**",
        "",
        "| Story | 规格状态 | 就绪度 | 结论 | 阻塞原因 |",
        "|---|---|---|---|---|",
    ]
    for spec in sorted(stories, key=lambda item: item.key):
        blockers = _story_blockers(spec, specs)
        conclusion = "READY" if not blockers and source_drift_count == 0 else "BLOCKED"
        if source_drift_count:
            blockers.append("存在未批准 PRD 来源漂移")
        lines.append(
            f"| `{spec.key}` | `{spec.status}` | `{spec.data['body']['readiness']}` | **{conclusion}** | {_table_escape('；'.join(blockers) or '无')} |"
        )
    lines.extend(
        [
            "",
            "> BLOCKED 是预期的诚实状态。当前 PRD 仍待联合评审，生成任务卡不等于批准进入生产开发。",
        ]
    )
    return "\n".join(lines) + "\n"


def _render_ownership() -> str:
    return (
        _generated_header()
        + "\n# 文件所有权规则\n\n"
        + "| 区域 | 所有权 | 修改规则 |\n"
        + "|---|---|---|\n"
        + "| `spec/` | 人工/评审所有 | 通过评审修改；每个版本独立文件 |\n"
        + "| `generated/spec/` | 生成器所有 | 禁止手改；运行 generate 更新 |\n"
        + "| `tools/specgen/` | 工具代码 | 正常代码评审，修改后必须跑完整测试 |\n"
        + "| 未来 `src/` | 实现所有 | 不由需求编译器覆盖；由契约和测试驱动同步 |\n"
        + "| 数据库迁移 | 追加历史 | 已执行迁移禁止重写，只能新增迁移 |\n"
        + "| 验收证据 | 不可变证据 | 固定需求版本、发布基线和哈希，不重新生成覆盖 |\n\n"
        + "生成目录不得混入手写文件。需要人工扩展时，应在生成目录之外通过明确端口引用。\n"
    )


def _render_readme(config: ProjectConfig) -> str:
    return (
        _generated_header()
        + f"\n# {config.project} 需求编译输出\n\n"
        + "这里的文件全部由 `python -m tools.specgen generate` 生成。\n\n"
        + "- `catalog.md`：结构化规格目录；\n"
        + "- `lifecycle-index.md`：同一逻辑 ID 的版本与状态；\n"
        + "- `traceability.csv/json`：来源、依赖、模块和测试追踪；\n"
        + "- `source-inventory.csv`：PRD 中全部带 ID 条目的扫描结果；\n"
        + "- `source-coverage.md`：精化规格对 PRD 条目的覆盖；\n"
        + "- `readiness-report.md`：AI 开发就绪门禁；\n"
        + "- `tasks/`：AI 可执行任务卡；\n"
        + "- `features/`：由 Story 测试场景生成的 Gherkin；\n"
        + "- `releases/`：固定版本的 requirements lock。\n\n"
        + "不要编辑本目录。修改 `spec/` 后运行生成器。\n"
    )


def _render_release_lock(spec: SpecObject, specs: dict[str, SpecObject], scans: dict[str, SourceScan]) -> str:
    selected: dict[str, Any] = {}
    for key in spec.data.get("selected_specs", []):
        target = specs.get(key)
        if target is None:
            continue
        selected[key] = {
            "kind": target.kind,
            "status": target.status,
            "fingerprint": target.fingerprint,
            "source_refs": [
                {
                    **ref,
                    "source_fingerprint": scans.get(str(ref.get("document")), SourceScan("", "", "", "", {})).items.get(str(ref.get("item"))).fingerprint
                    if scans.get(str(ref.get("document")))
                    and str(ref.get("item")) in scans[str(ref.get("document"))].items
                    else None,
                }
                for ref in target.source_refs
            ],
        }
    return dump_json(
        {
            "_generated": {
                "generator": f"openlims-specgen@{__version__}",
                "do_not_edit": True,
            },
            "release": spec.key,
            "status": spec.status,
            "runtime_resolution": "pinned_only",
            "baseline_fingerprint": semantic_hash(spec.data),
            "selected_specs": selected,
        }
    )


def render_all(
    config: ProjectConfig,
    specs: dict[str, SpecObject],
    scans: dict[str, SourceScan],
    baseline: dict[str, Any],
) -> RenderResult:
    root = Path(config.generated_root).as_posix().rstrip("/")
    outputs: dict[str, str] = {}
    owners: dict[str, tuple[str, ...]] = {}

    def add(relative: str, content: str, source_keys: tuple[str, ...] = ()) -> None:
        path = f"{root}/{relative}"
        outputs[path] = content
        owners[path] = tuple(sorted(source_keys))

    all_keys = tuple(sorted(specs))
    source_drifts = compare_source_baseline(scans, baseline)
    add("README.md", _render_readme(config), all_keys)
    add("OWNERSHIP.md", _render_ownership(), all_keys)
    add("catalog.md", _render_catalog(specs), all_keys)
    add("lifecycle-index.md", _render_lifecycle_index(specs), all_keys)
    add("dependency-graph.mmd", _render_dependency_graph(specs), all_keys)
    add("traceability.csv", _render_traceability_csv(specs), all_keys)
    add("traceability.json", _traceability_json(specs), all_keys)
    add("source-inventory.csv", _render_source_inventory(scans, specs), all_keys)
    add("source-coverage.md", _render_source_coverage(scans, specs), all_keys)
    add("readiness-report.md", _render_readiness(specs, len(source_drifts)), all_keys)

    for spec in sorted(specs.values(), key=lambda item: item.key):
        if spec.kind == "story":
            name = _story_output_name(spec)
            add(f"tasks/{name}.md", render_task_card(spec), (spec.key, *spec.dependencies))
            add(f"features/{name}.feature", render_feature(spec), (spec.key, *spec.dependencies))
        if spec.kind == "acceptance":
            name = _story_output_name(spec)
            add(
                f"features/{name}.feature",
                render_acceptance_feature(spec),
                (spec.key, *spec.dependencies),
            )
        if spec.kind == "release-baseline":
            name = _story_output_name(spec)
            add(
                f"releases/{name}.requirements-lock.json",
                _render_release_lock(spec, specs, scans),
                (spec.key, *tuple(spec.data.get("selected_specs", []))),
            )
    return RenderResult(outputs=outputs, owners=owners)
