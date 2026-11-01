#!/usr/bin/env python3
"""Check the chaos audit of an email (.eml) chaos run.

Usage:
    validate_email_chaos_audit.py standard <properties.json>
    validate_email_chaos_audit.py production <loadfile_properties.json>

standard:   the run used --chaos-amount 50% on 20 Emails with all four email types, so 10 anomalies
            must be audited, each with an email type.
production: the run used --chaos-types encoded-word,header-fold on 10 Emails, so at least one anomaly
            must be audited, each with one of those two types.

Exits 0 when the audit matches, 1 otherwise.
"""
import json
import sys

STANDARD_TYPES = {"mime-boundary", "encoded-word", "header-fold", "rf822-depth"}
PRODUCTION_TYPES = {"encoded-word", "header-fold"}


def main(argv):
    if len(argv) != 3 or argv[1] not in ("standard", "production"):
        print(__doc__, file=sys.stderr)
        return 1

    mode, path = argv[1], argv[2]
    with open(path, encoding="utf-8") as handle:
        chaos = json.load(handle)["chaosMode"]

    anomalies = chaos["injectedAnomalies"] or []
    error_types = {anomaly["errorType"] for anomaly in anomalies}
    if mode == "standard":
        ok = chaos["totalAnomalies"] == 10 and len(anomalies) == 10 and error_types <= STANDARD_TYPES
    else:
        ok = chaos["totalAnomalies"] >= 1 and len(anomalies) == chaos["totalAnomalies"] and error_types <= PRODUCTION_TYPES

    if not ok:
        print(f"Audit mismatch in {path}: totalAnomalies={chaos['totalAnomalies']}, "
              f"injected={len(anomalies)}, errorTypes={sorted(error_types)}", file=sys.stderr)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
