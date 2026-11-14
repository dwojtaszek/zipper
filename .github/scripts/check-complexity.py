#!/usr/bin/env python3
"""Diff-scoped complexity guard matching SonarCloud S3776 / ruff C901 limits."""
import argparse
import ast
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
CS_TEST_PREFIXES = ("src/Zipper.Tests/", "src/Zipper.Analyzers.Tests/")
CS_SIG_START_RE = re.compile(
    r"^\s*(?:(?:public|private|internal|protected|static|async|virtual|override|sealed|new|partial|extern|unsafe|abstract|readonly|ref|file)\s+)*"
    r"(?!\s*(?:if|for|foreach|while|switch|catch|using|lock|return|new|else|await|throw|yield)\b)"
    r"(?![^(\n]*\b(?:class|struct|record|interface|namespace)\s+[A-Za-z_]\w*\s*(?:<[^>]+>)?\s*\()"
    r"(?:\([^)]*\)\s+|[\w<>\[\]?.,\s]+?\s+)([A-Za-z_]\w*)\s*(?:<[^>]+>)?\s*\("
)
CS_UNTRACKED_PREFIXES = ("src/", ".github/", ".zipper-runner/", "tests/")
CS_NEST_RE = re.compile(r"\b(if|for|foreach|while|do|switch|catch)\b")
CS_FLAT_RE = re.compile(r"\belse\b|(?<=\s)\?(?=\s|$)|(?<=\))\?(?=[^:]*:)")
CS_BOOL_RE = re.compile(r"&&|\|\|")


def _changed_lines_from_git(diff_target: str) -> dict[str, set[int]]:
    changed: dict[str, set[int]] = {}
    res = subprocess.run(
        ["git", "diff", "-U0", "--no-renames", diff_target],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", check=False,
    )
    if res.returncode == 0:
        _parse_unified_diff(res.stdout, changed)
    untracked = subprocess.run(
        ["git", "ls-files", "--others", "--exclude-standard", "--", *CS_UNTRACKED_PREFIXES],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", check=False,
    )
    if untracked.returncode == 0:
        for rel in untracked.stdout.splitlines():
            p = ROOT / rel
            if p.is_file() and rel.endswith((".py", ".cs")):
                line_count = len(p.read_text(encoding="utf-8", errors="ignore").splitlines())
                changed.setdefault(rel, set()).update(range(1, line_count + 1))
    return changed


def _parse_unified_diff(diff_text: str, changed: dict[str, set[int]]) -> None:
    current: str | None = None
    for line in diff_text.splitlines():
        if line.startswith("+++ b/"):
            current = line[6:]
        elif line.startswith("@@ ") and current is not None:
            match = re.search(r"\+(\d+)(?:,(\d+))?", line)
            if match:
                start = int(match.group(1))
                count = int(match.group(2) or "1")
                if count > 0:
                    changed.setdefault(current, set()).update(range(start, start + count))


class _PyComplexityVisitor(ast.NodeVisitor):
    def __init__(self) -> None:
        self.score = 1

    def _bump(self, node: ast.AST) -> None:
        self.score += 1
        self.generic_visit(node)

    def _stop(self, _node: ast.AST) -> None:
        return None

    visit_FunctionDef = _stop
    visit_AsyncFunctionDef = _stop
    visit_ClassDef = _stop
    visit_If = _bump
    visit_For = _bump
    visit_AsyncFor = _bump
    visit_While = _bump
    visit_ExceptHandler = _bump
    visit_IfExp = _bump
    visit_match_case = _bump

    def visit_comprehension(self, node: ast.comprehension) -> None:
        self.score += 1 + len(node.ifs)
        self.generic_visit(node)


def _py_function_complexity(node: ast.AST) -> int:
    visitor = _PyComplexityVisitor()
    for child in ast.iter_child_nodes(node):
        if not isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            visitor.visit(child)
    return visitor.score


def _collect_py_functions(tree: ast.AST) -> list[tuple[str, ast.AST]]:
    collected: list[tuple[str, ast.AST]] = []

    def walk(node: ast.AST, prefix: tuple[str, ...]) -> None:
        for child in ast.iter_child_nodes(node):
            if isinstance(child, ast.ClassDef):
                walk(child, (*prefix, child.name))
            elif isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef)):
                qualname = ".".join((*prefix, child.name))
                collected.append((qualname, child))
                walk(child, (*prefix, child.name))
            else:
                walk(child, prefix)

    walk(tree, ())
    return collected


def _py_scores_by_name(source: str) -> dict[str, int]:
    tree = ast.parse(source)
    scores: dict[str, int] = {}
    for qualname, node in _collect_py_functions(tree):
        scores[qualname] = max(scores.get(qualname, 0), _py_function_complexity(node))
    return scores


class _PyNestedIfExpVisitor(ast.NodeVisitor):
    def __init__(self) -> None:
        self.count = 0
        self.lines: set[int] = set()

    def _stop(self, _node: ast.AST) -> None:
        return None

    visit_FunctionDef = _stop
    visit_AsyncFunctionDef = _stop
    visit_ClassDef = _stop

    def visit_IfExp(self, node: ast.IfExp) -> None:
        for child in (node.test, node.body, node.orelse):
            if _contains_ifexp(child):
                self.count += 1
                end_ln = getattr(node, "end_lineno", node.lineno) or node.lineno
                self.lines.update(range(node.lineno, end_ln + 1))
                break
        self.generic_visit(node)


def _contains_ifexp(node: ast.AST) -> bool:
    if isinstance(node, ast.IfExp):
        return True
    if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
        return False
    return any(_contains_ifexp(child) for child in ast.iter_child_nodes(node))


def _py_nested_ifexp_info(node: ast.AST) -> tuple[int, set[int]]:
    visitor = _PyNestedIfExpVisitor()
    for child in ast.iter_child_nodes(node):
        if not isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            visitor.visit(child)
    return visitor.count, visitor.lines


def _py_nested_counts(tree: ast.AST) -> dict[str, int]:
    counts: dict[str, int] = {"<module>": _py_nested_ifexp_info(tree)[0]}
    for qname, node in _collect_py_functions(tree):
        counts[qname] = counts.get(qname, 0) + _py_nested_ifexp_info(node)[0]
    return counts


def _check_nested_ternary(
    label: str,
    scope: str,
    node: ast.AST,
    touched_lines: set[int] | None,
    base_counts: dict[str, int],
) -> list[str]:
    count, lines = _py_nested_ifexp_info(node)
    if count <= base_counts.get(scope, 0):
        return []
    if touched_lines is not None and not (lines & touched_lines):
        return []
    return [f"{label}:{min(lines)}: Nested ternary expression (S3358) in '{scope}'"]


def check_python_source(
    label: str,
    source: str,
    touched_lines: set[int] | None,
    max_complexity: int,
    base_source: str | None = None,
) -> list[str]:
    tree = ast.parse(source, filename=label)
    base_scores = _py_scores_by_name(base_source) if base_source is not None else {}
    base_nested = _py_nested_counts(ast.parse(base_source)) if base_source is not None else {}
    findings = _check_nested_ternary(label, "<module>", tree, touched_lines, base_nested)
    for qualname, node in _collect_py_functions(tree):
        end_line = getattr(node, "end_lineno", node.lineno) or node.lineno
        if touched_lines is not None and not any(node.lineno <= ln <= end_line for ln in touched_lines):
            continue
        score = _py_function_complexity(node)
        prior = base_scores.get(qualname)
        if score > max_complexity and (prior is None or score > prior):
            findings.append(f"{label}:{node.lineno}: Python function '{qualname}' complexity {score} > {max_complexity}")
        findings.extend(_check_nested_ternary(label, qualname, node, touched_lines, base_nested))
    return findings


def check_python_file(path: Path, touched_lines: set[int] | None, max_complexity: int) -> list[str]:
    return check_python_source(str(path), path.read_text(encoding="utf-8"), touched_lines, max_complexity)


def _advance_quoted(line: str, i: int, quote: str) -> int:
    i += 1
    while i < len(line):
        if line[i] == "\\":
            i += 2
        elif line[i] == quote:
            return i + 1
        else:
            i += 1
    return i


def _advance_verbatim(line: str, i: int) -> tuple[int, bool]:
    while i < len(line):
        if line[i : i + 2] == '""':
            i += 2
        elif line[i] == '"':
            return i + 1, False
        else:
            i += 1
    return i, True


def _strip_cs_line(line: str, in_block: bool, in_verbatim: bool) -> tuple[str, bool, bool]:
    out: list[str] = []
    i = 0
    if in_verbatim:
        nxt, in_verbatim = _advance_verbatim(line, i)
        out.append(" " * nxt)
        i = nxt
    while i < len(line):
        if in_block:
            end = line.find("*/", i)
            if end < 0:
                return "".join(out), True, False
            in_block, i = False, end + 2
        elif line.startswith("//", i):
            break
        elif line.startswith("/*", i):
            in_block, i = True, i + 2
        elif line[i : i + 2] == '@"':
            nxt, in_verbatim = _advance_verbatim(line, i + 2)
            out.append(" " * (nxt - i))
            i = nxt
        elif line[i] in ('"', "'"):
            nxt = _advance_quoted(line, i, line[i])
            out.append(" " * (nxt - i))
            i = nxt
        else:
            out.append(line[i])
            i += 1
    return "".join(out), in_block, in_verbatim


def _cs_line_cognitive_delta(clean: str, depth: int) -> int:
    delta = 0
    if re.search(r"\belse\s+if\b", clean):
        delta += 1
        clean = re.sub(r"\belse\s+if\b", " ", clean)
    clean_no_dowhile = re.sub(r"\}\s*while\b", "}", clean)
    clean_no_null = re.sub(r"\?\?=|\?\?|\?\.|\?\[", " ", clean_no_dowhile)
    for _ in CS_NEST_RE.finditer(clean_no_null):
        delta += 1 + depth
    for match in CS_FLAT_RE.finditer(clean_no_null):
        delta += 1 if match.group(0) == "else" else (1 + depth)
    prev_op = None
    for match in CS_BOOL_RE.finditer(clean_no_null):
        op = match.group(0)
        if op != prev_op:
            delta += 1
            prev_op = op
    return delta


def _find_method_body_start(cleaned: list[str], sig_idx: int) -> tuple[str, int] | None:
    for offset in range(12):
        idx = sig_idx + offset
        if idx >= len(cleaned):
            return None
        line = cleaned[idx]
        if "=>" in line:
            return ("expr", idx)
        if ";" in line and "{" not in line:
            return None
        if "{" in line:
            return ("block", idx)
    return None


def _measure_expr_body(cleaned: list[str], body_idx: int) -> tuple[int, int]:
    score = _cs_line_cognitive_delta(cleaned[body_idx].split("=>", 1)[1], 0)
    j = body_idx
    while j < len(cleaned) and ";" not in cleaned[j]:
        j += 1
        if j < len(cleaned):
            score += _cs_line_cognitive_delta(cleaned[j], 0)
    return j + 1, score


def _update_brace_stack(brace_stack: list[bool], text: str, is_ctrl_open: bool) -> bool:
    for ch in text:
        if ch == "{":
            brace_stack.append(is_ctrl_open)
        elif ch == "}" and brace_stack:
            brace_stack.pop()
            if not brace_stack:
                return True
    return False


def _measure_method_body(cleaned: list[str], kind: str, body_idx: int) -> tuple[int, int]:
    if kind == "expr":
        return _measure_expr_body(cleaned, body_idx)
    brace_stack: list[bool] = []
    score = 0
    prev_ctrl = False
    for j in range(body_idx, len(cleaned)):
        line = cleaned[j]
        if j > body_idx:
            score += _cs_line_cognitive_delta(line, sum(1 for is_ctrl in brace_stack if is_ctrl))
        has_ctrl = j > body_idx and bool(CS_NEST_RE.search(line) or re.search(r"\belse\b", line))
        is_ctrl_open = has_ctrl or (line.strip().startswith("{") and prev_ctrl)
        start = line.find("{") if j == body_idx else 0
        if _update_brace_stack(brace_stack, line[start:], is_ctrl_open):
            return j + 1, score
        if line.strip():
            prev_ctrl = has_ctrl
    return len(cleaned), score


def _cs_method_identity(cleaned: list[str], idx: int, body_idx: int, container: str, name: str) -> str:
    sig = " ".join(" ".join(cleaned[idx:body_idx + 1]).split()).split("{", 1)[0].split("=>", 1)[0]
    start, end = sig.find("("), sig.rfind(")")
    raw_params = sig[start + 1 : end].strip() if 0 <= start < end else ""
    types: list[str] = []
    if raw_params:
        for param in raw_params.split(","):
            tokens = [t for t in param.split("=")[0].strip().split() if t not in ("ref", "out", "in", "params", "this")]
            if len(tokens) >= 2:
                types.append(" ".join(tokens[:-1]))
            elif tokens:
                types.append(tokens[0])
            else:
                types.append("")
    return f"{container}.{name}({','.join(types)})"


def _scan_csharp_methods(source: str) -> list[tuple[str, str, int, int, int]]:
    cleaned: list[str] = []
    in_block = False
    in_verbatim = False
    for raw in source.splitlines():
        clean, in_block, in_verbatim = _strip_cs_line(raw, in_block, in_verbatim)
        cleaned.append(clean)

    methods: list[tuple[str, str, int, int, int]] = []
    container = ""
    idx = 0
    while idx < len(cleaned):
        type_match = re.match(r"^\s*(?:[a-z]+\s+)*(?:class|struct|record|interface)\s+([A-Za-z_]\w*)", cleaned[idx])
        if type_match:
            container = type_match.group(1)
        match = CS_SIG_START_RE.match(cleaned[idx])
        body_info = _find_method_body_start(cleaned, idx) if match else None
        if not match or body_info is None:
            idx += 1
            continue
        kind, body_idx = body_info
        sig_key = _cs_method_identity(cleaned, idx, body_idx, container, match.group(1))
        end_idx, score = _measure_method_body(cleaned, kind, body_idx)
        methods.append((match.group(1), sig_key, idx + 1, end_idx, score))
        idx = end_idx
    return methods


def check_csharp_source(
    label: str,
    source: str,
    touched_lines: set[int] | None,
    max_complexity: int,
    base_source: str | None = None,
) -> list[str]:
    base_scores: dict[str, int] = {}
    if base_source is not None:
        for _name, sig_key, _s, _e, bscore in _scan_csharp_methods(base_source):
            base_scores[sig_key] = max(base_scores.get(sig_key, 0), bscore)
    findings: list[str] = []
    for name, sig_key, start_ln, end_ln, score in _scan_csharp_methods(source):
        if touched_lines is not None and not any(start_ln <= ln <= end_ln for ln in touched_lines):
            continue
        prior = base_scores.get(sig_key)
        if score > max_complexity and (prior is None or score > prior):
            findings.append(f"{label}:{start_ln}: C# method '{name}' cognitive complexity {score} > {max_complexity}")
    return findings


def check_csharp_file(path: Path, touched_lines: set[int] | None, max_complexity: int) -> list[str]:
    return check_csharp_source(str(path), path.read_text(encoding="utf-8", errors="ignore"), touched_lines, max_complexity)


def _read_git_blob(base_ref: str, rel: str) -> str | None:
    res = subprocess.run(
        ["git", "show", f"{base_ref}:{rel}"],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", check=False,
    )
    return res.stdout if res.returncode == 0 else None


def _resolve_base(base: str) -> str | None:
    for candidate in (base, f"origin/{base}", "HEAD~1", "HEAD"):
        res = subprocess.run(
            ["git", "rev-parse", "--verify", f"{candidate}^{{commit}}"],
            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", check=False,
        )
        if res.returncode == 0:
            mb = subprocess.run(
                ["git", "merge-base", candidate, "HEAD"],
                cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", check=False,
            )
            return mb.stdout.strip() if mb.returncode == 0 and mb.stdout.strip() else candidate
    return None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default=os.environ.get("DEV_BASE_REF", "main"))
    parser.add_argument("--files", help="Comma-separated repo-relative paths (scans full files)")
    parser.add_argument("--max-python", type=int, default=15)
    parser.add_argument("--max-csharp", type=int, default=15)
    args = parser.parse_args()

    resolved = _resolve_base(args.base)
    if args.files:
        file_map = {f.strip(): None for f in re.split(r"[,\n]+", args.files) if f.strip()}
    elif not resolved:
        return 0
    else:
        file_map = _changed_lines_from_git(resolved)

    findings: list[str] = []
    for rel, touched in sorted(file_map.items()):
        path = ROOT / rel
        if not path.is_file():
            continue
        base_src = _read_git_blob(resolved, rel) if resolved else None
        if rel.endswith(".py"):
            findings.extend(check_python_source(str(path), path.read_text(encoding="utf-8"), touched, args.max_python, base_src))
        elif rel.startswith("src/") and rel.endswith(".cs") and not rel.startswith(CS_TEST_PREFIXES):
            findings.extend(check_csharp_source(str(path), path.read_text(encoding="utf-8", errors="ignore"), touched, args.max_csharp, base_src))

    for item in findings:
        print(item, file=sys.stderr)
    return 1 if findings else 0


if __name__ == "__main__":
    raise SystemExit(main())
