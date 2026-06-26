#!/usr/bin/env python3
"""Add MCP tool annotations (Title, ReadOnly, Destructive) to GrampsWeb.Mcp tool files."""

from __future__ import annotations

import re
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parent.parent / "GrampsWeb.Mcp" / "Tools"

# PascalCase method name -> human title
def to_title(name: str) -> str:
    words = re.sub(r"([a-z])([A-Z])", r"\1 \2", name).split()
    return " ".join(w.lower() if w in {"by", "to", "and", "or", "of", "in", "for"} else w.capitalize() for w in words)


def classify(name: str) -> tuple[bool, bool]:
    """Return (read_only, destructive)."""
    if name.startswith("Get") or name.startswith("Search") or name.startswith("List") or name == "FindByGrampsId":
        return True, False
    if name.startswith("Delete"):
        return False, True
    if name.startswith("Create") or name.startswith("Update") or name.startswith("Quick") or name.startswith("Add"):
        return False, False
    raise ValueError(f"Cannot classify tool: {name}")


PATTERN = re.compile(
    r"(\s+)\[McpServerTool\]\r?\n(\s+)\[Description\(",
    re.MULTILINE,
)


def process_file(path: Path) -> int:
    text = path.read_text(encoding="utf-8")
    count = 0

    def replacer(match: re.Match[str]) -> str:
        nonlocal count
        indent = match.group(1)
        desc_indent = match.group(2)
        # Find method name after the Description block closes
        start = match.end()
        method_match = re.search(
            r"public\s+static\s+(?:async\s+)?(?:Task<[^>]+>|Task|string|ImageContentBlock)\s+(\w+)\s*\(",
            text[start : start + 2000],
        )
        if not method_match:
            return match.group(0)
        name = method_match.group(1)
        read_only, destructive = classify(name)
        title = to_title(name)
        count += 1
        return (
            f'{indent}[McpServerTool(Title = "{title}", ReadOnly = {str(read_only).lower()}, Destructive = {str(destructive).lower()})]\n'
            f"{desc_indent}[Description("
        )

    new_text = PATTERN.sub(replacer, text)
    if new_text != text:
        path.write_text(new_text, encoding="utf-8")
    return count


def main() -> None:
    total = 0
    for path in sorted(TOOLS_DIR.glob("*.cs")):
        if path.name in {"ToolDescriptionFragments.cs", "McpToolErrors.cs", "NotFoundHelper.cs", "DeleteHelper.cs",
                          "BacklinkCollector.cs", "PlaceTimelineFilters.cs", "PlaceTimelineFallback.cs",
                          "PersonTreeTraversal.cs"}:
            continue
        n = process_file(path)
        if n:
            print(f"{path.name}: {n} tools annotated")
            total += n
    print(f"Total: {total} tools annotated")


if __name__ == "__main__":
    main()
