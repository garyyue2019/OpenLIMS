from __future__ import annotations

from collections import defaultdict, deque

from .models import SpecObject


def dependency_cycles(specs: dict[str, SpecObject]) -> list[list[str]]:
    state: dict[str, int] = {}
    stack: list[str] = []
    cycles: list[list[str]] = []

    def visit(node: str) -> None:
        current_state = state.get(node, 0)
        if current_state == 2:
            return
        if current_state == 1:
            try:
                start = stack.index(node)
            except ValueError:
                start = 0
            cycle = stack[start:] + [node]
            if cycle not in cycles:
                cycles.append(cycle)
            return
        state[node] = 1
        stack.append(node)
        for dependency in specs[node].dependencies:
            if dependency in specs:
                visit(dependency)
        stack.pop()
        state[node] = 2

    for spec_id in sorted(specs):
        if state.get(spec_id, 0) == 0:
            visit(spec_id)
    return cycles


def reverse_dependencies(specs: dict[str, SpecObject]) -> dict[str, set[str]]:
    reverse: dict[str, set[str]] = defaultdict(set)
    for spec in specs.values():
        for dependency in spec.dependencies:
            reverse[dependency].add(spec.key)
    return reverse


def transitive_dependents(specs: dict[str, SpecObject], seeds: set[str]) -> set[str]:
    reverse = reverse_dependencies(specs)
    found: set[str] = set()
    queue: deque[str] = deque(sorted(seeds))
    while queue:
        current = queue.popleft()
        for dependent in sorted(reverse.get(current, ())):
            if dependent in found or dependent in seeds:
                continue
            found.add(dependent)
            queue.append(dependent)
    return found


def source_ref_index(specs: dict[str, SpecObject]) -> dict[tuple[str, str], set[str]]:
    index: dict[tuple[str, str], set[str]] = defaultdict(set)
    for spec in specs.values():
        for reference in spec.source_refs:
            document = str(reference.get("document", ""))
            item = str(reference.get("item", ""))
            if document and item:
                index[(document, item)].add(spec.key)
    return index
