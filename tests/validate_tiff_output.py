#!/usr/bin/env python3
"""Check the TIFF (.tiff / .tif) output of a generation run: the chaos audit and the byte order.

Usage:
    validate_tiff_output.py standard <dat_properties.json>
    validate_tiff_output.py production <loadfile_properties.json>
    validate_tiff_output.py byte-order <archive.zip | directory> <II|MM>

standard:   the run used --chaos-amount 100% on 8 TIFFs with all four TIFF types, so 8 anomalies
            must be audited, every TIFF type must appear, and each anomaly must locate itself in IFD 1.
production: the run used --chaos-types tiff-truncated-ifd,tiff-tag-count-lie on 6 TIFFs at 100%, so
            6 anomalies must be audited, and both of those types must appear.
byte-order: every .tiff or .tif file must start with the byte-order mark and the TIFF magic number 42.

Exits 0 when the check passes, 1 otherwise.
"""
import json
import os
import sys
import zipfile

STANDARD_TYPES = {"tiff-dimension-lie", "tiff-truncated-ifd", "tiff-tag-count-lie", "tiff-strip-offset"}
PRODUCTION_TYPES = {"tiff-truncated-ifd", "tiff-tag-count-lie"}
BYTE_ORDER_HEADERS = {"II": b"II\x2a\x00", "MM": b"MM\x00\x2a"}


def check_chaos(mode, path):
    with open(path, encoding="utf-8") as handle:
        chaos = json.load(handle)["chaosMode"]

    anomalies = chaos["injectedAnomalies"] or []
    error_types = {anomaly["errorType"] for anomaly in anomalies}
    if mode == "standard":
        ok = (
            chaos["totalAnomalies"] == 8
            and len(anomalies) == 8
            and error_types == STANDARD_TYPES
            and all(anomaly.get("location", "").startswith("IFD 1") for anomaly in anomalies)
        )
    else:
        ok = chaos["totalAnomalies"] == 6 and len(anomalies) == 6 and error_types == PRODUCTION_TYPES

    if not ok:
        print(f"Audit mismatch in {path}: totalAnomalies={chaos['totalAnomalies']}, "
              f"injected={len(anomalies)}, errorTypes={sorted(error_types)}", file=sys.stderr)
    return ok


def tiff_headers(path):
    """Yields (name, first four bytes) for every .tiff or .tif file in a directory or an archive."""
    if os.path.isdir(path):
        for root, _, files in os.walk(path):
            for name in files:
                if name.lower().endswith((".tiff", ".tif")):
                    with open(os.path.join(root, name), "rb") as handle:
                        yield name, handle.read(4)
        return

    with zipfile.ZipFile(path) as archive:
        for name in archive.namelist():
            if name.lower().endswith((".tiff", ".tif")):
                with archive.open(name) as handle:
                    yield name, handle.read(4)


def check_byte_order(path, byte_order):
    expected = BYTE_ORDER_HEADERS[byte_order]
    headers = list(tiff_headers(path))
    if not headers:
        print(f"No .tiff or .tif files found in {path}", file=sys.stderr)
        return False

    wrong = [name for name, header in headers if header != expected]
    if wrong:
        print(f"Expected byte order {byte_order} in {path}, but these files differ: {wrong}", file=sys.stderr)
    return not wrong


def main(argv):
    if len(argv) == 3 and argv[1] in ("standard", "production"):
        ok = check_chaos(argv[1], argv[2])
    elif len(argv) == 4 and argv[1] == "byte-order" and argv[3] in BYTE_ORDER_HEADERS:
        ok = check_byte_order(argv[2], argv[3])
    else:
        print(__doc__, file=sys.stderr)
        return 1

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
