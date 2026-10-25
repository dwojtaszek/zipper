"""Tests for the systemd timer units that drive the runner.

Two of the runner's availability properties live in the unit files rather than in
the script they run, so they are guarded here.

The deploy timer exists to make a merged change live within about a minute. Part of
that promise is systemd's default `AccuracySec` of one minute, which is this
timer's entire cadence, so a tick may fire up to 60 seconds after its scheduled
elapse. A one-minute cadence cannot tolerate a one-minute slack, so the accuracy is
pinned here and guarded below.

The other is the clock the cadence is expressed in. systemd walks an `OnCalendar`
spec in local wall-clock time, which repeats an hour at the autumn DST fall-back;
the repeated minutes sort before the minute just fired, so systemd skips them and
the entire runner goes quiet for 61-75 minutes. Every runner timer therefore pins
its cadence to UTC, which has no transition.

Run from the .zipper-runner directory:
    python3 -m unittest discover -s tests
"""

import re
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from zoneinfo import ZoneInfo

_SYSTEMD_DIR = Path(__file__).resolve().parents[1] / "systemd"

# Names systemd accepts for the fixed zero offset besides the tzdata zones. They
# are listed explicitly because `zoneinfo` resolves tzdata, not systemd's own
# aliases, so it would not recognize `utc` or `Zulu` on its own.
_FIXED_ZERO_ZONES = {"utc", "gmt", "gmt0", "zulu", "universal", "etc/utc", "etc/gmt"}

# A trailing token that carries a calendar metacharacter (`*`, `:`, `@`, `..`) or
# starts with a digit is part of the calendar expression rather than a timezone
# name, so `*:0/1` and `Mon..Fri 09:00` read as local time while `UTC` and
# `Etc/GMT+0` read as zones.
_NOT_A_TIMEZONE_RE = re.compile(r"[*:@]|\.\.|^\d")


def _unit_files():
    """Every timer unit and drop-in the repository installs.

    Drop-ins are matched by their directory (`<unit>.timer.d/`) rather than by a
    fixed filename, so a drop-in named `10-cadence.conf` is guarded too.
    """
    return sorted(p for p in _SYSTEMD_DIR.rglob("*")
                  if p.is_file() and (p.suffix == ".timer"
                                      or (p.suffix == ".conf" and p.parent.name.endswith(".timer.d"))))


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


def _resets_on_calendar_list(path: Path) -> bool:
    """Whether the file clears the timer's list before assigning a cadence.

    This matters for a drop-in over a host-only base unit: the assignment only
    *replaces* the base unit's cadence because the bare `OnCalendar=` clears the
    list first. Without it the two specs are merged, and a local-time base unit
    keeps its DST gap no matter what the drop-in says.
    """
    lines = [re.match(r"\s*OnCalendar\s*=\s*(.*)$", line)
             for line in path.read_text().splitlines()]
    assignments = [m.group(1).strip() for m in lines if m]
    return bool(assignments) and assignments[0] == ""


def _timezone_of(on_calendar: str) -> str | None:
    """The timezone an OnCalendar spec names, or None when it is local time."""
    tokens = on_calendar.split()
    if not tokens:
        return None
    candidate = tokens[-1]
    return None if _NOT_A_TIMEZONE_RE.match(candidate) else candidate


def _zone_utc_offsets(zone: str) -> set[timedelta] | None:
    """Every UTC offset a zone uses during a year, or None if it cannot be resolved.

    One offset means the zone's wall clock never repeats or skips an hour, which is
    the property a cadence needs: pinning a spec to a DST-observing zone like
    `Europe/Warsaw` reintroduces the very gap that pinning to UTC removes.
    """
    if zone.lower() in _FIXED_ZERO_ZONES:
        return {timedelta(0)}
    try:
        tz = ZoneInfo(zone)
    except Exception:
        return None
    start = datetime(2026, 1, 1, tzinfo=timezone.utc)
    return {(start + timedelta(days=day)).astimezone(tz).utcoffset() for day in range(366)}


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
        """The default accuracy equals this timer's whole cadence, so a tick may
        fire up to 60s late. The timer must be tighter than the interval it
        promises."""
        unit = _SYSTEMD_DIR / "zipper-deploy.timer"
        self.assertTrue(unit.exists(), "the deploy timer unit must be installed from the repo")
        self.assertEqual(_effective_on_calendar(unit), "*:0/1 UTC")

        accuracy = _accuracy_seconds(unit)

        self.assertIsNotNone(
            accuracy, "the deploy timer must set AccuracySec explicitly: the 1min default "
                      "equals its whole cadence")
        self.assertLess(accuracy, 60,
                        f"AccuracySec={accuracy}s is not tighter than the 1-minute cadence")

    def test_everySubFiveMinuteTimer_setsAnAccuracy(self):
        """Any timer whose cadence is short enough for the default slack to matter
        must state its own accuracy rather than inherit one."""
        for path in _unit_files():
            calendar = _effective_on_calendar(path)
            match = re.match(r"^\*:0/(\d+)(?:\s+\S+)?$", calendar)
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


class TimerTimezoneTests(unittest.TestCase):
    """A cadence read against a clock that repeats an hour stops the runner for it.

    systemd iterates an OnCalendar spec through the wall clock of the zone the spec
    names. At the autumn fall-back that clock repeats an hour, and the repeated
    minutes sort before the minute just fired, so the iterator treats them as
    elapsed and jumps to the end of the repeat. Measured on the host at the 2026
    autumn transition: last fires of 02:45 CEST (intake), 02:55 CEST (babysit) and
    02:59 CEST (deploy), all with a next fire of 03:00 CET — a 61-75 minute
    real-time outage for the entire runner.

    A zone whose offset never changes cannot produce it, so every runner timer names
    one. The check is on the zone's offsets rather than on the presence of a name:
    `OnCalendar=*:0/5 Europe/Warsaw` is as broken as a bare local spec, and the
    hazard belongs to the spec's zone rather than to the host's.
    """

    def test_everyRunnerTimer_namesAZoneThatCannotRepeatAnHour(self):
        for path in _unit_files():
            calendar = _effective_on_calendar(path)
            if not calendar:
                continue
            with self.subTest(unit=str(path.relative_to(_SYSTEMD_DIR))):
                zone = _timezone_of(calendar)
                self.assertIsNotNone(
                    zone,
                    f"{path.relative_to(_SYSTEMD_DIR)} schedules '{calendar}' against the local "
                    f"clock, which repeats an hour at the autumn DST fall-back and stops the "
                    f"runner for that hour")
                offsets = _zone_utc_offsets(zone)
                self.assertIsNotNone(
                    offsets,
                    f"{path.relative_to(_SYSTEMD_DIR)} names the zone '{zone}', which this test "
                    f"cannot resolve, so it cannot show the zone is free of the repeated hour")
                self.assertEqual(
                    len(offsets), 1,
                    f"{path.relative_to(_SYSTEMD_DIR)} pins '{calendar}' to {zone}, whose UTC "
                    f"offset changes during the year: its wall clock repeats an hour at the "
                    f"autumn fall-back exactly as the local clock does")

    def test_dropIns_overAHostOnlyTimer_resetTheCadenceTheyReplace(self):
        """The intake and babysit base units live on the host, so the drop-in's
        cadence only *replaces* theirs because the bare `OnCalendar=` clears the
        list first. Drop that line and the specs merge, and a host base unit still
        carrying a local-time cadence keeps its DST gap with the suite green.
        """
        for path in _unit_files():
            if path.suffix != ".conf":
                continue
            with self.subTest(unit=str(path.relative_to(_SYSTEMD_DIR))):
                self.assertTrue(
                    _resets_on_calendar_list(path),
                    f"{path.relative_to(_SYSTEMD_DIR)} assigns an OnCalendar without clearing the "
                    f"list first, so it merges with the base unit's cadence instead of replacing it")


class TimezoneClassificationTests(unittest.TestCase):
    """The helpers above, against the spellings systemd and tzdata actually accept."""

    def test_timezoneOf_shouldReadTheTrailingZoneAndNothingElse(self):
        cases = {
            "*:0/1": None,                      # bare: local time
            "*:0/1 UTC": "UTC",
            "*:0/1 utc": "utc",                 # systemd accepts lowercase
            "*:0/1 Etc/UTC": "Etc/UTC",
            "*:0/1 America/Argentina/Buenos_Aires": "America/Argentina/Buenos_Aires",
            "Mon..Fri 09:00": None,             # a trailing clock time is not a zone
            "*-*-* 06:05:00": None,
            "": None,
        }
        for spec, expected in cases.items():
            with self.subTest(spec=spec):
                self.assertEqual(_timezone_of(spec), expected)

    def test_zoneUtcOffsets_shouldReportOneOffsetForAFixedZoneAndTwoForADstZone(self):
        cases = {
            "UTC": 1,
            "utc": 1,
            "Etc/GMT+0": 1,
            "Zulu": 1,                          # a systemd alias, not a tzdata zone
            "Asia/Kolkata": 1,                  # +05:30, no DST
            "America/Argentina/Buenos_Aires": 1,
            "Europe/Warsaw": 2,                 # CET/CEST
            "America/St_Johns": 2,              # -03:30/-02:30
            "Not/AZone": None,
        }
        for zone, expected in cases.items():
            with self.subTest(zone=zone):
                offsets = _zone_utc_offsets(zone)
                self.assertEqual(None if offsets is None else len(offsets), expected)


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
