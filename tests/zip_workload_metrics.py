"""Measure individual ZIP workload processes on Linux."""

import os
import time


def measure_process(command, *, file_actions=()):
    started = time.monotonic()
    child = os.posix_spawnp(command[0], command, os.environ, file_actions=file_actions)
    _, status, usage = os.wait4(child, 0)
    code = os.waitstatus_to_exitcode(status)
    if code:
        raise RuntimeError(f"{command[0]} exited with status {code}")
    return {"seconds": round(time.monotonic() - started, 3), "peak_rss_kib": usage.ru_maxrss}
