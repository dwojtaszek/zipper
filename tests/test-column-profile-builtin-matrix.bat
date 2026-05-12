@echo off
REM E2E test: Column profile built-in matrix (Windows)
REM Verifies all built-in profiles produce valid output for each file type

call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

set PASSED=0
set FAILED=0
set TEST_OUTPUT_DIR=.\results\column-profile-matrix

if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

echo [ INFO ] === Column Profile Built-in Matrix Tests (Windows) ===

for %%P in (minimal standard litigation full) do (
    for %%T in (pdf tiff) do (
        echo [ INFO ] Test: %%P/%%T
        %ZIPPER_CMD% --type %%T --count 5 --output-path "%TEST_OUTPUT_DIR%\%%P_%%T" --column-profile %%P --seed 42 >nul 2>&1
        if not errorlevel 1 (
            echo [ INFO ] PASS: %%P/%%T
            set /a PASSED+=1
        ) else (
            echo [ ERROR ] FAIL: %%P/%%T
            set /a FAILED+=1
        )
    )
)

REM --- Cleanup ---
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"

echo.
set /a TOTAL=!PASSED!+!FAILED!
if !FAILED! equ 0 (
    echo [ SUCCESS ] Column profile matrix tests passed! ^(!PASSED!/!TOTAL!^)
) else (
    echo [ ERROR ] Column profile matrix tests: !FAILED!/!TOTAL! FAILED
    exit /b 1
)
exit /b 0
