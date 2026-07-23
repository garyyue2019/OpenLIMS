from __future__ import annotations

from pathlib import Path
from typing import Any

from .errors import ConfigurationError, ValidationError
from .models import ProjectConfig, SourceScan, SpecObject
from .scanner import scan_document
from .util import load_json, project_relative, resolve_within


DEFAULT_CONFIG = "spec/specgen.json"


def find_project_root(start: Path, config_path: str = DEFAULT_CONFIG) -> Path:
    current = start.resolve()
    for candidate in (current, *current.parents):
        if (candidate / config_path).is_file():
            return candidate
    raise ConfigurationError(
        f"从 {start} 向上未找到 {config_path}；请在项目根目录运行或使用 --root"
    )


def load_config(root: Path, config_path: str = DEFAULT_CONFIG) -> ProjectConfig:
    path = resolve_within(root, config_path, label="配置文件")
    data = load_json(path)
    if not isinstance(data, dict):
        raise ConfigurationError(f"配置根节点必须是对象：{path}")
    required = {
        "schema_version",
        "project",
        "spec_roots",
        "source_documents",
        "source_baseline_path",
        "generated_root",
        "lock_path",
    }
    missing = sorted(required - data.keys())
    if missing:
        raise ConfigurationError(f"配置缺少字段：{', '.join(missing)}")
    if data["schema_version"] != 1:
        raise ConfigurationError("当前仅支持 specgen 配置 schema_version=1")
    for key in ("source_baseline_path", "generated_root", "lock_path"):
        resolve_within(root, str(data[key]), label=key)
    if not isinstance(data["spec_roots"], list) or not data["spec_roots"]:
        raise ConfigurationError("spec_roots 必须是非空数组")
    for value in data["spec_roots"]:
        resolve_within(root, str(value), label="spec_roots")
    return ProjectConfig(root=root.resolve(), path=path, data=data)


def load_specs(config: ProjectConfig) -> tuple[dict[str, SpecObject], list[str]]:
    specs: dict[str, SpecObject] = {}
    errors: list[str] = []
    for root_value in config.spec_roots:
        spec_root = resolve_within(config.root, root_value, label="spec_roots")
        if not spec_root.exists():
            errors.append(f"规格目录不存在：{project_relative(spec_root, config.root)}")
            continue
        for path in sorted(spec_root.rglob("*.json")):
            try:
                data: Any = load_json(path)
            except ConfigurationError as exc:
                errors.append(str(exc))
                continue
            if not isinstance(data, dict):
                errors.append(f"规格文件根节点必须是对象：{project_relative(path, config.root)}")
                continue
            spec = SpecObject(
                path=path,
                relative_path=project_relative(path, config.root),
                data=data,
            )
            if not spec.id:
                errors.append(f"规格缺少 id：{spec.relative_path}")
                continue
            if spec.key in specs:
                errors.append(
                    f"规格版本键重复：{spec.key} 同时位于 {specs[spec.key].relative_path} 和 {spec.relative_path}"
                )
                continue
            specs[spec.key] = spec
    return specs, errors


def scan_sources(config: ProjectConfig) -> dict[str, SourceScan]:
    scans: dict[str, SourceScan] = {}
    errors: list[str] = []
    for source in config.source_documents:
        if not isinstance(source, dict):
            errors.append("source_documents 每项必须是对象")
            continue
        missing = [key for key in ("id", "path", "format") if key not in source]
        if missing:
            errors.append(f"source_documents 项缺少字段：{', '.join(missing)}")
            continue
        source_id = str(source["id"])
        if source_id in scans:
            errors.append(f"来源文档 ID 重复：{source_id}")
            continue
        relative = str(source["path"])
        path = resolve_within(config.root, relative, label="source_documents.path")
        if not path.is_file():
            errors.append(f"来源文档不存在：{relative}")
            continue
        try:
            scans[source_id] = scan_document(
                source_id,
                relative,
                str(source["format"]),
                path,
            )
        except ValidationError as exc:
            errors.extend(exc.messages)
    if errors:
        raise ValidationError(errors)
    return scans


def load_optional_json(path: Path, default: Any) -> Any:
    if not path.exists():
        return default
    return load_json(path)
