#!/usr/bin/env python3
"""Summarize a Cobertura coverage report and enforce a minimum line coverage.

CI runs the tests with the code coverage collector that Microsoft.NET.Test.Sdk
already brings:

    dotnet test gramps-web-mcp.sln \\
        --collect "Code Coverage;Format=cobertura" \\
        --results-directory TestResults/coverage

and then this script, which prints a Markdown summary of one package, the
server (GrampsWeb.Mcp) by default, since the report covers the test project
too: line and branch coverage overall and by top-level folder, and the files
with the most lines no test runs. In GitHub Actions it also adds the summary
to the job's summary page.

Usage:
    python3 scripts/coverage-summary.py REPORT... [--package NAME]
        [--min-line PERCENT] [--top N]

A REPORT is a .cobertura.xml file or a directory; from a directory the script
takes the newest report, so earlier local runs left there do not count.
Several reports are merged. Compiler-generated classes (lambdas, async state
machines) repeat lines of their method; such a line counts once and is covered
when any of its entries is. Needs Python 3.9 or later and nothing else.

Exit code: 0 if line coverage is at least --min-line, 1 if below, 2 on error.
"""

from __future__ import annotations

import argparse
import math
import os
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

CONDITION_COVERAGE = re.compile(r"\((\d+)/(\d+)\)")
ROOT_FOLDER = "(root)"


class ReportError(Exception):
    pass


@dataclass
class Stats:
    lines: int = 0
    lines_covered: int = 0
    branches: int = 0
    branches_covered: int = 0

    def add(self, other: Stats) -> None:
        self.lines += other.lines
        self.lines_covered += other.lines_covered
        self.branches += other.branches
        self.branches_covered += other.branches_covered

    @property
    def uncovered(self) -> int:
        return self.lines - self.lines_covered


def percent(covered: int, total: int) -> str:
    # Rounded down, so a value just below the minimum never prints as equal to it.
    return f"{math.floor(covered * 1000 / total) / 10:.1f}%" if total else "–"


def find_reports(paths: list[str]) -> list[Path]:
    reports = []
    for value in paths:
        path = Path(value)
        if path.is_dir():
            found = sorted(path.rglob("*.cobertura.xml"), key=lambda p: p.stat().st_mtime)
            if not found:
                raise ReportError(f"no *.cobertura.xml report in {path}")
            reports.append(found[-1])
        elif path.is_file():
            reports.append(path)
        else:
            raise ReportError(f"{path} does not exist")
    return reports


def read_lines(reports: list[Path], package: str) -> dict[str, dict[int, tuple[bool, int, int]]]:
    """Return file -> line number -> (covered, branches, branches covered)."""
    files: dict[str, dict[int, tuple[bool, int, int]]] = {}
    seen_packages = set()
    for report in reports:
        root = ET.parse(report).getroot()
        for pkg in root.iter("package"):
            seen_packages.add(pkg.get("name"))
            if pkg.get("name") != package:
                continue
            for cls in pkg.iter("class"):
                lines = files.setdefault(cls.get("filename", ""), {})
                # A class lists its lines once more under each method; read the class's own list.
                for line in cls.findall("lines/line"):
                    number = int(line.get("number", "0"))
                    covered = int(line.get("hits", "0")) > 0
                    branches = branches_covered = 0
                    match = CONDITION_COVERAGE.search(line.get("condition-coverage", ""))
                    if line.get("branch") == "True" and match:
                        branches_covered, branches = int(match[1]), int(match[2])
                    old = lines.get(number)
                    if old:
                        covered = covered or old[0]
                        branches, branches_covered = max((branches, branches_covered), old[1:])
                    lines[number] = (covered, branches, branches_covered)
    if package not in seen_packages:
        names = ", ".join(sorted(n for n in seen_packages if n)) or "none"
        raise ReportError(f"no package {package} in the report; packages: {names}")
    return files


def file_stats(files: dict[str, dict[int, tuple[bool, int, int]]]) -> dict[str, Stats]:
    paths = [f for f in files if files[f]]
    base = os.path.commonpath([os.path.dirname(f) for f in paths]) if paths else ""
    result = {}
    for path in paths:
        stats = Stats()
        for covered, branches, branches_covered in files[path].values():
            stats.lines += 1
            stats.lines_covered += covered
            stats.branches += branches
            stats.branches_covered += branches_covered
        result[Path(os.path.relpath(path, base)).as_posix()] = stats
    return result


def summarize(package: str, by_file: dict[str, Stats], min_line: float | None, top: int) -> tuple[str, Stats]:
    total = Stats()
    by_folder: dict[str, Stats] = {}
    for path, stats in by_file.items():
        total.add(stats)
        folder = path.split("/", 1)[0] if "/" in path else ROOT_FOLDER
        by_folder.setdefault(folder, Stats()).add(stats)

    out = [f"## Test coverage: {package}", ""]
    headline = (
        f"Lines: **{percent(total.lines_covered, total.lines)}** "
        f"({total.lines_covered} of {total.lines}), "
        f"branches: **{percent(total.branches_covered, total.branches)}** "
        f"({total.branches_covered} of {total.branches})"
    )
    if min_line is not None:
        below = total.lines_covered * 100 < min_line * total.lines
        headline += f". Minimum for lines: {min_line:g}%{', **not met**' if below else ''}"
    out += [headline, ""]

    out += ["| Folder | Lines | Line coverage | Branch coverage |", "|---|--:|--:|--:|"]
    for folder, stats in sorted(by_folder.items(), key=lambda item: (-item[1].lines, item[0])):
        out.append(
            f"| {folder} | {stats.lines} | {percent(stats.lines_covered, stats.lines)} "
            f"| {percent(stats.branches_covered, stats.branches)} |"
        )
    out.append(
        f"| **Total** | **{total.lines}** | **{percent(total.lines_covered, total.lines)}** "
        f"| **{percent(total.branches_covered, total.branches)}** |"
    )

    least = sorted(
        ((path, stats) for path, stats in by_file.items() if stats.uncovered),
        key=lambda item: (-item[1].uncovered, item[0]),
    )[:top]
    if least:
        out += [
            "",
            "<details><summary>Files with the most lines no test runs</summary>",
            "",
            "| File | Lines not run | Line coverage |",
            "|---|--:|--:|",
        ]
        out += [
            f"| `{path}` | {stats.uncovered} of {stats.lines} | {percent(stats.lines_covered, stats.lines)} |"
            for path, stats in least
        ]
        out += ["", "</details>"]
    return "\n".join(out) + "\n", total


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    parser.add_argument("reports", nargs="+", help="a .cobertura.xml file or a directory to take the newest one from")
    parser.add_argument("--package", default="GrampsWeb.Mcp", help="package (assembly) to summarize")
    parser.add_argument("--min-line", type=float, help="fail when line coverage is below this percentage")
    parser.add_argument("--top", type=int, default=15, help="how many least covered files to list")
    args = parser.parse_args()

    try:
        reports = find_reports(args.reports)
        by_file = file_stats(read_lines(reports, args.package))
    except ET.ParseError as ex:
        print(f"error: cannot read the report: {ex}", file=sys.stderr)
        return 2
    except ReportError as ex:
        print(f"error: {ex}", file=sys.stderr)
        return 2

    summary, total = summarize(args.package, by_file, args.min_line, args.top)
    print("Reports: " + ", ".join(str(r) for r in reports), file=sys.stderr)
    print(summary)
    step_summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if step_summary:
        with open(step_summary, "a", encoding="utf-8") as f:
            f.write(summary)

    if args.min_line is not None and total.lines_covered * 100 < args.min_line * total.lines:
        message = (
            f"Line coverage of {args.package} is {percent(total.lines_covered, total.lines)}, "
            f"below the {args.min_line:g}% minimum"
        )
        prefix = "::error::" if os.environ.get("GITHUB_ACTIONS") == "true" else "error: "
        print(prefix + message, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
