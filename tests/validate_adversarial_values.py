#!/usr/bin/env python3
"""Validate parser-safe adversarial-values DAT output."""

import csv
import sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: validate_adversarial_values.py <load-file> <record-count>", file=sys.stderr)
        return 2

    load_file = Path(sys.argv[1])
    expected_records = int(sys.argv[2])
    content = load_file.read_bytes().decode("utf-8")
    if not content.endswith("\r\n\r\n\r\n"):
        raise AssertionError("expected two trailing empty DAT rows using CRLF")

    with load_file.open("r", encoding="utf-8", newline="") as stream:
        parsed_rows = list(csv.reader(stream, delimiter="\x14", quotechar="\xfe", doublequote=True, strict=True))

    empty_rows = sum(not row for row in parsed_rows)
    records = [row for row in parsed_rows if row]
    if empty_rows != 2:
        raise AssertionError(f"expected two trailing empty rows, got {empty_rows}")
    if len(records) != expected_records + 1:
        raise AssertionError(f"expected {expected_records} data records plus a header, got {len(records)} non-empty rows")

    if expected_records:
        value_fields = [value for row in records[1:] for value in row]
        if not any("\x14" in value and "\xfe" in value for value in value_fields):
            raise AssertionError("escaped column and quote delimiter values were not found")
        if not any("Ã¾" in value for value in value_fields):
            raise AssertionError("quote-lookalike value was not found")
        if not any(len(value) >= 32_768 for value in value_fields):
            raise AssertionError("platform-length value was not found")

    print(f"Validated {expected_records} valid adversarial-values DAT records")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
