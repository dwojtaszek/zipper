"""Compare documented required contexts with live protection and branch rules."""
import json
from pathlib import Path
import re
import sys


def main():
    try:
        document, protection_path, rules_path = map(Path, sys.argv[1:])
        matches = re.findall(r'^Required status checks for `main`: (.+)\.$', document.read_text(), re.M)
        if len(matches) != 1:
            raise ValueError('Expected one required-status-check declaration in docs/cicd.md')
        expected = set(re.findall(r'`([^`]+)`', matches[0]))
        if not expected:
            raise ValueError('Required-status-check declaration is empty')
        protection = json.loads(protection_path.read_text())
        status = protection['required_status_checks']
        actual = set(status['contexts']) | {check['context'] for check in status['checks']}
        rules = json.loads(rules_path.read_text())
        for rule in rules:
            if rule['type'] == 'required_status_checks':
                actual.update(check['context'] for check in rule['parameters']['required_status_checks'])
        if actual != expected:
            raise ValueError(f'Protection drift: documented={sorted(expected)}, live={sorted(actual)}')
    except (ValueError, KeyError, TypeError, OSError) as error:
        print(f'::error::{error}', file=sys.stderr)
        return 1
    print(f'Required checks match: {sorted(expected)}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
