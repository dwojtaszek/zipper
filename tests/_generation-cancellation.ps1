# Generation cancellation worker for the Windows E2E workflow.
# Invoked by tests/run-e2e-loadfile.bat and tests/test-production-sets.bat.

[System.IO.File]::WriteAllText($env:CANCEL_WORKER_PID_FILE, [string]$PID)

$source = @'
using System;
using System.Runtime.InteropServices;
public static class GenerationCancellation
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, bool add);
    public static int Send(uint processId)
    {
        FreeConsole();
        if (AttachConsole(processId) == false) return 4;
        SetConsoleCtrlHandler(IntPtr.Zero, true);
        bool sent = GenerateConsoleCtrlEvent(0, 0);
        FreeConsole();
        return sent ? 0 : 3;
    }
}
'@

$process = $null
$processId = 0
$signalSent = -1
$workerExit = 1
try {
    Add-Type -TypeDefinition $source
    $process = Start-Process -FilePath $env:CANCEL_EXE -ArgumentList $env:CANCEL_ARGS -WorkingDirectory $env:CANCEL_WORKDIR -WindowStyle Hidden -PassThru
    $processId = $process.Id
    [System.IO.File]::WriteAllText($env:CANCEL_PID_FILE, [string]$processId)
    $timeoutSeconds = if ($env:CANCEL_STAGING_TIMEOUT_SECONDS) { [int]$env:CANCEL_STAGING_TIMEOUT_SECONDS } else { 30 }
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    $pollInterval = if ($env:CANCEL_POLL_INTERVAL_MS) { [int]$env:CANCEL_POLL_INTERVAL_MS } else { 20 }
    while ([DateTime]::UtcNow -lt $deadline) {
        $live = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -eq $live) {
            $signalSent = 0
            break
        }
        $evidence = @(Get-ChildItem -Path $env:CANCEL_EVIDENCE_PATH -Filter $env:CANCEL_EVIDENCE_FILTER -Recurse -File -ErrorAction SilentlyContinue)
        if ($evidence.Count -gt 0) {
            $sendResult = [GenerationCancellation]::Send([uint32]$processId)
            if ($sendResult -eq 0) {
                $signalSent = 1
            } else {
                $liveAfterSend = Get-Process -Id $processId -ErrorAction SilentlyContinue
                if ($null -eq $liveAfterSend) {
                    $signalSent = 0
                } else {
                    throw "GenerateConsoleCtrlEvent failed with $sendResult"
                }
            }
            break
        }
        Start-Sleep -Milliseconds $pollInterval
    }
    if ($signalSent -eq -1) { throw "Generation evidence did not appear within deadline" }
    $cleanupTimeoutMs = if ($env:CANCEL_CLEANUP_TIMEOUT_SECONDS) { [int]$env:CANCEL_CLEANUP_TIMEOUT_SECONDS * 1000 } else { 15000 }
    if (-not $process.WaitForExit($cleanupTimeoutMs)) {
        throw "Process $processId failed to exit within $cleanupTimeoutMs ms after cancellation signal"
    }
    [System.IO.File]::WriteAllText($env:CANCEL_RESULT_FILE, "$signalSent,$($process.ExitCode)")
    $workerExit = 0
} catch {
    [System.IO.File]::WriteAllText($env:CANCEL_ERROR_FILE, $_.Exception.ToString())
} finally {
    if ($processId -ne 0) {
        $live = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -ne $live) {
            try {
                $cleanupTimeout = if ($env:CANCEL_CLEANUP_TIMEOUT_SECONDS) { [int]$env:CANCEL_CLEANUP_TIMEOUT_SECONDS } else { 10 }
                & taskkill.exe /PID $processId /T /F 2>$null | Out-Null
                Wait-Process -Id $processId -Timeout $cleanupTimeout -ErrorAction SilentlyContinue
            } catch {
            }
        }
    }
}
[System.IO.File]::WriteAllText($env:CANCEL_DONE_FILE, "1")
exit $workerExit
