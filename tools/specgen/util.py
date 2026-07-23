from __future__ import annotations

import hashlib
import json
import os
import re
import tempfile
import unicodedata
from pathlib import Path
from typing import Any

from .errors import ConfigurationError


SEMVER_RE = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")
ID_RE = re.compile(r"^[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+$")


def canonical_json(value: Any) -> str:
    """Return a stable UTF-8 JSON representation used for semantic hashes."""

    return json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    )


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def semantic_hash(value: Any) -> str:
    return sha256_text(canonical_json(value))


def load_json(path: Path) -> Any:
    try:
        raw = path.read_bytes()
        if raw.startswith(b"\xef\xbb\xbf"):
            raise ConfigurationError(f"权威 JSON 禁止 UTF-8 BOM：{path}")
        text = raw.decode("utf-8")

        def object_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
            result: dict[str, Any] = {}
            for key, value in pairs:
                if key in result:
                    raise ConfigurationError(f"JSON 包含重复键 {key!r}：{path}")
                result[key] = value
            return result

        def reject_constant(value: str) -> Any:
            raise ConfigurationError(f"JSON 禁止非有限数值 {value}：{path}")

        def reject_float(value: str) -> Any:
            raise ConfigurationError(
                f"权威 JSON 禁止浮点 number {value}；十进制量值必须使用字符串：{path}"
            )

        value = json.loads(
            text,
            object_pairs_hook=object_pairs,
            parse_constant=reject_constant,
            parse_float=reject_float,
        )

        def check_strings(node: Any, location: str = "$") -> None:
            if isinstance(node, str):
                if "\r" in node:
                    raise ConfigurationError(f"JSON 字符串包含 CR：{path} {location}")
                if unicodedata.normalize("NFC", node) != node:
                    raise ConfigurationError(
                        f"JSON 字符串不是 Unicode NFC：{path} {location}"
                    )
            elif isinstance(node, list):
                for index, item in enumerate(node):
                    check_strings(item, f"{location}[{index}]")
            elif isinstance(node, dict):
                for key, item in node.items():
                    check_strings(key, f"{location}.<key>")
                    check_strings(item, f"{location}.{key}")

        check_strings(value)
        return value
    except FileNotFoundError as exc:
        raise ConfigurationError(f"文件不存在：{path}") from exc
    except UnicodeDecodeError as exc:
        raise ConfigurationError(f"JSON 不是有效 UTF-8：{path}: {exc}") from exc
    except json.JSONDecodeError as exc:
        raise ConfigurationError(
            f"JSON 解析失败：{path}:{exc.lineno}:{exc.colno} {exc.msg}"
        ) from exc


def dump_json(value: Any) -> str:
    return json.dumps(
        value, ensure_ascii=False, sort_keys=True, indent=2, allow_nan=False
    ) + "\n"


def normalize_text(value: str) -> str:
    return value.replace("\r\n", "\n").replace("\r", "\n")


def ensure_relative_path(value: str, *, label: str) -> Path:
    candidate = Path(value)
    if candidate.is_absolute() or ".." in candidate.parts:
        raise ConfigurationError(f"{label} 必须是项目内相对路径：{value}")
    return candidate


def resolve_within(root: Path, relative: str | Path, *, label: str) -> Path:
    rel = ensure_relative_path(str(relative), label=label)
    root_resolved = root.resolve()
    result = (root_resolved / rel).resolve()
    if result != root_resolved and root_resolved not in result.parents:
        raise ConfigurationError(f"{label} 越出项目目录：{relative}")
    return result


def project_relative(path: Path, root: Path) -> str:
    return path.resolve().relative_to(root.resolve()).as_posix()


def atomic_write_text(path: Path, content: str) -> bool:
    """Atomically write normalized UTF-8 text; return True when content changed."""

    normalized = normalize_text(content)
    if not normalized.endswith("\n"):
        normalized += "\n"
    if path.exists() and normalize_text(path.read_text(encoding="utf-8-sig")) == normalized:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary_name = tempfile.mkstemp(prefix=f".{path.name}.", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(normalized)
        os.replace(temporary_name, path)
    finally:
        if os.path.exists(temporary_name):
            os.unlink(temporary_name)
    return True


def parse_semver(value: str) -> tuple[int, int, int]:
    match = SEMVER_RE.fullmatch(value)
    if not match:
        raise ValueError(value)
    return tuple(int(part) for part in match.groups())


def get_nested(mapping: dict[str, Any], path: str, default: Any = None) -> Any:
    current: Any = mapping
    for part in path.split("."):
        if not isinstance(current, dict) or part not in current:
            return default
        current = current[part]
    return current
