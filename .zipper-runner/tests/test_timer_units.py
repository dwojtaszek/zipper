"""Tests for the systemd timer units that drive the runner.

The deploy timer exists to make a merged change live within about a minute. That
promise is a property of the unit file, not of the script it runs, so it is
guarded here: systemd's default `AccuracySec=1min` lets a `*:0/1` timer slip to
113 seconds, which skips a whole minute of deploys — measured on the live host as
5.7% of ticks. A one-minute cadence cannot tolerate a one-minute slack.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""

import re
import unittest
from pathlib import Path

_SYSTEMD_DIR = Path(__file__).resolve().parents[1] / "systemd"


def _unit_files():
    """Every timer unit and drop-in the repository installs."""
    return sorted(p for p in _SYSTEMD_DIR.rglob("*")
                  if p.is_file() and (p.suffix == ".timer" or p.name == "cadence.conf"))


def _effective_on_calendar(path: Path) -> str:
    """The timer's cadence, after drop-in `OnCalendar=` resets are applied.

    A drop-in that writes a bare `OnCalendar=` clears the list, so the last
    assignment is the effective one. Drop-ins are how the repository retunes a
    cadence without editing the unit.
    """
    values = []
    for line in path.read_text().splitlines():
        match = re.match(r"\s*OnCalendar\s*=\s*(.*)$", line)
        if match:
            values.append(match.group(1).strip())
    return values[-1] if values else ""


def _accuracy_seconds(path: Path) -> int | None:
    for line in path.read_text().splitlines():
        match = re.match(r"\s*AccuracySec\s*=\s*(\S+)\s*$", line)
        if match:
            raw = match.group(1).strip().lower()
            if raw == "us":
                return 0
            unit_seconds = {"s": 1, "sec": 1, "m": 60, "min": 60, "h": 3600}
            for suffix, scale in sorted(unit_seconds.items(), key=lambda kv: -len(kv[0])):
                if raw.endswith(suffix):
                    return int(float(raw[: -len(suffix)]) * scale)
            return int(float(raw))
    return None


class DeployTimerAccuracyTests(unittest.TestCase):
    def test_deployTimer_setsAnAccuracyBelowItsCadence(self):
        """A one-minute cadence with the default one-minute slack skips whole
        minutes. The deploy timer must be tighter than the interval it promises."""
        unit = _SYSTEMD_DIR / "zipper-deploy.timer"
        self.assertTrue(unit.exists(), "the deploy timer unit must be installed from the repo")
        self.assertEqual(_effective_on_calendar(unit), "*:0/1")

        accuracy = _accuracy_seconds(unit)

        self.assertIsNotNone(
            accuracy, "the deploy timer must set AccuracySec explicitly: the 1min default "
                      "equals its whole cadence and skips ticks")
        self.assertLess(accuracy, 60,
                        f"AccuracySec={accuracy}s is not tighter than the 1-minute cadence")

    def test_everySubFiveMinuteTimer_setsAnAccuracy(self):
        """Any timer whose cadence is short enough for the default slack to matter
        must state its own accuracy rather than inherit one."""
        for path in _unit_files():
            calendar = _effective_on_calendar(path)
            match = re.match(r"^\*:0/(\d+)$", calendar)
            if not match:
                continue
            minutes = int(match.group(1))
            if minutes >= 5:
                continue
            with self.subTest(unit=str(path.relative_to(_SYSTEMD_DIR))):
                self.assertIsNotNone(
                    _accuracy_seconds(path),
                    f"{path.name} fires every {minutes}m, where the 1min default slack is a "
                    f"whole {minutes}-minute fraction of the cadence")


class TimerUnitTests(unittest.TestCase):
    def test_runnerTimers_areInstalledFromTheRepository(self):
        """The units are versioned so a cadence change is reviewable, not just made
        by hand on the host."""
        self.assertTrue((_SYSTEMD_DIR / "zipper-deploy.timer").exists())
        self.assertTrue((_SYSTEMD_DIR / "zipper-deploy.service").exists())

    def test_deployService_runsTheInstalledCopyOutsideTheCheckout(self):
        """A script inside the checkout is an untracked file in the commit that adds
        it, which blocks the fast-forward that would deploy it."""
        text = (_SYSTEMD_DIR / "zipper-deploy.service").read_text()
        exec_start = re.search(r"^\s*ExecStart\s*=\s*(\S+)", text, re.MULTILINE)
        self.assertIsNotNone(exec_start)
        self.assertIn(".local/bin/", exec_start.group(1))
        self.assertNotIn("/repos/zipper/.zipper-runner/deploy-main.sh", exec_start.group(1))

    def test_deployService_pinsTheCheckoutItDeploys(self):
        """The script derives nothing from its own location when REPO_PATH is set,
        so the unit must set it."""
        text = (_SYSTEMD_DIR / "zipper-deploy.service").read_text()
        self.assertRegex(text, r"(?m)^\s*Environment\s*=\s*REPO_PATH=")


if __name__ == "__main__":
    unittest.main()
