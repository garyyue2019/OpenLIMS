from __future__ import annotations

from collections import defaultdict, deque
from typing import Any

from .baseline import compare_source_baseline
from .graph import source_ref_index
from .models import ImpactReport, SourceScan, SpecObject


def _combined_reverse_graph(
    specs: dict[str, SpecObject], lock: dict[str, Any]
) -> dict[str, set[str]]:
    reverse: dict[str, set[str]] = defaultdict(set)
    for spec in specs.values():
        for dependency in spec.dependencies:
            reverse[dependency].add(spec.key)
    for key, metadata in lock.get("specs", {}).items():
        for dependency in metadata.get("depends_on", []):
            reverse[str(dependency)].add(str(key))
    return reverse


def compute_impact(
    specs: dict[str, SpecObject],
    scans: dict[str, SourceScan],
    baseline: dict[str, Any],
    lock: dict[str, Any],
) -> ImpactReport:
    previous_specs = lock.get("specs", {}) if isinstance(lock, dict) else {}
    current_keys = set(specs)
    previous_keys = set(previous_specs)
    added = sorted(current_keys - previous_keys)
    removed = sorted(previous_keys - current_keys)
    changed = sorted(
        key
        for key in current_keys & previous_keys
        if specs[key].fingerprint != str(previous_specs[key].get("fingerprint", ""))
    )
    source_drifts = compare_source_baseline(scans, baseline)
    ref_index = source_ref_index(specs)
    direct: set[str] = set(added) | set(changed) | set(removed)
    for drift in source_drifts:
        for item_id in (*drift.added, *drift.changed, *drift.removed):
            direct.update(ref_index.get((drift.document, item_id), set()))

    reverse = _combined_reverse_graph(specs, lock)
    transitive: set[str] = set()
    queue: deque[str] = deque(sorted(direct))
    while queue:
        current = queue.popleft()
        for dependent in sorted(reverse.get(current, ())):
            if dependent in direct or dependent in transitive:
                continue
            transitive.add(dependent)
            queue.append(dependent)

    major: set[str] = set(removed)
    for key in set(added) | set(changed):
        if key in specs and specs[key].data.get("change_class") == "major":
            major.add(key)

    return ImpactReport(
        added_specs=tuple(added),
        changed_specs=tuple(changed),
        removed_specs=tuple(removed),
        source_drifts=source_drifts,
        directly_impacted=tuple(sorted(direct)),
        transitively_impacted=tuple(sorted(transitive)),
        major_changes=tuple(sorted(major)),
    )
