from __future__ import annotations

from pathlib import Path
from typing import Any

from tools.specgen.baseline import build_full_baseline
from tools.specgen.engine import ProjectState, generate
from tools.specgen.util import dump_json


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(dump_json(value), encoding="utf-8", newline="\n")


def base_requirement(version: str = "1.0.0", summary: str = "隔离规则") -> dict[str, Any]:
    return {
        "schema_version": 1,
        "kind": "requirement",
        "id": "REQ-ONE-001",
        "version": version,
        "status": "approved",
        "title": "测试需求",
        "summary": summary,
        "owners": ["测试负责人"],
        "source_refs": [{"document": "PRD-TEST", "item": "REQ-ONE-001"}],
        "depends_on": [],
        "affects": ["test-module"],
        "change_class": "patch" if version.endswith(".1") else "minor",
        "priority": "Must",
        "activation": {
            "mode": "core",
            "applicability": "ENABLED",
            "condition": "测试基线",
        },
    }


def release(version: str, selected: list[str]) -> dict[str, Any]:
    return {
        "schema_version": 1,
        "kind": "release-baseline",
        "id": "REL-TEST",
        "version": version,
        "status": "approved",
        "title": "测试发布",
        "summary": "测试用固定发布基线",
        "owners": ["发布负责人"],
        "source_refs": [],
        "depends_on": selected,
        "affects": ["test-release"],
        "change_class": "minor",
        "runtime_resolution": "pinned_only",
        "breaking_change_approvals": [],
        "selected_specs": selected,
    }


def create_minimal_project(root: Path) -> ProjectState:
    (root / "docs").mkdir(parents=True, exist_ok=True)
    (root / "docs" / "prd.md").write_text(
        "# 测试 PRD\n\n| ID | 需求 |\n|---|---|\n| REQ-ONE-001 | 身份评估前保持隔离 |\n",
        encoding="utf-8",
        newline="\n",
    )
    config = {
        "schema_version": 1,
        "project": "SpecgenTest",
        "spec_roots": ["spec/requirements", "spec/releases"],
        "source_documents": [
            {"id": "PRD-TEST", "path": "docs/prd.md", "format": "openlims-prd-markdown"}
        ],
        "source_baseline_path": "spec/source-baseline.json",
        "generated_root": "generated/spec",
        "lock_path": "generated/spec/.specgen-lock.json",
    }
    write_json(root / "spec" / "specgen.json", config)
    write_json(
        root / "spec" / "requirements" / "REQ-ONE-001__v1.0.0.json",
        base_requirement(),
    )
    write_json(
        root / "spec" / "releases" / "REL-TEST__v1.0.0.json",
        release("1.0.0", ["REQ-ONE-001@1.0.0"]),
    )
    initial = ProjectState.load(root)
    baseline = build_full_baseline(
        initial.scans,
        reviewer="unit-test",
        reviewed_on="2026-07-23",
        reason="test baseline",
        acknowledgement="approved",
    )
    write_json(root / "spec" / "source-baseline.json", baseline)
    return ProjectState.load(root)


def create_generated_project(root: Path) -> ProjectState:
    state = create_minimal_project(root)
    generate(state)
    return ProjectState.load(root)
