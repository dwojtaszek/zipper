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

echo [ INFO ] Test: --with-families accepted with EML + attachments
%ZIPPER_CMD% --type eml --count 10 --output-path "%TEST_OUTPUT_DIR%\families" --with-families --attachment-rate 50 2> "%TEMP%\no_warn.txt" >nul
set ZIPPER_EXIT=!ERRORLEVEL!
if !ZIPPER_EXIT! equ 0 (
    findstr /i /c:"warning: --with-families" "%TEMP%\no_warn.txt" >nul 2>&1
    if errorlevel 1 (
        if exist "%TEST_OUTPUT_DIR%\families\*.dat" (
            echo [ INFO ] PASS: --with-families accepted without warning
            set /a PASSED+=1
        ) else (
            echo [ ERROR ] FAIL: --with-families (no output produced)
            set /a FAILED+=1
        )
    ) else (
        echo [ ERROR ] FAIL: --with-families incorrectly emitted warning for valid config
        set /a FAILED+=1
    )
) else (
    echo [ ERROR ] FAIL: --with-families command failed with exit code !ZIPPER_EXIT!
    set /a FAILED+=1
)
del "%TEMP%\no_warn.txt" 2>nul

echo [ INFO ] Test: --with-families warning emitted without --type eml
%ZIPPER_CMD% --type pdf --count 5 --output-path "%TEST_OUTPUT_DIR%\families-warn1" --with-families 2> "%TEMP%\warn1.txt" >nul
set ZIPPER_EXIT=!ERRORLEVEL!
if !ZIPPER_EXIT! equ 0 (
    findstr /i /c:"warning: --with-families" "%TEMP%\warn1.txt" >nul 2>&1
    if not errorlevel 1 (
        echo [ INFO ] PASS: --with-families warning emitted for non-eml type
        set /a PASSED+=1
    ) else (
        echo [ ERROR ] FAIL: --with-families warning not emitted for non-eml type
        set /a FAILED+=1
    )
) else (
    echo [ ERROR ] FAIL: --with-families command failed with exit code !ZIPPER_EXIT!
    set /a FAILED+=1
)
del "%TEMP%\warn1.txt" 2>nul

echo [ INFO ] Test: --with-families warning emitted with --attachment-rate 0
%ZIPPER_CMD% --type eml --count 5 --output-path "%TEST_OUTPUT_DIR%\families-warn2" --with-families --attachment-rate 0 2> "%TEMP%\warn2.txt" >nul
set ZIPPER_EXIT=!ERRORLEVEL!
if !ZIPPER_EXIT! equ 0 (
    findstr /i /c:"warning: --with-families" "%TEMP%\warn2.txt" >nul 2>&1
    if not errorlevel 1 (
        echo [ INFO ] PASS: --with-families warning emitted for attachment-rate 0
        set /a PASSED+=1
    ) else (
        echo [ ERROR ] FAIL: --with-families warning not emitted for attachment-rate 0
        set /a FAILED+=1
    )
) else (
    echo [ ERROR ] FAIL: --with-families command failed with exit code !ZIPPER_EXIT!
    set /a FAILED+=1
)
del "%TEMP%\warn2.txt" 2>nul

echo [ INFO ] Test: --with-families warning emitted with --loadfile-only
%ZIPPER_CMD% --type eml --count 5 --output-path "%TEST_OUTPUT_DIR%\families-warn3" --with-families --attachment-rate 50 --loadfile-only 2> "%TEMP%\warn3.txt" >nul
set ZIPPER_EXIT=!ERRORLEVEL!
if !ZIPPER_EXIT! equ 0 (
    findstr /i /c:"warning: --with-families" "%TEMP%\warn3.txt" >nul 2>&1
    if not errorlevel 1 (
        echo [ INFO ] PASS: --with-families warning emitted for loadfile-only mode
        set /a PASSED+=1
    ) else (
        echo [ ERROR ] FAIL: --with-families warning not emitted for loadfile-only mode
        set /a FAILED+=1
    )
) else (
    echo [ ERROR ] FAIL: --with-families command failed with exit code !ZIPPER_EXIT!
    set /a FAILED+=1
)
del "%TEMP%\warn3.txt" 2>nul

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
