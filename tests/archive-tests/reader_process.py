"""Process-tree helpers for bounded Archive Test reader commands."""

import os
import signal
import subprocess


def start_reader_process(command, **kwargs):
    # Keep reader descendants in a killable process group on POSIX.
    kwargs["start_new_session"] = os.name != "nt"
    return subprocess.Popen(command, **kwargs)


def terminate_reader_process(process):
    process_id = getattr(process, "pid", None)
    if os.name == "nt" and process_id is not None:
        if process.poll() is not None:
            return
        try:
            subprocess.run(
                ["taskkill", "/T", "/F", "/PID", str(process_id)],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                timeout=5,
                check=False,
            )
        except (OSError, subprocess.TimeoutExpired):
            pass
        if process.poll() is None:
            process.kill()
    elif process_id is not None:
        try:
            os.killpg(process_id, signal.SIGKILL)
        except ProcessLookupError:
            pass
        except PermissionError:
            if process.poll() is None:
                process.kill()
    elif process.poll() is None:
        process.kill()


def run_reader_command(command, timeout):
    process = start_reader_process(
        command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    try:
        stdout, stderr = process.communicate(timeout=timeout)
    except subprocess.TimeoutExpired:
        terminate_reader_process(process)
        process.communicate()
        raise
    return subprocess.CompletedProcess(command, process.returncode, stdout, stderr)
