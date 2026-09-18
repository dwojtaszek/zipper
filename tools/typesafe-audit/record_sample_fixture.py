#!/usr/bin/env python3
"""Records a deterministic fixture-mode response for the sample question set.

Used by the E2E scripts (tests/test-typesafe-audit.sh/.bat) to seed the
no-network fixture store without shipping content hashes that drift when
the audited sources change.
"""

from __future__ import annotations

import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOL_DIR))

import runner  # noqa: E402


def main() -> int:
    if len(sys.argv) != 2:
        print(f"usage: {Path(__file__).name} <fixture-dir>", file=sys.stderr)
        return 2

    config = runner.load_config(runner.DEFAULT_CONFIG)
    inputs = runner.resolve_inputs(
        TOOL_DIR / "questions/files-sample.list",
        config["max_file_bytes"],
        config["max_total_bytes"],
    )
    questions = runner.load_questions(TOOL_DIR / "questions/example.json")
    request = runner.build_request(inputs, questions, config["model"])

    # Synthetic response: the foundation slice defines no real judgments.
    response = {
        "model": config["model"],
        "answers": {
            "req179_example": {
                "type": "choice",
                "choice": "traceability",
                "probabilities": {"traceability": 0.9, "consistency": 0.05, "none": 0.05},
                "confidence": 0.9,
            },
            "noul_example": {"type": "noul", "noul": 0.95},
        },
        "usage": {"input_tokens": 100, "output_tokens": 10},
    }
    path = runner.record_fixture(request, Path(sys.argv[1]), response)
    print(f"recorded sample fixture {path.name[:16]}…")
    return 0


if __name__ == "__main__":
    sys.exit(main())
