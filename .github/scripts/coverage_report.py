"""Builds the markdown coverage comparison that the build workflow posts on a pull request."""

import argparse
import glob
import os
import re
import xml.etree.ElementTree as ElementTree

MARKER = "<!-- capybarahome-coverage -->"
CONDITION = re.compile(r"\((\d+)/(\d+)\)")
MAX_FILE_ROWS = 25


class Counts:
    """Covered and total counts for one unit of coverage."""

    def __init__(self):
        self.lines_covered = 0
        self.lines_total = 0
        self.branches_covered = 0
        self.branches_total = 0

    def add(self, other):
        """Folds another unit's counts into this one."""
        self.lines_covered += other.lines_covered
        self.lines_total += other.lines_total
        self.branches_covered += other.branches_covered
        self.branches_total += other.branches_total

    @property
    def line_rate(self):
        """Line coverage as a percentage, or None when there is nothing to cover."""
        return percentage(self.lines_covered, self.lines_total)

    @property
    def branch_rate(self):
        """Branch coverage as a percentage, or None when there are no branches."""
        return percentage(self.branches_covered, self.branches_total)


def percentage(covered, total):
    """Returns covered/total as a percentage, or None when total is zero."""
    return None if total == 0 else covered * 100.0 / total


def read_report(path, lines, branches, assembly_of):
    """Folds one cobertura file into the per-line and per-branch maps."""
    root = ElementTree.parse(path).getroot()
    for package in root.iter("package"):
        assembly = package.get("name") or "(unnamed)"
        for class_element in package.iter("class"):
            filename = (class_element.get("filename") or "").replace("\\", "/")
            assembly_of.setdefault(filename, assembly)
            for line in class_element.iter("line"):
                number = int(line.get("number", "0"))
                hits = int(line.get("hits", "0"))
                key = (filename, number)
                lines[key] = lines.get(key, 0) + hits
                if (line.get("branch") or "").lower() != "true":
                    continue
                match = CONDITION.search(line.get("condition-coverage") or "")
                if not match:
                    continue
                covered, total = int(match.group(1)), int(match.group(2))
                previous = branches.get(key, (0, 0))
                branches[key] = (max(previous[0], covered), max(previous[1], total))


def read_directory(directory):
    """Reads every cobertura file under a directory into per-file and total counts."""
    lines, branches, assembly_of = {}, {}, {}
    reports = sorted(glob.glob(os.path.join(directory, "**", "*.cobertura.xml"), recursive=True))
    for report in reports:
        read_report(report, lines, branches, assembly_of)

    files, assemblies, total = {}, {}, Counts()
    for (filename, _), hits in lines.items():
        counts = files.setdefault(filename, Counts())
        counts.lines_total += 1
        counts.lines_covered += 1 if hits > 0 else 0
    for (filename, _), (covered, branch_total) in branches.items():
        counts = files.setdefault(filename, Counts())
        counts.branches_total += branch_total
        counts.branches_covered += covered
    for filename, counts in files.items():
        assemblies.setdefault(assembly_of.get(filename, "(unnamed)"), Counts()).add(counts)
        total.add(counts)
    return {"reports": len(reports), "files": files, "assemblies": assemblies, "total": total}


def rate_cell(counts, kind):
    """Formats one coverage rate as a table cell."""
    if kind == "line":
        rate, covered, total = counts.line_rate, counts.lines_covered, counts.lines_total
    else:
        rate, covered, total = counts.branch_rate, counts.branches_covered, counts.branches_total
    if rate is None:
        return "n/a"
    return f"{rate:.2f}% ({covered}/{total})"


def delta_cell(base, head, kind):
    """Formats the change between two coverage rates in percentage points."""
    base_rate = None if base is None else (base.line_rate if kind == "line" else base.branch_rate)
    head_rate = None if head is None else (head.line_rate if kind == "line" else head.branch_rate)
    if head_rate is None or base_rate is None:
        return "—"
    difference = head_rate - base_rate
    if abs(difference) < 0.005:
        return "no change"
    return f"{difference:+.2f} pp"


def overall_table(base, head):
    """Renders the line and branch totals for the base branch and the pull request."""
    base_total = None if base is None else base["total"]
    rows = ["| | Base | This PR | Change |", "| --- | --- | --- | --- |"]
    for kind, label in (("line", "Lines"), ("branch", "Branches")):
        base_cell = "n/a" if base_total is None else rate_cell(base_total, kind)
        rows.append(
            f"| {label} | {base_cell} | {rate_cell(head['total'], kind)} "
            f"| {delta_cell(base_total, head['total'], kind)} |"
        )
    return "\n".join(rows)


def assembly_table(base, head):
    """Renders per-assembly line and branch coverage, worst change first."""
    base_assemblies = {} if base is None else base["assemblies"]
    names = sorted(set(head["assemblies"]) | set(base_assemblies))
    rows = [
        "| Assembly | Lines (base) | Lines (PR) | Change | Branches (PR) | Change |",
        "| --- | --- | --- | --- | --- | --- |",
    ]
    for name in names:
        head_counts = head["assemblies"].get(name)
        base_counts = base_assemblies.get(name)
        rows.append(
            f"| `{name}` "
            f"| {'new' if base_counts is None else rate_cell(base_counts, 'line')} "
            f"| {'gone' if head_counts is None else rate_cell(head_counts, 'line')} "
            f"| {delta_cell(base_counts, head_counts, 'line')} "
            f"| {'gone' if head_counts is None else rate_cell(head_counts, 'branch')} "
            f"| {delta_cell(base_counts, head_counts, 'branch')} |"
        )
    return "\n".join(rows)


def changed_files(base, head):
    """Renders the files whose line coverage differs from the base branch."""
    if base is None:
        return None
    changes = []
    for filename in sorted(set(head["files"]) | set(base["files"])):
        head_counts = head["files"].get(filename)
        base_counts = base["files"].get(filename)
        head_rate = None if head_counts is None else head_counts.line_rate
        base_rate = None if base_counts is None else base_counts.line_rate
        if head_rate is None or base_rate is None:
            changes.append((-100.0, filename, base_counts, head_counts))
        elif abs(head_rate - base_rate) >= 0.005:
            changes.append((head_rate - base_rate, filename, base_counts, head_counts))
    if not changes:
        return None

    changes.sort(key=lambda change: (change[0], change[1]))
    rows = ["| File | Lines (base) | Lines (PR) | Change |", "| --- | --- | --- | --- |"]
    for _, filename, base_counts, head_counts in changes[:MAX_FILE_ROWS]:
        rows.append(
            f"| `{short_name(filename)}` "
            f"| {'new file' if base_counts is None else rate_cell(base_counts, 'line')} "
            f"| {'gone' if head_counts is None else rate_cell(head_counts, 'line')} "
            f"| {delta_cell(base_counts, head_counts, 'line')} |"
        )
    if len(changes) > MAX_FILE_ROWS:
        rows.append("")
        rows.append(f"…and {len(changes) - MAX_FILE_ROWS} more file(s).")
    return "\n".join(rows)


def short_name(filename):
    """Trims the checkout path off a source file name so the table stays narrow."""
    for marker in ("/src/", "/tests/"):
        index = filename.find(marker)
        if index >= 0:
            return filename[index + 1:]
    return filename


def build(base, head, base_label):
    """Assembles the whole comment body."""
    parts = [MARKER, "### Test coverage", ""]
    if base is None:
        parts.append(
            f"No coverage report was found for `{base_label}`, so this run has nothing to "
            "compare against. The next push to that branch will record one."
        )
        parts.append("")
    parts.append(overall_table(base, head))
    parts.append("")
    parts.append("<details>")
    parts.append("<summary>By assembly</summary>")
    parts.append("")
    parts.append(assembly_table(base, head))
    parts.append("")
    parts.append("</details>")
    files = changed_files(base, head)
    if files:
        parts.append("")
        parts.append("<details>")
        parts.append("<summary>Files whose line coverage changed</summary>")
        parts.append("")
        parts.append(files)
        parts.append("")
        parts.append("</details>")
    parts.append("")
    parts.append(
        f"Collected with coverlet via `dotnet test`. Base is the latest successful "
        f"`{base_label}` build."
    )
    return "\n".join(parts) + "\n"


def main():
    """Writes the comparison comment, and the totals for the job summary."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--head", required=True)
    parser.add_argument("--base")
    parser.add_argument("--base-label", default="main")
    parser.add_argument("--out", required=True)
    arguments = parser.parse_args()

    head = read_directory(arguments.head)
    if head["reports"] == 0:
        raise SystemExit(f"No cobertura reports found under {arguments.head}")
    base = None
    if arguments.base and os.path.isdir(arguments.base):
        candidate = read_directory(arguments.base)
        base = candidate if candidate["reports"] else None

    with open(arguments.out, "w", encoding="utf-8") as handle:
        handle.write(build(base, head, arguments.base_label))
    rate = head["total"].line_rate
    print(f"line coverage {rate:.2f}%" if rate is not None else "line coverage n/a")


if __name__ == "__main__":
    main()
