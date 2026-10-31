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

echo [ INFO ] Test: --benchmark runs and produces output
%ZIPPER_CMD% --benchmark >nul 2>&1
if not errorlevel 2 (
    echo [ INFO ] PASS: --benchmark ran (exit 0 or 1 acceptable)
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: --benchmark crashed (exit errorlevel %errorlevel%)
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
    findstr /i /c:"Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified." "%TEMP%\warn1.txt" >nul 2>&1
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
    findstr /i /c:"Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified." "%TEMP%\warn2.txt" >nul 2>&1
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
    findstr /i /c:"Warning: --with-families has no effect in --loadfile-only mode." "%TEMP%\warn3.txt" >nul 2>&1
    if errorlevel 1 (
        echo [ INFO ] PASS: --with-families is supported in --loadfile-only mode without warning
        set /a PASSED+=1
    ) else (
        echo [ ERROR ] FAIL: --with-families warning emitted for loadfile-only mode
        set /a FAILED+=1
    )
) else (
    echo [ ERROR ] FAIL: --with-families command failed with exit code !ZIPPER_EXIT!
    set /a FAILED+=1
)
del "%TEMP%\warn3.txt" 2>nul

echo [ INFO ] Test: --with-threading warning emitted for non-eml type
%ZIPPER_CMD% --type pdf --count 5 --output-path "%TEST_OUTPUT_DIR%\threading-warn1" --with-threading 2> "%TEMP%\warn_thread.txt" >nul
set ZIPPER_EXIT=!ERRORLEVEL!
if !ZIPPER_EXIT! equ 0 (
    findstr /i /c:"Warning: --with-threading is only meaningful when --type eml (or eml participates in --types) is specified." "%TEMP%\warn_thread.txt" >nul 2>&1
    if not errorlevel 1 (
        echo [ INFO ] PASS: --with-threading warning emitted for non-eml type
        set /a PASSED+=1
    ) else (
        echo [ ERROR ] FAIL: --with-threading warning NOT emitted for non-eml type
        set /a FAILED+=1
    )
) else (
    echo [ ERROR ] FAIL: --with-threading non-eml type command failed with exit code !ZIPPER_EXIT!
    set /a FAILED+=1
)
del "%TEMP%\warn_thread.txt" 2>nul

echo [ INFO ] Test: --with-threading produces thread columns in DAT
%ZIPPER_CMD% --type eml --count 5 --seed 42 --output-path "%TEST_OUTPUT_DIR%\threading-gen" --with-threading >nul 2>&1
set ZIPPER_EXIT=!ERRORLEVEL!
if !ZIPPER_EXIT! equ 0 (
    set THREAD_DAT_FOUND=0
    for /r "%TEST_OUTPUT_DIR%\threading-gen" %%F in (*.dat) do (
        set THREAD_DAT_FOUND=1
        findstr /i /c:"Thread ID" "%%F" >nul 2>&1
        if not errorlevel 1 (
            findstr /i /c:"DOC00000001" "%%F" >nul 2>&1
            if not errorlevel 1 (
                echo [ INFO ] PASS: --with-threading populates thread columns in DAT
                set /a PASSED+=1
            ) else (
                echo [ ERROR ] FAIL: --with-threading thread columns empty in DAT
                set /a FAILED+=1
            )
        ) else (
            echo [ ERROR ] FAIL: --with-threading missing thread columns in DAT
            set /a FAILED+=1
        )
    )
    if !THREAD_DAT_FOUND! equ 0 (
        echo [ ERROR ] FAIL: --with-threading produced no DAT file
        set /a FAILED+=1
    )
) else (
    echo [ ERROR ] FAIL: --with-threading eml generation command failed with exit code !ZIPPER_EXIT!
    set /a FAILED+=1
)

echo [ INFO ] Test: --mime-realism warning emitted for non-eml type
set "ERR_FILE=%TEMP%\zipper_realism_err_%RANDOM%.txt"
call "%ZIPPER_CMD%" --type pdf --count 5 --output-path "%TEST_OUTPUT_DIR%\mime-realism-warn1" --mime-realism standard 2> "%ERR_FILE%"
set "ZIPPER_EXIT=!ERRORLEVEL!"
if !ZIPPER_EXIT! equ 0 (
    findstr /c:"Warning: --mime-realism is only meaningful when --type eml (or eml participates in --types) is specified." "%ERR_FILE%" > nul 2>&1
    if !ERRORLEVEL! equ 0 (
        echo [ PASS ] PASS: --mime-realism warning emitted for non-eml type
        set /a PASSED+=1
    ) else (
        echo [ ERROR ] FAIL: --mime-realism warning NOT emitted for non-eml type
        set /a FAILED+=1
    )
) else (
    echo [ ERROR ] FAIL: --mime-realism non-eml type command failed with exit code !ZIPPER_EXIT!
    set /a FAILED+=1
)
if exist "%ERR_FILE%" del /f /q "%ERR_FILE%"

set "MIME_BASELINE_OUTPUT=%TEST_OUTPUT_DIR%\mime-realism-baseline"
set "MIME_BASELINE_ZIP="
call "%ZIPPER_CMD%" --type eml --count 32 --seed 42 --attachment-rate 100 --output-path "!MIME_BASELINE_OUTPUT!" >nul 2>&1
set "ZIPPER_EXIT=!ERRORLEVEL!"
if !ZIPPER_EXIT! equ 0 (
    for %%F in ("!MIME_BASELINE_OUTPUT!\*.zip") do if exist "%%~fF" set "MIME_BASELINE_ZIP=%%~fF"
)
if defined MIME_BASELINE_ZIP (
    echo [ PASS ] PASS: --mime-realism body-fidelity baseline generated
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: --mime-realism body-fidelity baseline generation failed
    set /a FAILED+=1
)

for %%M in (standard full) do (
    set "MIME_MODE=%%M"
    set "MIME_OUTPUT=%TEST_OUTPUT_DIR%\mime-realism-!MIME_MODE!"
    echo [ INFO ] Test: --mime-realism !MIME_MODE! produces parser-readable MIME structures
    call "%ZIPPER_CMD%" --type eml --count 32 --seed 42 --attachment-rate 100 --output-path "!MIME_OUTPUT!" --mime-realism !MIME_MODE! >nul 2>&1
    set "ZIPPER_EXIT=!ERRORLEVEL!"
    if !ZIPPER_EXIT! equ 0 (
        set "MIME_ZIP="
        set "MIME_DAT="
        for %%F in ("!MIME_OUTPUT!\*.zip") do if exist "%%~fF" set "MIME_ZIP=%%~fF"
        for %%F in ("!MIME_OUTPUT!\*.dat") do if exist "%%~fF" set "MIME_DAT=%%~fF"
        if defined MIME_ZIP if defined MIME_DAT (
            python "%~dp0validate_mime_realism.py" !MIME_MODE! "!MIME_ZIP!" "!MIME_DAT!" "!MIME_BASELINE_ZIP!" >nul
            if errorlevel 1 (
                echo [ ERROR ] FAIL: --mime-realism !MIME_MODE! failed MIME parser validation
                set /a FAILED+=1
            ) else (
                echo [ PASS ] PASS: --mime-realism !MIME_MODE! passes the standard MIME parser checks
                set /a PASSED+=1
            )
        ) else (
            echo [ ERROR ] FAIL: --mime-realism !MIME_MODE! archive or DAT file not found
            set /a FAILED+=1
        )
    ) else (
        echo [ ERROR ] FAIL: --mime-realism !MIME_MODE! generation failed with exit code !ZIPPER_EXIT!
        set /a FAILED+=1
    )
)

set "MIME_FAMILY_OUTPUT=%TEST_OUTPUT_DIR%\mime-realism-families"
echo [ INFO ] Test: --mime-realism standard emits a Document Family child for every MIME Attachment
call "%ZIPPER_CMD%" --type eml --count 32 --seed 42 --attachment-rate 100 --with-families --with-text --output-path "!MIME_FAMILY_OUTPUT!" --mime-realism standard >nul 2>&1
set "ZIPPER_EXIT=!ERRORLEVEL!"
if !ZIPPER_EXIT! equ 0 (
    set "MIME_FAMILY_ZIP="
    set "MIME_FAMILY_DAT="
    for %%F in ("!MIME_FAMILY_OUTPUT!\*.zip") do if exist "%%~fF" set "MIME_FAMILY_ZIP=%%~fF"
    for %%F in ("!MIME_FAMILY_OUTPUT!\*.dat") do if exist "%%~fF" set "MIME_FAMILY_DAT=%%~fF"
    if defined MIME_FAMILY_ZIP if defined MIME_FAMILY_DAT if defined MIME_BASELINE_ZIP (
        python "%~dp0validate_mime_realism.py" standard "!MIME_FAMILY_ZIP!" "!MIME_FAMILY_DAT!" "!MIME_BASELINE_ZIP!" --families >nul
        if errorlevel 1 (
            echo [ ERROR ] FAIL: --with-families failed MIME Attachment child validation
            set /a FAILED+=1
        ) else (
            echo [ PASS ] PASS: --with-families includes every MIME Attachment as a child record
            set /a PASSED+=1
        )
    ) else (
        echo [ ERROR ] FAIL: --with-families archive, DAT file, or baseline not found
        set /a FAILED+=1
    )
) else (
    echo [ ERROR ] FAIL: --mime-realism standard with --with-families generation failed with exit code !ZIPPER_EXIT!
    set /a FAILED+=1
)

set "MIME_FULL_FAMILY_OUTPUT=%TEST_OUTPUT_DIR%\mime-realism-full-families"
echo [ INFO ] Test: --mime-realism full emits family children for nested and placeholder MIME Attachments
call "%ZIPPER_CMD%" --type eml --count 32 --seed 42 --attachment-rate 100 --with-families --with-text --output-path "!MIME_FULL_FAMILY_OUTPUT!" --mime-realism full >nul 2>&1
set "ZIPPER_EXIT=!ERRORLEVEL!"
if !ZIPPER_EXIT! equ 0 (
    set "MIME_FULL_FAMILY_ZIP="
    set "MIME_FULL_FAMILY_DAT="
    for %%F in ("!MIME_FULL_FAMILY_OUTPUT!\*.zip") do if exist "%%~fF" set "MIME_FULL_FAMILY_ZIP=%%~fF"
    for %%F in ("!MIME_FULL_FAMILY_OUTPUT!\*.dat") do if exist "%%~fF" set "MIME_FULL_FAMILY_DAT=%%~fF"
    if defined MIME_FULL_FAMILY_ZIP if defined MIME_FULL_FAMILY_DAT if defined MIME_BASELINE_ZIP (
        python "%~dp0validate_mime_realism.py" full "!MIME_FULL_FAMILY_ZIP!" "!MIME_FULL_FAMILY_DAT!" "!MIME_BASELINE_ZIP!" --families >nul
        if errorlevel 1 (
            echo [ ERROR ] FAIL: --mime-realism full failed nested MIME Attachment child validation
            set /a FAILED+=1
        ) else (
            echo [ PASS ] PASS: --mime-realism full includes every nested and placeholder Attachment as a child
            set /a PASSED+=1
        )
    ) else (
        echo [ ERROR ] FAIL: --mime-realism full family archive, DAT file, or baseline not found
        set /a FAILED+=1
    )
) else (
    echo [ ERROR ] FAIL: --mime-realism full with --with-families generation failed with exit code !ZIPPER_EXIT!
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
