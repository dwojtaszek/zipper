# Pre-push git hook (PowerShell) — unit tests + basic E2E smoke suite.
# For developers on Windows without Git Bash / MSYS.
#
# Installs to: .git\hooks\pre-push.ps1 and wrapped by .git\hooks\pre-push
#
# Skips unit tests if pre-commit ran them within the last 10 minutes on the
# same HEAD. Full E2E + coverage checks run in CI.
#
# To bypass (not recommended): git push --no-verify

$ErrorActionPreference = 'Stop'
$SkipWindowSeconds = 600

Write-Host "`u{250C}---------------------------------------------`u{2510}"
Write-Host "`u{2502}  Pre-push: Unit Tests + Basic E2E Smoke     `u{2502}"
Write-Host "`u{2514}---------------------------------------------`u{2518}"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "dotnet not found; skipping pre-push checks."
    exit 0
}

# Step 1: Unit tests (skip if pre-commit recently passed)
$runUnitTests = $true
$marker = '.git\zipper-hooks\pre-commit.ok'
if (Test-Path $marker) {
    $props = Get-Content $marker | ConvertFrom-StringData
    $markerTs = [int64]($props.timestamp)
    $markerHead = [string]$props.head
    $currentHead = (git rev-parse HEAD 2>$null).Trim()
    if ($markerHead -eq $currentHead) {
        $age = ([int64][double]::Parse((Get-Date -UFormat %s))) - $markerTs
        if ($age -lt $SkipWindowSeconds) {
            Write-Host "Unit tests already passed in pre-commit ($age s ago); skipping."
            $runUnitTests = $false
        }
    }
}

if ($runUnitTests) {
    Write-Host ""
    Write-Host "Running unit tests..."
    & dotnet test src/Zipper.Tests/Zipper.Tests.csproj --logger "console;verbosity=quiet" --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Unit tests failed. Push aborted."
        Write-Host "  Run 'dotnet test' for details."
        Write-Host "  To bypass: git push --no-verify"
        exit 1
    }
    Write-Host "Unit tests passed"
}

# Step 2: Basic E2E smoke suite
Write-Host ""
Write-Host "Running basic E2E smoke tests..."
if (Test-Path .\tests\run-e2e-basic.bat) {
    & cmd /c .\tests\run-e2e-basic.bat
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Basic E2E smoke tests failed. Push aborted."
        Write-Host "  Run 'tests\run-e2e-basic.bat' for details."
        Write-Host "  To bypass: git push --no-verify"
        exit 1
    }
    Write-Host "All pre-push checks passed"
} else {
    Write-Host "tests\run-e2e-basic.bat not found, skipping E2E"
}

exit 0
