from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .util import semantic_hash


@dataclass(frozen=True)
class SourceItem:
    id: str
    title: str
    content: str
    section: str
    line: int
    kind: str

    @property
    def fingerprint(self) -> str:
        # Line numbers intentionally do not affect the semantic fingerprint.
        return semantic_hash(
            {
                "id": self.id,
                "title": self.title,
                "content": self.content,
                "section": self.section,
                "kind": self.kind,
            }
        )


@dataclass(frozen=True)
class SourceScan:
    id: str
    path: str
    format: str
    document_hash: str
    items: dict[str, SourceItem]


@dataclass(frozen=True)
class SpecObject:
    path: Path
    relative_path: str
    data: dict[str, Any]

    @property
    def id(self) -> str:
        return str(self.data.get("id", ""))

    @property
    def version(self) -> str:
        return str(self.data.get("version", ""))

    @property
    def key(self) -> str:
        return f"{self.id}@{self.version}"

    @property
    def kind(self) -> str:
        return str(self.data.get("kind", ""))

    @property
    def status(self) -> str:
        return str(self.data.get("status", ""))

    @property
    def dependencies(self) -> list[str]:
        return list(self.data.get("depends_on", []))

    @property
    def source_refs(self) -> list[dict[str, str]]:
        return list(self.data.get("source_refs", []))

    @property
    def fingerprint(self) -> str:
        return semantic_hash(self.data)

    @property
    def behavior_fingerprint(self) -> str:
        excluded = {
            "$schema",
            "schema_version",
            "version",
            "status",
            "title",
            "owners",
            "source_refs",
            "change_class",
        }
        return semantic_hash(
            {key: value for key, value in self.data.items() if key not in excluded}
        )


@dataclass(frozen=True)
class ProjectConfig:
    root: Path
    path: Path
    data: dict[str, Any]

    @property
    def project(self) -> str:
        return str(self.data["project"])

    @property
    def generated_root(self) -> str:
        return str(self.data["generated_root"])

    @property
    def lock_path(self) -> str:
        return str(self.data["lock_path"])

    @property
    def source_baseline_path(self) -> str:
        return str(self.data["source_baseline_path"])

    @property
    def spec_roots(self) -> list[str]:
        return list(self.data["spec_roots"])

    @property
    def source_documents(self) -> list[dict[str, str]]:
        return list(self.data.get("source_documents", []))

    @property
    def config_hash(self) -> str:
        return semantic_hash(self.data)


@dataclass
class ValidationResult:
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return not self.errors


@dataclass(frozen=True)
class SourceDrift:
    document: str
    added: tuple[str, ...] = ()
    changed: tuple[str, ...] = ()
    removed: tuple[str, ...] = ()
    document_changed: bool = False

    @property
    def has_drift(self) -> bool:
        return bool(self.added or self.changed or self.removed or self.document_changed)


@dataclass(frozen=True)
class ImpactReport:
    added_specs: tuple[str, ...]
    changed_specs: tuple[str, ...]
    removed_specs: tuple[str, ...]
    source_drifts: tuple[SourceDrift, ...]
    directly_impacted: tuple[str, ...]
    transitively_impacted: tuple[str, ...]
    major_changes: tuple[str, ...]
