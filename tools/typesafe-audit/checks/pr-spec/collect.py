#!/usr/bin/env python3
"""Deterministic PR evidence collection for the spec compliance side check (#958).

Pure functions only: everything here is exact string/path work with no network
and no model. The live collector (workflow) shells out to `gh` and feeds the
resulting JSON to run_check.py; tests and the evaluation corpus hand the same
shape in directly.

PR context JSON shape:
  {
    "pr": 966, "base": "<sha>", "head": "<sha>",
    "body": "<PR markdown>",
    "files": [{"path": "...", "additions": 3, "patch": "+ lines"}],
    "issues": {"927": {"number": 927, "body": "...",
               "comments": [{"order": 0, "author": "...", "body": "..."}]}}
  }
"""

from __future__ import annotations

import json
import re
from pathlib import Path

# Explicit closure keywords only (issue #958): never guess from branch names.
_ISSUE_REF = re.compile(r"\b([A-Za-z]+)\s+#(\d+)")
_CLOSURE_KEYWORDS = frozenset({
    "fixes", "fixed", "fix",
    "closes", "closed", "close", "closing",
    "resolved", "resolves", "resolve", "resolving",
})
_REQ_ID = re.compile(r"\bREQ-\d{3,4}\b")
_TSV_REF = re.compile(r"^(?:[^\t]*\t){2}([^\t]+)\t")

_GENERATED_PREFIXES = ("bin/", "obj/", "results/", "publish-bin/", ".worktrees/")
_BINARY_SUFFIXES = (
    ".dll", ".exe", ".zip", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".jar",
    ".bin", ".woff", ".woff2", ".ttf", ".pdf", ".7z", ".gz", ".tar",
)
_SECRET_FILES = (".env", )


def extract_issue_refs(text: str) -> list[int]:
    """Explicit `Fixes/Closes/Resolves #N` references, deduplicated, in order."""
    seen: list[int] = []
    for match in _ISSUE_REF.finditer(text or ""):
        if match.group(1).lower() in _CLOSURE_KEYWORDS:
            number = int(match.group(2))
            if number not in seen:
                seen.append(number)
    return seen


def filter_files(files: list[dict], max_files: int | None = None) -> list[dict]:
    """Drop binaries, generated artifacts, and secrets; enforce the file budget."""
    kept = []
    for entry in files or []:
        path = entry.get("path", "").replace("\\", "/")
        lowered = path.lower()
        if path.startswith(_GENERATED_PREFIXES) or "/bin/" in f"/{lowered}" or "/obj/" in f"/{lowered}":
            continue
        if lowered.endswith(_BINARY_SUFFIXES) or lowered in _SECRET_FILES or lowered.endswith(".pem") or lowered.endswith(".key"):
            continue
        kept.append(entry)
    limit = max_files
    return kept[:limit] if limit is not None else kept


def _tsv_refs_for_line(line: str) -> tuple[str, set[str]] | None:
    if not line or line.startswith("req_id") or "\t" not in line:
        return None
    row = line.split("\t")
    if len(row) < 4:
        return None
    unit_ref, e2e_ref = row[2], row[3]
    unit_file = unit_ref.split(".")[0] + ".cs" if "." in unit_ref else unit_ref
    e2e_file = e2e_ref.split(" ")[0] if " " in e2e_ref else e2e_ref
    return row[0].strip(), {unit_file, e2e_file}


def _req_ids_from_tsv(tsv_path: Path | None, changed_paths: set[str]) -> set[str]:
    if not tsv_path or not tsv_path.exists():
        return set()
    reqs: set[str] = set()
    for line in tsv_path.read_text(encoding="utf-8").splitlines():
        parsed = _tsv_refs_for_line(line)
        if parsed and (parsed[1] & changed_paths):
            reqs.add(parsed[0])
    return reqs


def affected_req_ids(
    files: list[dict],
    pr_text: str,
    issue_texts: dict[int, str],
    repo_root: Path | None = None,
) -> list[str]:
    """REQ IDs from changed lines, traceability rows for changed test paths,
    and explicit PR/issue references — deduplicated, sorted."""
    reqs: set[str] = set()
    changed_paths: set[str] = set()
    for entry in files or []:
        changed_paths.add(entry.get("path", ""))
        reqs.update(_REQ_ID.findall(entry.get("patch", "") or ""))
    reqs.update(_REQ_ID.findall(pr_text or ""))
    for text in issue_texts.values():
        reqs.update(_REQ_ID.findall(text or ""))

    tsv_path = repo_root / "tests" / "req-traceability.tsv" if repo_root else None
    reqs.update(_req_ids_from_tsv(tsv_path, changed_paths))
    return sorted(reqs)


def spec_evidence(issues: dict[dict] | dict, refs: list[int]) -> list[dict]:
    """Issue body + substantive comments in chronological order.

    The newest item of each issue is flagged `supersedes_older`: consumers ask
    the model to prefer it deterministically when texts conflict (issue #958
    acceptance criteria). `order` is a stable 0-based sequence across the whole
    evidence list.
    """
    evidence: list[dict] = []
    order = 0
    for ref in refs:
        issue = issues.get(str(ref)) or issues.get(ref)
        if not issue:
            continue
        evidence.append({"order": order, "source": f"issue #{ref} body", "text": issue.get("body", ""), "supersedes_older": False})
        order += 1
        comments = sorted(issue.get("comments", []), key=lambda c: c.get("order", 0))
        for index, comment in enumerate(comments):
            last = index == len(comments) - 1
            evidence.append({
                "order": order,
                "source": f"issue #{ref} comment by {comment.get('author', 'unknown')}",
                "text": comment.get("body", ""),
                "supersedes_older": last,
            })
            order += 1
    return evidence


def policy_evidence(repo_root: Path, per_file_bytes: int) -> list[dict]:
    """Supplied repository policy excerpts (issue #958 step 5). Deterministic
    head-of-file excerpts so deliberate documented patterns are visible to the
    model and cannot become findings by omission."""
    evidence = []
    for relative in ("AGENTS.md", "UBIQUITOUS_LANGUAGE.md", "docs/code-review-guidelines.md"):
        path = repo_root / relative
        if not path.exists():
            continue
        raw = path.read_bytes()[:per_file_bytes]
        evidence.append({"source": relative, "text": raw.decode("utf-8", errors="replace")})
    return evidence


def load_pr_json(path: Path) -> dict:
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)
