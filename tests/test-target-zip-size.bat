@echo off
REM E2E test: --target-zip-size accuracy (Windows)
REM Smoke test only - full tolerance checks require python3 (run on Linux CI)

call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

set PASSED=0
set FAILED=0
set TEST_OUTPUT_DIR=.\results\target-zip-size

if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

echo [ INFO ] === Target-zip-size Smoke Tests (Windows) ===

echo [ INFO ] Test: 10MB/100/pdf generates a zip
%ZIPPER_CMD% --type pdf --count 100 --target-zip-size 10MB --output-path "%TEST_OUTPUT_DIR%\10mb" >nul 2>&1
if exist "%TEST_OUTPUT_DIR%\10mb\*.zip" (
    echo [ INFO ] PASS: 10MB/100/pdf produces zip
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: 10MB/100/pdf no zip
    set /a FAILED+=1
)

echo [ INFO ] Test: 100MB/500/tiff generates a zip
%ZIPPER_CMD% --type tiff --count 500 --target-zip-size 100MB --output-path "%TEST_OUTPUT_DIR%\100mb" >nul 2>&1
if exist "%TEST_OUTPUT_DIR%\100mb\*.zip" (
    echo [ INFO ] PASS: 100MB/500/tiff produces zip
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: 100MB/500/tiff no zip
    set /a FAILED+=1
)

REM --- Cleanup ---
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"

echo.
set /a TOTAL=!PASSED!+!FAILED!
if !FAILED! equ 0 (
    echo [ SUCCESS ] Target-zip-size smoke tests passed! ^(!PASSED!/!TOTAL!^)
) else (
    echo [ ERROR ] Target-zip-size tests: !FAILED!/!TOTAL! FAILED
    exit /b 1
)
exit /b 0
