from __future__ import annotations

import re
from pathlib import Path

from .errors import ValidationError
from .models import SourceItem, SourceScan
from .util import normalize_text, sha256_text


HEADING_RE = re.compile(r"^(#{1,6})\s+(.+?)\s*$")
TABLE_ID_RE = re.compile(
    r"^\|\s*`?([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+)`?\s*\|"
)
AC_RE = re.compile(
    r"^\*\*([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+)[：:]\s*(.+?)\*\*\s*$"
)


def _normalize_table_row(row: str) -> str:
    cells = [re.sub(r"\s+", " ", cell.strip()) for cell in row.strip().strip("|").split("|")]
    return " | ".join(cells)


def scan_openlims_prd(document_id: str, relative_path: str, path: Path) -> SourceScan:
    raw = normalize_text(path.read_text(encoding="utf-8-sig"))
    lines = raw.split("\n")
    items: dict[str, SourceItem] = {}
    current_section = ""
    errors: list[str] = []
    index = 0

    while index < len(lines):
        line = lines[index]
        heading = HEADING_RE.match(line)
        if heading:
            current_section = heading.group(2).strip()

        table = TABLE_ID_RE.match(line)
        if table:
            item_id = table.group(1)
            normalized = _normalize_table_row(line)
            cells = normalized.split(" | ")
            title = cells[1] if len(cells) > 1 else item_id
            item = SourceItem(
                id=item_id,
                title=title,
                content=normalized,
                section=current_section,
                line=index + 1,
                kind="table-row",
            )
            if item_id in items:
                errors.append(
                    f"{relative_path}:{index + 1} 出现重复来源 ID {item_id}；首次位于第 {items[item_id].line} 行"
                )
            else:
                items[item_id] = item
            index += 1
            continue

        acceptance = AC_RE.match(line)
        if acceptance:
            item_id = acceptance.group(1)
            title = acceptance.group(2).strip()
            body_lines: list[str] = []
            cursor = index + 1
            while cursor < len(lines):
                candidate = lines[cursor].strip()
                if not candidate:
                    break
                if HEADING_RE.match(lines[cursor]) or AC_RE.match(lines[cursor]):
                    break
                body_lines.append(re.sub(r"\s+", " ", candidate))
                cursor += 1
            content = title
            if body_lines:
                content += "\n" + " ".join(body_lines)
            item = SourceItem(
                id=item_id,
                title=title,
                content=content,
                section=current_section,
                line=index + 1,
                kind="acceptance-block",
            )
            if item_id in items:
                errors.append(
                    f"{relative_path}:{index + 1} 出现重复来源 ID {item_id}；首次位于第 {items[item_id].line} 行"
                )
            else:
                items[item_id] = item
            index = max(cursor, index + 1)
            continue

        index += 1

    if errors:
        raise ValidationError(errors)
    return SourceScan(
        id=document_id,
        path=relative_path,
        format="openlims-prd-markdown",
        document_hash=sha256_text(raw),
        items=items,
    )


def scan_document(document_id: str, relative_path: str, format_name: str, path: Path) -> SourceScan:
    if format_name == "openlims-prd-markdown":
        return scan_openlims_prd(document_id, relative_path, path)
    raise ValidationError([f"不支持的来源格式：{format_name}（{relative_path}）"])
