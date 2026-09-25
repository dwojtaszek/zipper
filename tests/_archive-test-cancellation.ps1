# Archive Test cancellation worker for the Windows E2E workflow.
# Invoked only by tests/test-archive-test-suites.bat.

[System.IO.File]::WriteAllText($env:CANCEL_WORKER_PID_FILE, [string]$PID)

$source = @'
using System;
using System.Runtime.InteropServices;
public static class ArchiveTestCancellation
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
    $deadline = [DateTime]::UtcNow.AddSeconds([int]$env:CANCEL_STAGING_TIMEOUT_SECONDS)
    while ([DateTime]::UtcNow -lt $deadline) {
        $live = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -eq $live) {
            $signalSent = 0
            break
        }
        $staging = @(Get-ChildItem -Path "$env:CANCEL_PARENT\atc-staging-*\atc-*.zip" -File -ErrorAction SilentlyContinue)
        if ($staging.Count -gt 0) {
            $sendResult = [ArchiveTestCancellation]::Send([uint32]$processId)
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
        Start-Sleep -Milliseconds ([int]$env:CANCEL_POLL_INTERVAL_MS)
    }
    if ($signalSent -eq -1) { throw "Archive Test staging evidence did not appear" }
    $process.WaitForExit()
    [System.IO.File]::WriteAllText($env:CANCEL_RESULT_FILE, "$signalSent,$($process.ExitCode)")
    $workerExit = 0
} catch {
    [System.IO.File]::WriteAllText($env:CANCEL_ERROR_FILE, $_.Exception.ToString())
} finally {
    if ($processId -ne 0) {
        $live = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -ne $live) {
            try {
                & taskkill.exe /PID $processId /T /F 2>$null | Out-Null
                Wait-Process -Id $processId -Timeout ([int]$env:CANCEL_CLEANUP_TIMEOUT_SECONDS) -ErrorAction SilentlyContinue
            } catch {
            }
        }
    }
}
[System.IO.File]::WriteAllText($env:CANCEL_DONE_FILE, "1")
exit $workerExit
