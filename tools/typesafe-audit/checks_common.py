"""Shared helpers for the TypeSafe check packages (#958 review refactor).

The three checks (requirements, traceability, pr-spec) previously carried
byte-identical copies of these helpers; the duplication gate (new code >= 3%)
failed on exactly that. Import via the TOOL_DIR path entry the checks already
install.
"""

from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

# One request key namespace per obligation/span across all checks.
SPAN_SEPARATOR = "#"


def load_module(name: str, path: Path):
    """Explicit-path module loading: sibling check packages ship same-named
    modules (collect, runner) that would collide through the sys.modules
    cache."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def write_outputs(json_out: Path, md_out: Path, summary_out: Path | None, report: dict, markdown: str) -> None:
    """Write the JSON/Markdown report and optionally append the job summary."""
    json_out.parent.mkdir(parents=True, exist_ok=True)
    md_out.parent.mkdir(parents=True, exist_ok=True)
    json_out.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    md_out.write_text(markdown, encoding="utf-8")
    if summary_out:
        with open(summary_out, "a", encoding="utf-8") as fh:
            fh.write(markdown)


def load_json(path: Path, label: str = "check") -> dict:
    try:
        with open(path, "r", encoding="utf-8") as fh:
            return json.load(fh)
    except (OSError, json.JSONDecodeError) as exc:
        print(f"{label}: input error: cannot read {path}: {exc}", file=sys.stderr)
        sys.exit(2)


def _status_for_noul(answer: object, policy: dict) -> str:
    if answer is None or not isinstance(answer, (int, float)):
        return "needs-human-review"
    low, high = policy["noul_finding_range"]
    if low < answer < high:
        return "needs-human-review"
    return "finding" if answer >= high else "ok"


def _status_for_choice(question_id: str, answer: object, confidence: float | None, policy: dict) -> str:
    threshold = policy["confidence_threshold"]
    is_low_conf = confidence is None or confidence < threshold
    good_values = policy["choice_good_values"].get(question_id, [])
    if answer not in good_values:
        return "needs-human-review" if is_low_conf else "finding"
    return "needs-human-review" if (confidence is not None and confidence < threshold) else "ok"


def status_for(question_id: str, row: dict, policy: dict) -> str:
    """Shared status policy: needs-human-review on low confidence or mid-range
    noul; finding/ok otherwise. Identical semantics in every advisory check."""
    row_type = row.get("type")
    if row_type == "noul":
        return _status_for_noul(row.get("answer"), policy)
    if row_type == "choice":
        return _status_for_choice(question_id, row.get("answer"), row.get("confidence"), policy)
    return "needs-human-review"
