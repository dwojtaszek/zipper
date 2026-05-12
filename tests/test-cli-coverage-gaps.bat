@echo off
REM E2E test: CLI flags with no prior end-to-end coverage (Windows)

call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

set PASSED=0
set FAILED=0
set TEST_OUTPUT_DIR=.\results\e2e-coverage-gaps

if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

echo [ INFO ] === E2E Coverage Gap Tests ===

REM --- Utility flags ---

echo [ INFO ] Test: --benchmark exits 0
%ZIPPER_CMD% --benchmark >nul 2>&1
if not errorlevel 1 (
    echo [ INFO ] PASS: --benchmark exits 0
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: --benchmark exits 0
    set /a FAILED+=1
)

echo [ INFO ] Test: --chaos-list exits 0
%ZIPPER_CMD% --chaos-list >nul 2>&1
if not errorlevel 1 (
    echo [ INFO ] PASS: --chaos-list exits 0
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: --chaos-list exits 0
    set /a FAILED+=1
)

REM --- Multi-format generation ---

echo [ INFO ] Test: --load-file-formats dat,opt,csv
%ZIPPER_CMD% --type pdf --count 5 --output-path "%TEST_OUTPUT_DIR%\multi_format" --load-file-formats dat,opt,csv >nul 2>&1
if exist "%TEST_OUTPUT_DIR%\multi_format\*.dat" (
    echo [ INFO ] PASS: produces .dat
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: produces .dat
    set /a FAILED+=1
)

REM --- Loadfile-only delimiter flags ---

echo [ INFO ] Test: --quote-delim none
%ZIPPER_CMD% --loadfile-only --count 5 --output-path "%TEST_OUTPUT_DIR%\unquoted" --col-delim "char:|" --quote-delim none >nul 2>&1
if not errorlevel 1 (
    echo [ INFO ] PASS: --quote-delim none accepted
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: --quote-delim none
    set /a FAILED+=1
)

echo [ INFO ] Test: --load-file-format edrm-xml
%ZIPPER_CMD% --type pdf --count 5 --output-path "%TEST_OUTPUT_DIR%\edrm_xml" --load-file-format edrm-xml >nul 2>&1
if exist "%TEST_OUTPUT_DIR%\edrm_xml\*.xml" (
    echo [ INFO ] PASS: --load-file-format edrm-xml produces .xml
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: --load-file-format edrm-xml
    set /a FAILED+=1
)

REM --- Cleanup ---
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"

echo.
set /a TOTAL=!PASSED!+!FAILED!
if !FAILED! equ 0 (
    echo [ SUCCESS ] All E2E coverage gap tests passed! ^(!PASSED!/!TOTAL!^)
) else (
    echo [ ERROR ] E2E coverage gap tests: !FAILED!/!TOTAL! FAILED
    exit /b 1
)
exit /b 0
