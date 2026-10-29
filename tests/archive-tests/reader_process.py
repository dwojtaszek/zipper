"""Process-tree helpers for bounded Archive Test reader commands."""

import os
import signal
import subprocess
import sys
import threading
import time


_READER_START_FAILURE = "__ZIPPER_READER_START_FAILURE__:"
MAX_READER_OUTPUT_BYTES = 64 * 1024 * 1024
_READER_OUTPUT_CHUNK_BYTES = 64 * 1024
_JOB_OBJECT_EXTENDED_LIMIT_INFORMATION = 9
_JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000


def _run_windows_reader_wrapper(command):
    import ctypes
    import threading
    from ctypes import wintypes

    class BasicLimitInformation(ctypes.Structure):
        _fields_ = [
            ("PerProcessUserTimeLimit", ctypes.c_longlong),
            ("PerJobUserTimeLimit", ctypes.c_longlong),
            ("LimitFlags", wintypes.DWORD),
            ("MinimumWorkingSetSize", ctypes.c_size_t),
            ("MaximumWorkingSetSize", ctypes.c_size_t),
            ("ActiveProcessLimit", wintypes.DWORD),
            ("Affinity", ctypes.c_size_t),
            ("PriorityClass", wintypes.DWORD),
            ("SchedulingClass", wintypes.DWORD),
        ]

    class IoCounters(ctypes.Structure):
        _fields_ = [
            ("ReadOperationCount", ctypes.c_ulonglong),
            ("WriteOperationCount", ctypes.c_ulonglong),
            ("OtherOperationCount", ctypes.c_ulonglong),
            ("ReadTransferCount", ctypes.c_ulonglong),
            ("WriteTransferCount", ctypes.c_ulonglong),
            ("OtherTransferCount", ctypes.c_ulonglong),
        ]

    class ExtendedLimitInformation(ctypes.Structure):
        _fields_ = [
            ("BasicLimitInformation", BasicLimitInformation),
            ("IoInfo", IoCounters),
            ("ProcessMemoryLimit", ctypes.c_size_t),
            ("JobMemoryLimit", ctypes.c_size_t),
            ("PeakProcessMemoryUsed", ctypes.c_size_t),
            ("PeakJobMemoryUsed", ctypes.c_size_t),
        ]

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateJobObjectW.argtypes = (ctypes.c_void_p, wintypes.LPCWSTR)
    kernel32.CreateJobObjectW.restype = wintypes.HANDLE
    kernel32.SetInformationJobObject.argtypes = (
        wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD)
    kernel32.SetInformationJobObject.restype = wintypes.BOOL
    kernel32.GetCurrentProcess.argtypes = ()
    kernel32.GetCurrentProcess.restype = wintypes.HANDLE
    kernel32.AssignProcessToJobObject.argtypes = (
        wintypes.HANDLE, wintypes.HANDLE)
    kernel32.AssignProcessToJobObject.restype = wintypes.BOOL

    try:
        job = kernel32.CreateJobObjectW(None, None)
        if not job:
            raise ctypes.WinError(ctypes.get_last_error())
        limits = ExtendedLimitInformation()
        limits.BasicLimitInformation.LimitFlags = _JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not kernel32.SetInformationJobObject(
                job, _JOB_OBJECT_EXTENDED_LIMIT_INFORMATION,
                ctypes.byref(limits), ctypes.sizeof(limits)):
            raise ctypes.WinError(ctypes.get_last_error())
        if not kernel32.AssignProcessToJobObject(
                job, kernel32.GetCurrentProcess()):
            raise ctypes.WinError(ctypes.get_last_error())
        process = subprocess.Popen(
            command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    except OSError as error:
        print(_READER_START_FAILURE + str(error), file=sys.stderr, flush=True)
        return 127

    def forward(source, destination):
        try:
            while chunk := source.read(65536):
                destination.buffer.write(chunk)
                destination.buffer.flush()
        finally:
            source.close()

    streams = (
        threading.Thread(target=forward, args=(process.stdout, sys.stdout)),
        threading.Thread(target=forward, args=(process.stderr, sys.stderr)),
    )
    for stream in streams:
        stream.start()
    return_code = process.wait()
    for stream in streams:
        stream.join()
    return return_code


def start_reader_process(command, **kwargs):
    if os.name == "nt":
        command = [
            sys.executable, os.path.abspath(__file__), "__reader_wrapper__", *command]
    else:
        # Keep reader descendants in a killable process group on POSIX.
        kwargs["start_new_session"] = True

    return subprocess.Popen(command, **kwargs)


def terminate_reader_process(process):
    process_id = getattr(process, "pid", None)
    if os.name == "nt" and process_id is not None:
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


def run_reader_command(command, timeout, max_output_bytes=None):
    process = start_reader_process(
        command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    output_limit = (
        MAX_READER_OUTPUT_BYTES if max_output_bytes is None
        else min(max_output_bytes, MAX_READER_OUTPUT_BYTES))
    stdout = bytearray()
    stderr = bytearray()
    output_size = 0
    output_lock = threading.Lock()
    output_limit_reached = threading.Event()

    def capture_output(stream, destination):
        nonlocal output_size
        try:
            while chunk := stream.read(_READER_OUTPUT_CHUNK_BYTES):
                terminate = False
                with output_lock:
                    if output_limit_reached.is_set():
                        return
                    if output_size + len(chunk) > output_limit:
                        output_limit_reached.set()
                        terminate = True
                    else:
                        destination.extend(chunk)
                        output_size += len(chunk)
                if terminate:
                    terminate_reader_process(process)
                    return
        finally:
            stream.close()

    readers = (
        threading.Thread(target=capture_output, args=(process.stdout, stdout)),
        threading.Thread(target=capture_output, args=(process.stderr, stderr)),
    )
    for reader in readers:
        reader.start()

    deadline = time.monotonic() + timeout
    try:
        process.wait(timeout=timeout)
        for reader in readers:
            reader.join(timeout=max(0, deadline - time.monotonic()))
        if any(reader.is_alive() for reader in readers):
            raise subprocess.TimeoutExpired(command, timeout)
    except subprocess.TimeoutExpired:
        terminate_reader_process(process)
        process.wait()
        for reader in readers:
            reader.join()
        raise
    if output_limit_reached.is_set():
        raise OSError(
            f"reader command exceeded the {output_limit}-byte output budget")
    stdout_bytes = bytes(stdout)
    stderr_bytes = bytes(stderr)
    if stderr_bytes.startswith(_READER_START_FAILURE.encode("utf-8")):
        raise OSError(stderr_bytes.decode("utf-8", errors="replace").strip())
    return subprocess.CompletedProcess(
        command, process.returncode, stdout_bytes, stderr_bytes)


if __name__ == "__main__" and sys.argv[1:2] == ["__reader_wrapper__"]:
    raise SystemExit(_run_windows_reader_wrapper(sys.argv[2:]))
