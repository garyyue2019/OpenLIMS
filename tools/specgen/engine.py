from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any

from . import __version__
from .baseline import compare_source_baseline, empty_baseline
from .errors import DriftError, ValidationError
from .impact import compute_impact
from .loader import load_config, load_optional_json, load_specs, scan_sources
from .models import ImpactReport, ProjectConfig, SourceScan, SpecObject, ValidationResult
from .render import RenderResult, render_all
from .util import (
    atomic_write_text,
    dump_json,
    normalize_text,
    project_relative,
    resolve_within,
    sha256_text,
)
from .validation import validate_generated_paths, validate_project


@dataclass
class ProjectState:
    config: ProjectConfig
    specs: dict[str, SpecObject]
    scans: dict[str, SourceScan]
    baseline: dict[str, Any]
    lock: dict[str, Any]
    validation: ValidationResult

    @classmethod
    def load(cls, root: Path, config_path: str = "spec/specgen.json") -> "ProjectState":
        config = load_config(root, config_path)
        specs, load_errors = load_specs(config)
        scans = scan_sources(config)
        baseline_path = resolve_within(
            config.root, config.source_baseline_path, label="source_baseline_path"
        )
        lock_path = resolve_within(config.root, config.lock_path, label="lock_path")
        baseline = load_optional_json(baseline_path, empty_baseline())
        lock = load_optional_json(lock_path, {})
        validation = validate_project(config, specs, scans, baseline, load_errors)
        return cls(
            config=config,
            specs=specs,
            scans=scans,
            baseline=baseline,
            lock=lock,
            validation=validation,
        )

    @property
    def source_drifts(self):
        return compare_source_baseline(self.scans, self.baseline)

    @property
    def impact(self) -> ImpactReport:
        return compute_impact(
            self.specs, self.scans, self.baseline, self.lock
        )

    def require_valid(self) -> None:
        if self.validation.errors:
            raise ValidationError(self.validation.errors)

    def require_sources_current(self) -> None:
        if self.source_drifts:
            messages = ["PRD 来源与已确认基线不一致："]
            for drift in self.source_drifts:
                messages.append(
                    f"- {drift.document}: added={list(drift.added)}, "
                    f"changed={list(drift.changed)}, removed={list(drift.removed)}, "
                    f"document_changed={drift.document_changed}"
                )
            messages.append("先运行 impact 审核影响，再显式运行 source-accept。")
            raise DriftError("\n".join(messages))

    def render(self) -> RenderResult:
        rendered = render_all(self.config, self.specs, self.scans, self.baseline)
        path_errors = validate_generated_paths(self.config, rendered.outputs)
        if path_errors:
            raise ValidationError(path_errors)
        return rendered

    def desired_lock(self, rendered: RenderResult) -> dict[str, Any]:
        return {
            "schema_version": 1,
            "generator_version": __version__,
            "config_fingerprint": self.config.config_hash,
            "specs": {
                key: {
                    "fingerprint": spec.fingerprint,
                    "behavior_fingerprint": spec.behavior_fingerprint,
                    "id": spec.id,
                    "version": spec.version,
                    "kind": spec.kind,
                    "status": spec.status,
                    "change_class": spec.data.get("change_class"),
                    "depends_on": spec.dependencies,
                    "source_refs": spec.source_refs,
                }
                for key, spec in sorted(self.specs.items())
            },
            "sources": {
                document_id: {
                    "path": scan.path,
                    "document_hash": scan.document_hash,
                    "items": {
                        item_id: item.fingerprint
                        for item_id, item in sorted(scan.items.items())
                    },
                }
                for document_id, scan in sorted(self.scans.items())
            },
            "outputs": {
                path: {
                    "sha256": sha256_text(normalize_text(content)),
                    "owners": list(rendered.owners.get(path, ())),
                }
                for path, content in sorted(rendered.outputs.items())
            },
        }


@dataclass(frozen=True)
class GenerateResult:
    written: tuple[str, ...]
    unchanged: tuple[str, ...]
    removed: tuple[str, ...]


def _existing_generated_files(state: ProjectState) -> set[str]:
    root = resolve_within(
        state.config.root, state.config.generated_root, label="generated_root"
    )
    if not root.exists():
        return set()
    return {
        project_relative(path, state.config.root)
        for path in root.rglob("*")
        if path.is_file()
    }


def generate(state: ProjectState, *, allow_source_drift: bool = False) -> GenerateResult:
    state.require_valid()
    if not allow_source_drift:
        state.require_sources_current()
    rendered = state.render()
    desired_lock = state.desired_lock(rendered)
    lock_path_relative = Path(state.config.lock_path).as_posix()
    desired_paths = set(rendered.outputs) | {lock_path_relative}
    existing = _existing_generated_files(state)
    tracked = set(state.lock.get("outputs", {})) | (
        {lock_path_relative} if state.lock else set()
    )
    unknown = sorted(existing - desired_paths - tracked)
    if unknown:
        raise DriftError(
            "生成目录包含未登记文件，拒绝覆盖或删除：\n- " + "\n- ".join(unknown)
        )

    written: list[str] = []
    unchanged: list[str] = []
    for relative, content in sorted(rendered.outputs.items()):
        path = resolve_within(state.config.root, relative, label="生成输出")
        if atomic_write_text(path, content):
            written.append(relative)
        else:
            unchanged.append(relative)

    removed: list[str] = []
    stale = sorted(set(state.lock.get("outputs", {})) - set(rendered.outputs))
    generated_root = resolve_within(
        state.config.root, state.config.generated_root, label="generated_root"
    )
    for relative in stale:
        path = resolve_within(state.config.root, relative, label="旧生成输出")
        if generated_root not in path.parents:
            raise DriftError(f"旧锁文件包含越界输出，拒绝删除：{relative}")
        if path.exists() and path.is_file():
            path.unlink()
            removed.append(relative)

    lock_path = resolve_within(state.config.root, state.config.lock_path, label="lock_path")
    if atomic_write_text(lock_path, dump_json(desired_lock)):
        written.append(lock_path_relative)
    else:
        unchanged.append(lock_path_relative)
    return GenerateResult(
        written=tuple(written), unchanged=tuple(unchanged), removed=tuple(removed)
    )


def check(state: ProjectState) -> list[str]:
    errors: list[str] = []
    if state.validation.errors:
        errors.extend(state.validation.errors)
        return errors
    for drift in state.source_drifts:
        errors.append(
            f"来源漂移 {drift.document}: added={list(drift.added)} "
            f"changed={list(drift.changed)} removed={list(drift.removed)} "
            f"document_changed={drift.document_changed}"
        )
    rendered = state.render()
    desired_lock = state.desired_lock(rendered)
    for relative, expected in sorted(rendered.outputs.items()):
        path = resolve_within(state.config.root, relative, label="生成输出")
        if not path.is_file():
            errors.append(f"缺少生成文件：{relative}")
            continue
        actual = normalize_text(path.read_text(encoding="utf-8-sig"))
        normalized_expected = normalize_text(expected)
        if not normalized_expected.endswith("\n"):
            normalized_expected += "\n"
        if actual != normalized_expected:
            errors.append(f"生成文件已过期或被手改：{relative}")

    lock_relative = Path(state.config.lock_path).as_posix()
    desired_paths = set(rendered.outputs) | {lock_relative}
    unknown = sorted(_existing_generated_files(state) - desired_paths)
    for relative in unknown:
        errors.append(f"生成目录存在未知文件：{relative}")
    stale = sorted(set(state.lock.get("outputs", {})) - set(rendered.outputs))
    for relative in stale:
        errors.append(f"锁文件仍登记旧输出：{relative}")
    if state.lock != desired_lock:
        errors.append(f"生成锁文件已过期：{state.config.lock_path}")
    return errors
