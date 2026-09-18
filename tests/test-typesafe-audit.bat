@echo off
REM E2E test: TypeSafe audit runner foundation (#955).
REM Runs the Python unit suite and a no-network fixture-mode audit.

setlocal

set SCRIPT_DIR=%~dp0
set TOOL_DIR=%SCRIPT_DIR%..\tools\typesafe-audit
set TEMP_DIR=%SCRIPT_DIR%..\results\test-typesafe-audit-%RANDOM%
mkdir "%TEMP_DIR%" >nul 2>&1

echo Running TypeSafe runner unit tests...
python3 -m unittest discover -s "%TOOL_DIR%\tests" >nul 2>&1
if errorlevel 1 (
    echo [ ERROR ] TypeSafe runner unit tests failed.
    exit /b 1
)
echo [ SUCCESS ] TypeSafe runner unit tests passed.

set FIXTURE_DIR=%TEMP_DIR%\fixtures
mkdir "%FIXTURE_DIR%" >nul 2>&1

python3 "%TOOL_DIR%\record_sample_fixture.py" "%FIXTURE_DIR%" >nul 2>&1
if errorlevel 1 (
    echo [ ERROR ] Sample fixture recording failed.
    exit /b 1
)

python3 "%TOOL_DIR%\runner.py" --mode fixture --fixture-dir "%FIXTURE_DIR%" --files "%TOOL_DIR%\questions\files-sample.list" --questions "%TOOL_DIR%\questions\example.json" --json-out "%TEMP_DIR%\report.json" --md-out "%TEMP_DIR%\report.md" >nul 2>&1
if errorlevel 1 (
    echo [ ERROR ] Fixture-mode audit run failed.
    exit /b 1
)

if not exist "%TEMP_DIR%\report.json" (
    echo [ ERROR ] JSON report missing.
    exit /b 1
)

python3 "%TOOL_DIR%\runner.py" --mode fixture --fixture-dir "%FIXTURE_DIR%" --files "%TOOL_DIR%\questions\files-sample.list" --questions "%TOOL_DIR%\questions\example.json" --json-out "%TEMP_DIR%\report2.json" --md-out "%TEMP_DIR%\report2.md" >nul 2>&1
fc /b "%TEMP_DIR%\report.json" "%TEMP_DIR%\report2.json" >nul 2>&1
if errorlevel 1 (
    echo [ ERROR ] Fixture-mode output is not deterministic.
    exit /b 1
)

echo [ SUCCESS ] All TypeSafe audit E2E tests passed!

REM Note: the .sh variant additionally checks workflow structure (permissions,
REM pinning, fork safety); on Windows those gates run in the Linux lint job.
rd /s /q "%TEMP_DIR%" >nul 2>&1
exit /b 0
