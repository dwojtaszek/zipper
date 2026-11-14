param(
    [Parameter(Mandatory = $true)][string]$RunnerBat,
    [int]$Jobs = 0
)

$ErrorActionPreference = 'Stop'
if ($Jobs -le 0) {
    $cpu = [Environment]::ProcessorCount
    $Jobs = if ($cpu -gt 4) { 4 } elseif ($cpu -lt 1) { 1 } else { $cpu }
}

$forceSerial = @(
    'test-artifact-handling.bat',
    'test-run-tests-guard-parity.bat',
    'test-ci-build-props.bat',
    'test-run-tests-fatal.bat'
)

$scripts = New-Object System.Collections.Generic.List[string]
foreach ($raw in Get-Content -Path $RunnerBat -Encoding UTF8) {
    $line = $raw.Trim()
    if ($line -match '^(?i)(?:REM|::)') { continue }
    if ($line -match '(?i)^(?:@)?call\s+"?\.\\tests\\([A-Za-z0-9._-]+\.bat)"?') {
        $name = $Matches[1]
        if (-not $scripts.Contains($name)) { $scripts.Add($name) }
    }
}
if ($scripts.Count -eq 0) {
    Write-Output "[ ERROR ] No standalone .bat test suites discovered in $RunnerBat."
    exit 1
}
if (-not $env:ZIPPER_EXECUTABLE -and $env:ZIPPER_CMD) {
    $candidateExe = $env:ZIPPER_CMD.Trim('"')
    if ($candidateExe -match '\.exe$' -and (Test-Path $candidateExe)) {
        $env:ZIPPER_EXECUTABLE = $candidateExe
    }
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ("zipper-bat-parallel-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$active = New-Object System.Collections.Generic.List[hashtable]
$failed = New-Object System.Collections.Generic.List[string]

function Complete-FinishedSuite([bool]$WaitOne) {
    if ($active.Count -eq 0) { return }
    $done = $null
    while ($null -eq $done) {
        for ($i = 0; $i -lt $active.Count; $i++) {
            if ($active[$i].Proc.HasExited) {
                $done = $active[$i]
                $active.RemoveAt($i)
                break
            }
        }
        if ($null -eq $done) {
            if (-not $WaitOne) { return }
            Start-Sleep -Milliseconds 150
        }
    }
    $done.Proc.WaitForExit()
    $rc = $done.Proc.ExitCode
    $done.Proc.Dispose()
    Write-Output "[ INFO ] === Completed $($done.Name) (exit=$rc) ==="
    if (Test-Path $done.Log) {
        Get-Content -Path $done.Log | ForEach-Object { Write-Output $_ }
    }
    if ($rc -ne 0) {
        Write-Output "[ ERROR ] $($done.Name) failed with exit code $rc."
        $failed.Add($done.Name)
    }
}

try {
    $idx = 0
    foreach ($name in $scripts) {
        $isSerial = $forceSerial -contains $name.ToLowerInvariant()
        if ($isSerial) {
            while ($active.Count -gt 0) { Complete-FinishedSuite $true }
            Write-Output "[ INFO ] Running $name (serial)..."
            & cmd.exe /c "call .\tests\$name"
            if ($LASTEXITCODE -ne 0) {
                Write-Output "[ ERROR ] $name failed with exit code $LASTEXITCODE."
                $failed.Add($name)
            }
            continue
        }
        while ($active.Count -ge $Jobs) { Complete-FinishedSuite $true }
        $idx++
        $wTmp = Join-Path $scratch "tmp_$idx"
        $wLog = Join-Path $scratch "suite_$idx.log"
        New-Item -ItemType Directory -Path $wTmp -Force | Out-Null
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = 'cmd.exe'
        $psi.Arguments = "/c call "".\tests\$name"" > ""$wLog"" 2>&1"
        $psi.WorkingDirectory = (Get-Location).Path
        $psi.UseShellExecute = $false
        $psi.EnvironmentVariables['TEMP'] = $wTmp
        $psi.EnvironmentVariables['TMP'] = $wTmp
        $proc = [System.Diagnostics.Process]::Start($psi)
        $active.Add(@{ Name = $name; Proc = $proc; Log = $wLog })
    }
    while ($active.Count -gt 0) { Complete-FinishedSuite $true }
} finally {
    if (Test-Path $scratch) {
        Remove-Item -Path $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($failed.Count -gt 0) {
    Write-Output "[ ERROR ] Failed Windows E2E suites: $($failed -join ', ')"
    exit 1
}
exit 0
