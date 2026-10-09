#!/usr/bin/env python3
"""Deterministic coverage-gap extraction from Cobertura XML (#959 Phase 1).

Parses coverlet's Cobertura output, maps uncovered lines/branches to enclosing
methods, drops generated code and trivial accessors, and attaches REQ IDs +
owning test names as evidence. TypeSafe never computes any of this.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

_REQ_ID = re.compile(r"\bREQ-\d{3,4}\b")
_TRIVIAL_METHODS = re.compile(r"^(get_|set_)[A-Z]")
_GENERATED_MARKERS = ("obj/", "bin/", "generated/", ".Designer.cs", ".AssemblyInfo.cs")
_BRANCH_RE = re.compile(r"\(\s*(\d+)\s*/\s*(\d+)\s*\)")


def _uncovered_branches(line: dict) -> int:
    """Extract uncovered branch count (total - covered) from a line's condition-coverage string."""
    condition = line.get("condition") or ""
    match = _BRANCH_RE.search(condition)
    if not match:
        return 0
    covered = int(match.group(1))
    total = int(match.group(2))
    return max(0, total - covered)


def parse_cobertura(path: Path) -> list[dict]:
    """Class-level method/line data from a Cobertura XML document."""
    root = ET.parse(path).getroot()
    classes = []
    for cls in root.iter("class"):
        filename = cls.get("filename")
        if not filename:
            continue
        methods = []
        for method in cls.iter("method"):
            lines = []
            for line in method.iter("line"):
                lines.append({
                    "number": int(line.get("number")),
                    "hits": int(line.get("hits", 0)),
                    "branch": line.get("branch", "").lower() == "true",
                    "condition": line.get("condition-coverage", ""),
                })
            methods.append({"name": method.get("name", ""), "lines": lines})
        classes.append({"filename": filename, "methods": methods})
    return classes


_REQ_LOOKBACK_LINES = 40
_METHOD_DECL = re.compile(r"(?:public|private|internal|protected)[^({;=<]*\b([A-Z]\w*(?:<[^\(>]+>)?)\s*\(")
_TYPE_KEYWORD = re.compile(r"\b(?:class|record|struct|interface|enum)\b")
_TOKEN_RE = re.compile(
    r'@"(?:""|[^"])*"'
    r'|"(?:\\.|[^"\\])*"'
    r"|'(?:\\.|[^'\\])*'"
    r'|/\*[\s\S]*?\*/'
    r'|//[^\r\n]*'
)


def _mask_match(m: re.Match) -> str:
    s = m.group(0)
    return "".join("\n" if c == "\n" else " " for c in s)


def _strip_comments_and_strings(text: str) -> list[str]:
    """Strip strings, char literals, and comments across lines while preserving line count."""
    cleaned = _TOKEN_RE.sub(_mask_match, text)
    return cleaned.split("\n")


def _clean_code_line(line: str) -> str:
    """Strip string literals and comments for structural brace analysis."""
    return _strip_comments_and_strings(line)[0]


def _comment_start_line(lines: list[str], decl_index: int) -> int:
    """Find start line of doc comments / attributes directly preceding decl_index (1-indexed)."""
    start = decl_index
    for i in range(decl_index - 2, max(-1, decl_index - _REQ_LOOKBACK_LINES - 2), -1):
        stripped = lines[i].strip()
        if not stripped:
            continue
        if stripped.startswith(("//", "/*", "*", "///", "[", "*/")):
            start = i + 1
        else:
            break
    return start


def _check_method_end(clean: str, depth: int, decl_depth: int, method_started: bool) -> tuple[bool, bool]:
    """Determine whether the current method has closed and whether body opened."""
    if not method_started:
        if ";" in clean and "{" not in clean:
            return True, False
        if "{" in clean:
            return clean.count("{") <= clean.count("}"), clean.count("{") > clean.count("}")
        return False, False
    return depth <= decl_depth, method_started


def _find_method_spans(lines: list[str]) -> list[tuple[int, int]]:
    """Identify (comment_start_line, end_line) ranges for C# methods."""
    clean_lines = _strip_comments_and_strings("\n".join(lines))
    methods: list[tuple[int, int]] = []
    depth = 0
    in_method = False
    method_started = False
    decl_depth = 0
    current_start = 1

    for idx, (line, clean) in enumerate(zip(lines, clean_lines), start=1):
        match = _METHOD_DECL.search(clean)
        if not in_method and depth >= 1 and match and not _TYPE_KEYWORD.search(clean[:match.start(1)]):
            current_start = _comment_start_line(lines, idx)
            in_method = True
            method_started = False
            decl_depth = depth

        depth += clean.count("{") - clean.count("}")

        if in_method:
            closed, method_started = _check_method_end(clean, depth, decl_depth, method_started)
            if closed:
                methods.append((current_start, idx))
                in_method = False

    if in_method:
        methods.append((current_start, len(lines)))

    return methods


def extract_method_reqs(source: Path, start_line: int, end_line: int | None = None) -> set[str]:
    """Scan enclosing method text (including preceding doc comments) for REQ IDs."""
    if end_line is None:
        end_line = start_line
    try:
        lines = source.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return set()
    if not lines:
        return set()

    spans = _find_method_spans(lines)
    for m_start, m_end in spans:
        if m_start <= start_line <= m_end:
            text = "\n".join(lines[m_start - 1 : m_end])
            return set(_REQ_ID.findall(text))

    # Fallback to local window if outside detected method
    w_start = max(0, start_line - _REQ_LOOKBACK_LINES - 1)
    prior_ends = [e for _, e in spans if e < start_line]
    if prior_ends:
        w_start = max(w_start, max(prior_ends))
    w_end = min(len(lines), max(end_line, start_line))
    text = "\n".join(lines[w_start:w_end])
    return set(_REQ_ID.findall(text))


def _extract_relative_path(cleaned: str, repo_root: Path) -> str:
    p = Path(cleaned)
    if p.is_absolute():
        for root, prefix in ((repo_root, ""), (repo_root / "src", "src/")):
            try:
                rel = p.resolve().relative_to(root.resolve())
                return f"{prefix}{rel.as_posix()}"
            except (ValueError, OSError):
                continue

    idx = cleaned.rfind("/src/")
    if idx != -1:
        return cleaned[idx + 1:]
    if cleaned.startswith("src/"):
        return cleaned
    if cleaned.startswith("src\\"):
        return cleaned.replace("\\", "/")
    if cleaned.startswith("../src/"):
        return cleaned[3:]
    if re.match(r"^[A-Za-z]:/src/", cleaned):
        return cleaned[3:]
    if re.match(r"^[A-Za-z]:/", cleaned):
        return os.path.normpath(cleaned[3:]).replace("\\", "/")
    return os.path.normpath(cleaned).replace("\\", "/")


def normalize_file_path(raw_filename: str, repo_root: Path) -> tuple[str, str]:
    """Returns (file_entry, rel_to_src).

    file_entry: repo-relative path starting with 'src/', e.g. 'src/LoadFiles/Foo.cs'
    rel_to_src: path relative to src/, e.g. 'LoadFiles/Foo.cs'
    """
    cleaned = raw_filename.replace("\\", "/")
    norm = _extract_relative_path(cleaned, repo_root)
    if norm == ".":
        norm = ""

    if norm.startswith("src/"):
        return norm, norm[4:]
    if norm == "src":
        return "src", ""
    if norm.startswith(("../", "/")):
        return norm, norm
    return f"src/{norm}" if norm else "src", norm


def _scope_root(source_root: Path) -> Path | None:
    try:
        resolved = source_root.resolve()
        if resolved.name == "src":
            return resolved
        if (resolved / "src").is_dir():
            return (resolved / "src").resolve()
        return resolved
    except OSError:
        return None


def _candidate_source_paths(source_root: Path, cleaned: str) -> list[Path]:
    p = Path(cleaned)
    if p.is_absolute():
        return [p]
    norm = os.path.normpath(cleaned).replace("\\", "/")
    extra = norm[4:] if norm.startswith("src/") else f"src/{norm}"
    return [source_root / norm, source_root / extra]


def resolve_source_file(source_root: Path, file_or_rel: str) -> Path | None:
    """Safely resolve an existing C# source file within repository source scope.

    source_root points to the source directory (or repository root containing src/).
    file_or_rel may be repository-relative ('src/Foo.cs') or source-relative ('Foo.cs'),
    with forward slashes or Windows backslashes.
    Returns the resolved Path if the file exists, has a .cs suffix, and is within
    the approved source scope; otherwise returns None.
    """
    scope = _scope_root(source_root)
    if scope is None:
        return None

    cleaned = file_or_rel.replace("\\", "/")
    for cand in _candidate_source_paths(source_root, cleaned):
        try:
            resolved = cand.resolve()
            if resolved.is_relative_to(scope) and resolved.suffix.lower() == ".cs" and resolved.is_file():
                return resolved
        except (ValueError, OSError):
            continue
    return None


def method_for_line(source_root: Path, filename: str, line_number: int) -> str:
    """Best-effort enclosing method name by scanning the C# source text."""
    source = resolve_source_file(source_root, filename)
    if source is None:
        return ""
    depth = 0
    name = ""
    try:
        text = source.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""
    lines = text.splitlines()
    clean_lines = _strip_comments_and_strings("\n".join(lines))
    for index, (line, clean) in enumerate(zip(lines, clean_lines), start=1):
        if index > line_number:
            break
        match = _METHOD_DECL.search(clean)
        # Method declarations sit inside a type body (depth >= 1); the same
        # regex cannot match at depth 0 (namespace/file scope).
        if match and depth >= 1 and not _TYPE_KEYWORD.search(clean[:match.start(1)]):
            name = match.group(1)
        depth += clean.count("{") - clean.count("}")
    return name


def hash_source(source_file: Path | None) -> str:
    """Safely compute SHA-256 of source file, returning empty string on missing file or OSError."""
    if source_file is None:
        return ""
    try:
        return hashlib.sha256(source_file.read_bytes()).hexdigest()
    except OSError:
        return ""


def traceability_rows(repo_root: Path) -> list[dict]:
    path = repo_root / "tests" / "req-traceability.tsv"
    rows = []
    if not path.exists():
        return rows
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("req_id") or "\t" not in line:
            continue
        cells = line.split("\t")
        if len(cells) >= 4:
            rows.append({"req_id": cells[0].strip(), "reference": cells[2].strip(), "coverage": cells[1].strip()})
    return rows


def attach_req_evidence(
    gap: dict,
    source_root: Path,
    rows: list[dict],
    source_file: Path | None = None,
) -> None:
    """REQ IDs from the method source text + owning test names from the TSV."""
    if source_file is None:
        source_file = resolve_source_file(source_root, gap.get("file", ""))
    reqs: set[str] = set()
    if source_file is not None:
        first_line = gap.get("first_line", 1)
        last_line = gap.get("last_line", first_line)
        reqs.update(extract_method_reqs(source_file, first_line, last_line))
    class_name = Path(gap.get("file", "")).stem
    tests = []
    if class_name:
        for row in rows:
            if row["reference"].split(".")[0].startswith(class_name):
                reqs.add(row["req_id"])
                tests.append(row["reference"])
    gap["req_ids"] = sorted(reqs)
    gap["tests"] = sorted(set(tests))


def extract_gaps(cobertura_path: Path, repo_root: Path, max_gaps: int | None = None) -> list[dict]:
    """Uncovered line/branch gaps per non-excluded method."""
    source_root = repo_root / "src"
    rows = traceability_rows(repo_root)
    gaps: list[dict] = []
    for cls in parse_cobertura(cobertura_path):
        raw_filename = cls["filename"]
        file_entry, rel_to_src = normalize_file_path(raw_filename, repo_root)
        if any(marker in file_entry for marker in _GENERATED_MARKERS) or any(marker in rel_to_src for marker in _GENERATED_MARKERS):
            continue
        for method in cls["methods"]:
            name = method["name"]
            if _TRIVIAL_METHODS.match(name):
                continue
            uncovered_lines = 0
            uncovered_branches = 0
            gap_lines: list[dict] = []
            for line in method["lines"]:
                branches = _uncovered_branches(line)
                is_uncovered = line["hits"] == 0
                if is_uncovered:
                    uncovered_lines += 1
                if branches > 0:
                    uncovered_branches += branches
                if is_uncovered or branches > 0:
                    gap_lines.append(line)
            if not gap_lines:
                continue
            valid_gaps = [l for l in gap_lines if l["number"] > 0]
            gap_lines_sorted = sorted(valid_gaps or gap_lines, key=lambda l: l["number"])
            resolved = method_for_line(source_root, rel_to_src, gap_lines_sorted[0]["number"])
            source_file = resolve_source_file(source_root, rel_to_src)
            gap = {
                "origin": "coverage",
                "file": file_entry,
                "method": resolved or name,
                "first_line": gap_lines_sorted[0]["number"],
                "last_line": gap_lines_sorted[-1]["number"],
                "uncovered_lines": uncovered_lines,
                "uncovered_branches": uncovered_branches,
            }
            attach_req_evidence(gap, source_root, rows, source_file=source_file)
            gap["source_sha256"] = hash_source(source_file)
            gaps.append(gap)
    ordered = sorted(gaps, key=lambda g: (g["file"], g["first_line"]))
    return ordered[:max_gaps] if max_gaps is not None else ordered


def load_json(path: Path) -> dict:
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)
