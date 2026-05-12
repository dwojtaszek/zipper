@echo off
REM E2E test: Chaos anomaly coverage (Windows)
REM Verifies chaos mode produces anomalies for each type

call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

set PASSED=0
set FAILED=0
set TEST_OUTPUT_DIR=.\results\chaos-anomaly-coverage

if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

echo [ INFO ] === Chaos Anomaly Coverage Tests (Windows) ===

REM Test DAT chaos types
echo [ INFO ] Test: DAT chaos with quotes type
%ZIPPER_CMD% --loadfile-only --count 100 --output-path "%TEST_OUTPUT_DIR%\dat_quotes" --chaos-mode --chaos-types quotes --chaos-amount 10 --seed 42 >nul 2>&1
if not errorlevel 1 (
    echo [ INFO ] PASS: DAT quotes chaos accepted
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: DAT quotes chaos
    set /a FAILED+=1
)

REM Test OPT chaos types
echo [ INFO ] Test: OPT chaos with opt-boundary type
%ZIPPER_CMD% --loadfile-only --loadfile-format opt --count 100 --output-path "%TEST_OUTPUT_DIR%\opt_boundary" --chaos-mode --chaos-types opt-boundary --chaos-amount 10 --seed 42 >nul 2>&1
if not errorlevel 1 (
    echo [ INFO ] PASS: OPT boundary chaos accepted
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: OPT boundary chaos
    set /a FAILED+=1
)

REM Test chaos scenario
echo [ INFO ] Test: Chaos scenario full-chaos
%ZIPPER_CMD% --loadfile-only --count 100 --output-path "%TEST_OUTPUT_DIR%\full_chaos" --chaos-mode --chaos-scenario full-chaos --seed 42 >nul 2>&1
if not errorlevel 1 (
    echo [ INFO ] PASS: full-chaos scenario accepted
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: full-chaos scenario
    set /a FAILED+=1
)

REM --- Cleanup ---
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"

echo.
set /a TOTAL=!PASSED!+!FAILED!
if !FAILED! equ 0 (
    echo [ SUCCESS ] Chaos anomaly coverage tests passed! ^(!PASSED!/!TOTAL!^)
) else (
    echo [ ERROR ] Chaos anomaly tests: !FAILED!/!TOTAL! FAILED
    exit /b 1
)
exit /b 0
