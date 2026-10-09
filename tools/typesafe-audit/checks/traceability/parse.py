#!/usr/bin/env python3
"""Deterministic parsing/resolution for the semantic traceability audit (#957).

Resolves `unit` references (`Class.Method`) to exact test method bodies and
`e2e` references (`script.sh Scenario`) to exact scenario blocks. Unresolved,
malformed, or ambiguous references raise ParseError — the caller must fail
deterministically BEFORE any model call.
"""

from __future__ import annotations

import hashlib
import re
from pathlib import Path

TSV_HEADER = ("req_id", "coverage", "reference", "notes")
VALID_COVERAGES = {"unit", "e2e", "exemption"}


class ParseError(Exception):
    """Deterministic input problem: fail before any TypeSafe call."""


def sha256_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def parse_tsv(path: Path) -> list[dict]:
    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines:
        raise ParseError(f"{path} is empty.")
    header = tuple(col.strip().lower() for col in lines[0].split("\t"))
    if header != TSV_HEADER:
        raise ParseError(f"{path} header must be {TSV_HEADER}, got {header}.")
    rows = []
    for line_no, line in enumerate(lines[1:], start=2):
        if not line.strip():
            continue
        cols = line.split("\t")
        if len(cols) != 4:
            raise ParseError(f"{path}:{line_no}: expected 4 tab-separated columns, got {len(cols)}.")
        row = {
            "req_id": cols[0].strip(),
            "coverage": cols[1].strip().lower(),
            "reference": cols[2].strip(),
            "notes": cols[3].strip(),
            "line_no": line_no,
        }
        if not re.fullmatch(r"REQ-\d+", row["req_id"]):
            raise ParseError(f"{path}:{line_no}: malformed REQ id '{row['req_id']}'.")
        if row["coverage"] not in VALID_COVERAGES:
            raise ParseError(f"{path}:{line_no}: coverage must be one of {sorted(VALID_COVERAGES)}, got '{row['coverage']}'.")
        if row["coverage"] == "exemption":
            if row["reference"] not in ("-", ""):
                raise ParseError(f"{path}:{line_no}: exemption rows must use reference '-'.")
            if not row["notes"]:
                raise ParseError(f"{path}:{line_no}: exemption rows require a rationale in notes.")
        elif row["reference"] in ("-", ""):
            raise ParseError(f"{path}:{line_no}: {row['coverage']} rows require a reference.")
        rows.append(row)
    return rows


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


class Token:
    __slots__ = ("kind", "val", "line", "end_line", "start_pos", "end_pos")

    def __init__(self, kind: str, val: str, line: int, end_line: int, start_pos: int, end_pos: int):
        self.kind = kind
        self.val = val
        self.line = line
        self.end_line = end_line
        self.start_pos = start_pos
        self.end_pos = end_pos

    def __repr__(self) -> str:
        return f"Token({self.kind}, {self.val!r}, line={self.line})"


CONTROL_FLOW_KEYWORDS = {
    "if", "else", "while", "for", "foreach", "do", "switch", "case",
    "default", "catch", "finally", "try", "using", "lock", "fixed",
    "return", "throw", "break", "continue", "goto", "yield",
    "class", "struct", "record", "interface", "enum", "namespace",
    "new", "get", "set", "init", "add", "remove",
}

MODIFIERS = {
    "public", "private", "protected", "internal", "static", "virtual",
    "override", "abstract", "sealed", "async", "extern", "unsafe", "new", "partial", "readonly"
}


def _normalize_name(name: str) -> str:
    return name.lstrip("@")


def _names_match(a: str, b: str) -> bool:
    return a == b or _normalize_name(a) == _normalize_name(b)


def _qualified_names_match(spec: str, full_name: str) -> bool:
    spec_parts = spec.split(".")
    full_parts = full_name.split(".")
    if len(spec_parts) > len(full_parts):
        return False
    for s, f in zip(reversed(spec_parts), reversed(full_parts)):
        if not _names_match(s, f):
            return False
    return True


def tokenize_cs(text: str, path: Path | str = "") -> list[Token]:
    newline_offsets = [0]
    for m in re.finditer(r"\n", text):
        newline_offsets.append(m.end())

    def get_line(offset: int) -> int:
        from bisect import bisect_right
        return bisect_right(newline_offsets, offset)

    def scan_literal_or_comment(p: int) -> int | None:
        if text.startswith("//", p):
            next_nl = text.find("\n", p + 2)
            return n if next_nl == -1 else next_nl + 1

        if text.startswith("/*", p):
            end_c = text.find("*/", p + 2)
            if end_c == -1:
                raise ParseError(f"{path}:{get_line(p)}: unterminated multi-line comment.")
            return end_c + 2

        line_start = text.rfind("\n", 0, p) + 1
        if text[line_start:p].strip() == "" and text[p] == "#":
            next_nl = text.find("\n", p + 1)
            return n if next_nl == -1 else next_nl + 1

        if text[p] == "'":
            q = p + 1
            while q < n:
                if text[q] == "\\":
                    q += 2
                elif text[q] == "'":
                    return q + 1
                elif text[q] == "\n":
                    raise ParseError(f"{path}:{get_line(p)}: newline in character literal.")
                else:
                    q += 1
            raise ParseError(f"{path}:{get_line(p)}: unterminated character literal.")

        dollars = 0
        while p + dollars < n and text[p + dollars] == "$":
            dollars += 1
        q_start = p + dollars
        quotes = 0
        while q_start + quotes < n and text[q_start + quotes] == '"':
            quotes += 1

        if quotes >= 3:
            closing = '"' * quotes
            sp = q_start + quotes
            if dollars == 0:
                while True:
                    idx = text.find(closing, sp)
                    if idx == -1:
                        raise ParseError(f"{path}:{get_line(p)}: unterminated raw string literal.")
                    extra = 0
                    while idx + quotes + extra < n and text[idx + quotes + extra] == '"':
                        extra += 1
                    if extra == 0:
                        return idx + quotes
                    else:
                        sp = idx + quotes + extra
            else:
                sub_open = "{" * dollars
                sub_close = "}" * dollars
                while sp < n:
                    if text.startswith(closing, sp):
                        extra = 0
                        while sp + quotes + extra < n and text[sp + quotes + extra] == '"':
                            extra += 1
                        if extra == 0:
                            return sp + quotes
                        else:
                            sp += quotes + extra
                            continue
                    elif text.startswith(sub_open, sp):
                        sp = scan_interpolated_expr(sp + dollars, sub_close)
                    else:
                        sp += 1
                raise ParseError(f"{path}:{get_line(p)}: unterminated raw string literal.")

        if text.startswith('@"', p):
            sp = p + 2
            while sp < n:
                if text[sp] == '"':
                    if sp + 1 < n and text[sp + 1] == '"':
                        sp += 2
                    else:
                        return sp + 1
                else:
                    sp += 1
            raise ParseError(f"{path}:{get_line(p)}: unterminated verbatim string literal.")

        if text.startswith('$@"', p) or text.startswith('@$"', p):
            sp = p + 3
            while sp < n:
                if text[sp] == '"':
                    if sp + 1 < n and text[sp + 1] == '"':
                        sp += 2
                    else:
                        return sp + 1
                elif text[sp] == '{':
                    if sp + 1 < n and text[sp + 1] == '{':
                        sp += 2
                    else:
                        sp = scan_interpolated_expr(sp + 1, "}")
                elif text[sp] == '}':
                    if sp + 1 < n and text[sp + 1] == '}':
                        sp += 2
                    else:
                        raise ParseError(f"{path}:{get_line(sp)}: unexpected '}}' in verbatim interpolated string.")
                else:
                    sp += 1
            raise ParseError(f"{path}:{get_line(p)}: unterminated verbatim interpolated string literal.")

        if text.startswith('$"', p):
            sp = p + 2
            while sp < n:
                if text[sp] == '\\':
                    sp += 2
                elif text[sp] == '"':
                    return sp + 1
                elif text[sp] == '{':
                    if sp + 1 < n and text[sp + 1] == '{':
                        sp += 2
                    else:
                        sp = scan_interpolated_expr(sp + 1, "}")
                elif text[sp] == '}':
                    if sp + 1 < n and text[sp + 1] == '}':
                        sp += 2
                    else:
                        raise ParseError(f"{path}:{get_line(sp)}: unexpected '}}' in interpolated string.")
                else:
                    sp += 1
            raise ParseError(f"{path}:{get_line(p)}: unterminated interpolated string literal.")

        if text[p] == '"':
            sp = p + 1
            while sp < n:
                if text[sp] == '\\':
                    sp += 2
                elif text[sp] == '"':
                    return sp + 1
                elif text[sp] == '\n':
                    raise ParseError(f"{path}:{get_line(p)}: newline in string literal.")
                else:
                    sp += 1
            raise ParseError(f"{path}:{get_line(p)}: unterminated string literal.")

        return None

    def scan_interpolated_expr(expr_start: int, close_delim: str) -> int:
        p = expr_start
        depth = 0
        while p < n:
            if depth == 0 and text.startswith(close_delim, p):
                return p + len(close_delim)

            ch_expr = text[p]
            if ch_expr in " \t\r\n\v\f":
                p += 1
                continue

            lit_end = scan_literal_or_comment(p)
            if lit_end is not None:
                p = lit_end
                continue

            if ch_expr == "{":
                depth += 1
                p += 1
                continue
            elif ch_expr == "}":
                if depth > 0:
                    depth -= 1
                    p += 1
                    continue
                else:
                    raise ParseError(f"{path}:{get_line(p)}: unmatched '}}' in interpolated expression.")

            p += 1
        raise ParseError(f"{path}:{get_line(expr_start)}: unterminated interpolated expression.")

    tokens: list[Token] = []
    pos = 0
    n = len(text)

    while pos < n:
        while pos < n and text[pos] in " \t\r\n\v\f":
            pos += 1
        if pos >= n:
            break

        lit_end = scan_literal_or_comment(pos)
        if lit_end is not None:
            pos = lit_end
            continue

        ch = text[pos]
        start = pos
        line = get_line(start)

        if text.startswith("=>", pos):
            tokens.append(Token("ARROW", "=>", line, line, start, start + 2))
            pos += 2
            continue

        single_tokens = {
            "{": "LBRACE",
            "}": "RBRACE",
            "(": "LPAREN",
            ")": "RPAREN",
            "[": "LBRACKET",
            "]": "RBRACKET",
            ";": "SEMICOLON",
            ":": "COLON",
            ",": "COMMA",
            ".": "DOT",
            "<": "LANGLE",
            ">": "RANGLE",
        }
        if ch in single_tokens:
            tokens.append(Token(single_tokens[ch], ch, line, line, start, start + 1))
            pos += 1
            continue

        if ch == "@" or ch.isalpha() or ch == "_":
            p = pos + 1
            while p < n and (text[p].isalnum() or text[p] == "_"):
                p += 1
            val = text[pos:p]
            tokens.append(Token("IDENT", val, line, get_line(p - 1), start, p))
            pos = p
            continue

        tokens.append(Token("OTHER", ch, line, line, start, start + 1))
        pos += 1

    return tokens


def check_brace_balance(tokens: list[Token], path: Path | str = "") -> None:
    stack: list[Token] = []
    for tok in tokens:
        if tok.kind == "LBRACE":
            stack.append(tok)
        elif tok.kind == "RBRACE":
            if not stack:
                raise ParseError(f"{path}:{tok.line}: unmatched closing brace '}}'.")
            stack.pop()
    if stack:
        raise ParseError(f"{path}:{stack[-1].line}: unclosed opening brace '{{'.")


class MethodInfo:
    def __init__(self, name: str, start_line: int, end_line: int, body: str):
        self.name = name
        self.start_line = start_line
        self.end_line = end_line
        self.body = body


class ClassDeclaration:
    def __init__(self, name: str, full_name: str, file_path: Path, start_line: int, end_line: int):
        self.name = name
        self.full_name = full_name
        self.file_path = file_path
        self.start_line = start_line
        self.end_line = end_line
        self.methods: list[MethodInfo] = []
        self.nested_classes: list[ClassDeclaration] = []

    def all_nested_classes(self) -> list[ClassDeclaration]:
        res: list[ClassDeclaration] = []
        for nc in self.nested_classes:
            res.append(nc)
            res.extend(nc.all_nested_classes())
        return res


TYPE_DECL_KEYWORDS = {"class", "struct", "record", "interface"}


def get_class_name(header_tokens: list[Token]) -> str | None:
    for i, tok in enumerate(header_tokens):
        if tok.kind == "IDENT" and tok.val in TYPE_DECL_KEYWORDS:
            if i > 0 and header_tokens[i - 1].kind == "COLON":
                continue
            next_idx = i + 1
            if next_idx < len(header_tokens) and header_tokens[next_idx].kind == "IDENT" and header_tokens[next_idx].val in ("class", "struct"):
                next_idx += 1
            if next_idx < len(header_tokens) and header_tokens[next_idx].kind == "IDENT":
                return header_tokens[next_idx].val
    return None


def parse_method_signature(header: list[Token], class_name: str) -> tuple[str, int] | None:
    if not header:
        return None
    i = 0
    while i < len(header) and header[i].kind == "LBRACKET":
        depth = 0
        while i < len(header):
            if header[i].kind == "LBRACKET":
                depth += 1
            elif header[i].kind == "RBRACKET":
                depth -= 1
                if depth == 0:
                    i += 1
                    break
            i += 1
    sig_tokens = header[i:]
    if not sig_tokens:
        return None

    for w_idx in range(1, len(sig_tokens)):
        tok_w = sig_tokens[w_idx]
        if tok_w.kind == "IDENT" and tok_w.val == "where" and sig_tokens[w_idx - 1].kind == "RPAREN":
            sig_tokens = sig_tokens[:w_idx]
            break

    rparen_idx = None
    for idx in range(len(sig_tokens) - 1, -1, -1):
        if sig_tokens[idx].kind == "RPAREN":
            rparen_idx = idx
            break
    if rparen_idx is None:
        return None

    depth = 0
    lparen_idx = None
    for idx in range(rparen_idx, -1, -1):
        if sig_tokens[idx].kind == "RPAREN":
            depth += 1
        elif sig_tokens[idx].kind == "LPAREN":
            depth -= 1
            if depth == 0:
                lparen_idx = idx
                break
    if lparen_idx is None or lparen_idx == 0:
        return None

    prev_idx = lparen_idx - 1
    if sig_tokens[prev_idx].kind == "RANGLE":
        gdepth = 0
        for idx in range(prev_idx, -1, -1):
            if sig_tokens[idx].kind == "RANGLE":
                gdepth += 1
            elif sig_tokens[idx].kind == "LANGLE":
                gdepth -= 1
                if gdepth == 0:
                    prev_idx = idx - 1
                    break
        if prev_idx < 0:
            return None

    name_tok = sig_tokens[prev_idx]
    if name_tok.kind != "IDENT":
        return None
    name_clean = name_tok.val.lstrip("@")
    if name_clean in CONTROL_FLOW_KEYWORDS:
        return None

    if name_clean == class_name.lstrip("@"):
        prefix_tokens = [t for t in sig_tokens[:prev_idx] if t.val not in MODIFIERS]
        if not prefix_tokens:
            return None

    return name_tok.val, sig_tokens[0].line


def extract_members_from_tokens(
    tokens: list[Token],
    class_name: str,
    lines: list[str],
    file_path: Path,
    full_class_name: str,
) -> tuple[list[MethodInfo], list[ClassDeclaration]]:
    methods: list[MethodInfo] = []
    nested_classes: list[ClassDeclaration] = []

    idx = 0
    n = len(tokens)
    member_tokens: list[Token] = []

    while idx < n:
        tok = tokens[idx]

        if tok.kind == "LBRACE":
            cls_name = get_class_name(member_tokens)
            if cls_name is not None:
                nested_full = f"{full_class_name}.{cls_name}"
                start_tok = member_tokens[0] if member_tokens else tok
                depth = 1
                j = idx + 1
                while j < n and depth > 0:
                    if tokens[j].kind == "LBRACE":
                        depth += 1
                    elif tokens[j].kind == "RBRACE":
                        depth -= 1
                    j += 1
                nested_close = tokens[j - 1]
                nested_tokens = tokens[idx + 1 : j - 1]
                nested_m, sub_nested = extract_members_from_tokens(
                    nested_tokens, cls_name, lines, file_path, nested_full
                )
                nested_decl = ClassDeclaration(
                    cls_name, nested_full, file_path, start_tok.line, nested_close.line
                )
                nested_decl.methods = nested_m
                nested_decl.nested_classes = sub_nested
                nested_classes.append(nested_decl)
                member_tokens = []
                idx = j
                continue
            else:
                sig = parse_method_signature(member_tokens, class_name)
                depth = 1
                j = idx + 1
                while j < n and depth > 0:
                    if tokens[j].kind == "LBRACE":
                        depth += 1
                    elif tokens[j].kind == "RBRACE":
                        depth -= 1
                    j += 1
                close_tok = tokens[j - 1]
                if sig is not None:
                    m_name, start_line = sig
                    end_line = close_tok.line
                    body = "\n".join(lines[start_line - 1 : end_line])
                    methods.append(MethodInfo(m_name, start_line, end_line, body))
                member_tokens = []
                idx = j
                continue

        elif tok.kind == "ARROW":
            sig = parse_method_signature(member_tokens, class_name)
            b_depth = 0
            p_depth = 0
            k_depth = 0
            j = idx + 1
            semi_tok = None
            while j < n:
                jt = tokens[j]
                if jt.kind == "LBRACE":
                    b_depth += 1
                elif jt.kind == "RBRACE":
                    b_depth -= 1
                elif jt.kind == "LPAREN":
                    p_depth += 1
                elif jt.kind == "RPAREN":
                    p_depth -= 1
                elif jt.kind == "LBRACKET":
                    k_depth += 1
                elif jt.kind == "RBRACKET":
                    k_depth -= 1
                elif jt.kind == "SEMICOLON" and b_depth <= 0 and p_depth <= 0 and k_depth <= 0:
                    semi_tok = jt
                    j += 1
                    break
                j += 1
            if sig is not None and semi_tok is not None:
                m_name, start_line = sig
                end_line = semi_tok.line
                body = "\n".join(lines[start_line - 1 : end_line])
                methods.append(MethodInfo(m_name, start_line, end_line, body))
            member_tokens = []
            idx = j
            continue

        elif tok.kind == "SEMICOLON":
            member_tokens = []
            idx += 1
            continue

        else:
            member_tokens.append(tok)
            idx += 1

    return methods, nested_classes


def extract_classes_from_tokens(
    tokens: list[Token],
    lines: list[str],
    file_path: Path,
    current_ns: str = "",
) -> list[ClassDeclaration]:
    classes: list[ClassDeclaration] = []
    idx = 0
    n = len(tokens)
    header: list[Token] = []
    file_ns = current_ns

    while idx < n:
        tok = tokens[idx]

        if tok.kind == "SEMICOLON":
            if any(t.kind == "IDENT" and t.val == "namespace" for t in header):
                ns_parts = [t.val for t in header if t.kind == "IDENT" and t.val != "namespace"]
                block_ns = ".".join(ns_parts)
                file_ns = f"{file_ns}.{block_ns}" if file_ns else block_ns
            header = []
            idx += 1
            continue

        if tok.kind == "LBRACE":
            cls_name = get_class_name(header)
            if cls_name is not None:
                full_name = f"{file_ns}.{cls_name}" if file_ns else cls_name
                start_tok = header[0] if header else tok
                depth = 1
                j = idx + 1
                while j < n and depth > 0:
                    if tokens[j].kind == "LBRACE":
                        depth += 1
                    elif tokens[j].kind == "RBRACE":
                        depth -= 1
                    j += 1
                close_tok = tokens[j - 1]
                body_tokens = tokens[idx + 1 : j - 1]
                methods, nested = extract_members_from_tokens(
                    body_tokens, cls_name, lines, file_path, full_name
                )
                decl = ClassDeclaration(cls_name, full_name, file_path, start_tok.line, close_tok.line)
                decl.methods = methods
                decl.nested_classes = nested
                classes.append(decl)
                header = []
                idx = j
                continue
            elif any(t.kind == "IDENT" and t.val == "namespace" for t in header):
                ns_parts = [t.val for t in header if t.kind == "IDENT" and t.val != "namespace"]
                block_ns = ".".join(ns_parts)
                combined_ns = f"{file_ns}.{block_ns}" if file_ns else block_ns
                depth = 1
                j = idx + 1
                while j < n and depth > 0:
                    if tokens[j].kind == "LBRACE":
                        depth += 1
                    elif tokens[j].kind == "RBRACE":
                        depth -= 1
                    j += 1
                ns_tokens = tokens[idx + 1 : j - 1]
                classes.extend(extract_classes_from_tokens(ns_tokens, lines, file_path, combined_ns))
                header = []
                idx = j
                continue
            else:
                depth = 1
                j = idx + 1
                while j < n and depth > 0:
                    if tokens[j].kind == "LBRACE":
                        depth += 1
                    elif tokens[j].kind == "RBRACE":
                        depth -= 1
                    j += 1
                header = []
                idx = j
                continue
        else:
            header.append(tok)
            idx += 1

    return classes


def parse_cs_file(content: str, path: Path | str = "") -> list[ClassDeclaration]:
    tokens = tokenize_cs(content, path)
    check_brace_balance(tokens, path)
    lines = [ln[:-1] if ln.endswith("\r") else ln for ln in content.split("\n")]
    return extract_classes_from_tokens(tokens, lines, Path(path))


def flatten_classes(classes: list[ClassDeclaration]) -> list[ClassDeclaration]:
    result: list[ClassDeclaration] = []
    for c in classes:
        result.append(c)
        result.extend(flatten_classes(c.nested_classes))
    return result


def find_methods_in_class(decl: ClassDeclaration, method_name: str) -> list[tuple[Path, int, str]]:
    direct = [(decl.file_path, m.start_line, m.body) for m in decl.methods if _names_match(method_name, m.name)]
    if direct:
        return direct
    nested = []
    for nc in decl.nested_classes:
        nested.extend(find_methods_in_class(nc, method_name))
    return nested


class SourceIndex:
    """One preparation's source snapshot; never retained across audit runs."""

    def __init__(self, tests_root: Path):
        self.contents: dict[Path, str] = {}
        self.class_files: dict[str, list[Path]] = {}
        self._parsed: dict[Path, list[ClassDeclaration]] = {}
        for path in tests_root.rglob("*.cs"):
            content = self.read(path)
            names = dict.fromkeys(match.group(1) for match in re.finditer(r"\b(?:record\s+(?:class|struct)|class|struct|record|interface)\s+(@?\w+)\b", content))
            for name in names:
                self.class_files.setdefault(name, []).append(path)

    def read(self, path: Path) -> str:
        if path not in self.contents:
            self.contents[path] = _read(path)
        return self.contents[path]

    def get_classes(self, path: Path) -> list[ClassDeclaration]:
        if path not in self._parsed:
            content = self.read(path)
            self._parsed[path] = parse_cs_file(content, path)
        return self._parsed[path]


def _read_source(path: Path, source_index: SourceIndex | None) -> str:
    return _read(path) if source_index is None else source_index.read(path)


def _find_class_files(tests_root: Path, class_name: str, source_index: SourceIndex | None = None) -> list[Path]:
    if source_index is not None:
        files = source_index.class_files.get(class_name)
        if files:
            return files
        norm = _normalize_name(class_name)
        for name, file_list in source_index.class_files.items():
            if _normalize_name(name) == norm:
                return file_list
        return []
    candidates = []
    norm = _normalize_name(class_name)
    for cs_file in tests_root.rglob("*.cs"):
        content = _read_source(cs_file, source_index)
        if re.search(rf"\b(?:partial\s+)?(?:record\s+(?:class|struct)|class|struct|record|interface)\s+@?{re.escape(norm)}\b", content):
            candidates.append(cs_file)
    return candidates


def _method_occurrences(cs_file: Path, method_name: str, source_index: SourceIndex | None = None) -> list[tuple[int, str]]:
    if source_index is not None:
        decls = source_index.get_classes(cs_file)
    else:
        content = _read_source(cs_file, source_index)
        decls = parse_cs_file(content, cs_file)
    occurrences = []
    for decl in decls:
        for _path, line, body in find_methods_in_class(decl, method_name):
            occurrences.append((line - 1, body))
    return occurrences


def resolve_unit_reference(tests_root: Path, reference: str, source_index: SourceIndex | None = None) -> dict:
    """Resolve `Class.Method` to its test body. Ambiguity/unresolved = ParseError."""
    parts = reference.split(".")
    if len(parts) < 2 or not all(parts):
        raise ParseError(f"Unit reference '{reference}' must be 'Class.Method'.")
    class_spec = ".".join(parts[:-1])
    method_name = parts[-1]

    # Identify candidate files
    candidate_files: list[Path] = []
    spec_parts = class_spec.split(".")
    seen_files = set()
    if source_index is not None:
        for p in spec_parts:
            norm = _normalize_name(p)
            for name, f_list in source_index.class_files.items():
                if _normalize_name(name) == norm:
                    for f in f_list:
                        if f not in seen_files:
                            seen_files.add(f)
                            candidate_files.append(f)
    else:
        for p in spec_parts:
            for f in _find_class_files(tests_root, p):
                if f not in seen_files:
                    seen_files.add(f)
                    candidate_files.append(f)

    matched_classes: list[ClassDeclaration] = []
    for cs_file in candidate_files:
        if source_index is not None:
            decls = source_index.get_classes(cs_file)
        else:
            content = _read_source(cs_file, source_index)
            decls = parse_cs_file(content, cs_file)

        for decl in flatten_classes(decls):
            if "." in class_spec:
                if _qualified_names_match(class_spec, decl.full_name):
                    matched_classes.append(decl)
            else:
                if _names_match(class_spec, decl.name):
                    matched_classes.append(decl)

    if not matched_classes:
        raise ParseError(f"Test class '{class_spec}' not found under {tests_root}.")

    bodies: list[tuple[Path, int, str]] = []
    by_type: dict[str, list[ClassDeclaration]] = {}
    for decl in matched_classes:
        by_type.setdefault(decl.full_name, []).append(decl)
    for parts in by_type.values():
        direct = [
            (d.file_path, m.start_line, m.body)
            for d in parts
            for m in d.methods
            if _names_match(method_name, m.name)
        ]
        if direct:
            bodies.extend(direct)
            continue
        for d in parts:
            for nc in d.nested_classes:
                bodies.extend(find_methods_in_class(nc, method_name))

    if not bodies:
        raise ParseError(f"Test method '{reference}' not found (class file: {matched_classes[0].file_path.name}).")
    if len(bodies) > 1:
        raise ParseError(
            f"Test method '{reference}' is ambiguous: {len(bodies)} occurrences found "
            f"({', '.join(str(p) for p, _i, _b in bodies)}). Rename the tests."
        )

    cs_file, start_line, body = bodies[0]
    return {
        "source": f"{cs_file.relative_to(tests_root.parents[0])}",
        "line": start_line,
        "body": body,
        "sha256": sha256_text(body),
    }


def resolve_e2e_reference(tests_root: Path, reference: str, source_index: SourceIndex | None = None) -> dict:
    """Resolve `script.sh [Scenario]` to the whole script or its scenario block."""
    parts = reference.split(None, 1)
    if not parts:
        raise ParseError(f"E2E reference '{reference}' must be 'script.sh [Scenario]'.")
    script = parts[0]
    script_path = tests_root / script
    if not script_path.is_file():
        script_path = tests_root / "tests" / script
    if not script_path.is_file():
        script_path = tests_root.parent / script
    if not script_path.is_file():
        raise ParseError(f"E2E script '{script}' not found.")

    content = _read_source(script_path, source_index)
    lines = content.splitlines()
    start = 0
    block = lines
    if len(parts) == 2:
        scenario = parts[1]
        start = next((
            idx for idx, line in enumerate(lines)
            if scenario in line and any(marker in line for marker in ("scenario:", "Test Case", "print_info", "INFO"))
        ), None)
        if start is None:
            raise ParseError(f"Scenario '{scenario}' not found in {script_path.name}.")
        block = [lines[start]]
        for line in lines[start + 1:]:
            if "scenario:" in line or ("Test Case" in line and "print_info" in line):
                break
            block.append(line)
    text = "\n".join(block)
    return {
        "source": f"tests/{script}",
        "line": start + 1,
        "body": text,
        "sha256": sha256_text(text),
    }


def resolve_reference(tests_root: Path, row: dict, source_index: SourceIndex | None = None) -> dict | None:
    if row["coverage"] == "exemption":
        return None
    if row["coverage"] == "unit":
        return resolve_unit_reference(tests_root, row["reference"], source_index)
    return resolve_e2e_reference(tests_root, row["reference"], source_index)


def resolve_test_paths(
    repo_root: Path,
    row: dict,
    source_index: SourceIndex | None = None,
) -> set[str]:
    """Resolve a TSV row to repo-relative file paths for its mapped tests.

    Returns an empty set for exemptions, missing files, or unresolved references.
    """
    coverage = row.get("coverage", "").lower()
    ref = row.get("reference", "").strip()
    if coverage == "exemption" or not ref or ref == "-":
        return set()

    root_resolved = repo_root.resolve()
    if coverage == "unit":
        parts = ref.split(".")
        class_spec = ".".join(parts[:-1]) if len(parts) >= 2 else ref
        cls_name = class_spec.split(".")[-1].strip()
        if not cls_name:
            return set()
        files = _find_class_files(repo_root, cls_name, source_index)
        paths: set[str] = set()
        for f in files:
            try:
                paths.add(f.resolve().relative_to(root_resolved).as_posix())
            except ValueError:
                pass
        return paths

    if coverage == "e2e":
        script = ref.split(None, 1)[0].replace("\\", "/")
        script_path = repo_root / script
        if not script_path.is_file():
            script_path = repo_root / "tests" / script
        if script_path.is_file():
            try:
                return {script_path.resolve().relative_to(root_resolved).as_posix()}
            except ValueError:
                pass
        return set()

    return set()


