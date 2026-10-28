@echo off
REM E2E test: Chaos anomaly coverage (Windows)
REM Verifies chaos mode produces anomalies for each type

setlocal DisableDelayedExpansion
call "%~dp0_zipper-cli.bat"

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

REM Test OPT encoding chaos emits a UTF-8 BOM and records the anomaly
echo [ INFO ] Test: OPT chaos with opt-encoding type
%ZIPPER_CMD% --loadfile-only --loadfile-format opt --encoding ANSI --count 100 --output-path "%TEST_OUTPUT_DIR%\opt_encoding" --chaos-mode --chaos-types opt-encoding --chaos-amount 10 --seed 42 >nul 2>&1
if not errorlevel 1 (
    set "OPT_ENCODING_FILE="
    set "OPT_ENCODING_PROPS="
    for %%F in ("%TEST_OUTPUT_DIR%\opt_encoding\*.opt") do set "OPT_ENCODING_FILE=%%~fF"
    for %%F in ("%TEST_OUTPUT_DIR%\opt_encoding\*_properties.json") do set "OPT_ENCODING_PROPS=%%~fF"
    if defined OPT_ENCODING_FILE if defined OPT_ENCODING_PROPS (
        powershell -NoProfile -Command "$b=[IO.File]::ReadAllBytes($env:OPT_ENCODING_FILE); $p=Get-Content -Raw $env:OPT_ENCODING_PROPS | ConvertFrom-Json; if ([BitConverter]::ToString($b).Contains('EF-BB-BF') -and @($p.chaosMode.injectedAnomalies | Where-Object { $_.errorType -eq 'opt-encoding' }).Count -gt 0) { exit 0 } else { exit 1 }" >nul 2>&1
        if not errorlevel 1 (
            echo [ INFO ] PASS: OPT encoding chaos emitted and audited UTF-8 BOM
            set /a PASSED+=1
        ) else (
            echo [ ERROR ] FAIL: OPT encoding chaos output or audit
            set /a FAILED+=1
        )
    ) else (
        echo [ ERROR ] FAIL: OPT encoding chaos output files missing
        set /a FAILED+=1
    )
) else (
    echo [ ERROR ] FAIL: OPT encoding chaos
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

REM Test documented platform import-failure anomalies and their audited columns
echo [ INFO ] Test: Chaos scenario realistic-import-failures
%ZIPPER_CMD% --loadfile-only --count 100 --output-path "%TEST_OUTPUT_DIR%\realistic_import_failures" --chaos-mode --chaos-scenario realistic-import-failures --seed 42 >nul 2>&1
if not errorlevel 1 (
    powershell -NoProfile -Command "$p=Get-Content -Raw (Get-ChildItem '%TEST_OUTPUT_DIR%\realistic_import_failures\*_properties.json' | Select-Object -First 1).FullName | ConvertFrom-Json; $expected=@('path-pointer','field-length','date-format','null-byte','trailing-empties'); $actual=@($p.chaosMode.injectedAnomalies | ForEach-Object { $_.errorType }); $pathAudited=@($p.chaosMode.injectedAnomalies | Where-Object { $_.errorType -eq 'path-pointer' -and $_.column -eq 'File Path' }).Count -gt 0; $pathValidated=@($p.chaosMode.injectedAnomalies | Where-Object { $_.errorType -eq 'path-pointer' -and $_.description -like 'PathReconciliation:*' }).Count -gt 0; if (@($expected | Where-Object { $_ -notin $actual }).Count -eq 0 -and $pathAudited -and $pathValidated -and @($p.chaosMode.injectedAnomalies | Where-Object { [string]::IsNullOrWhiteSpace($_.column) -or $_.column -eq 'N/A' }).Count -eq 0) { exit 0 } else { exit 1 }" >nul 2>&1
    if not errorlevel 1 (
        echo [ INFO ] PASS: realistic-import-failures injects and audits all five DAT types
        set /a PASSED+=1
    ) else (
        echo [ ERROR ] FAIL: realistic-import-failures output or audit
        set /a FAILED+=1
    )
) else (
    echo [ ERROR ] FAIL: realistic-import-failures scenario
    set /a FAILED+=1
)

REM --- Cleanup ---
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"

echo.
set /a TOTAL=%PASSED%+%FAILED%
if %FAILED% equ 0 (
    echo [ SUCCESS ] Chaos anomaly coverage tests passed! ^(%PASSED%/%TOTAL%^)
) else (
    echo [ ERROR ] Chaos anomaly tests: %FAILED%/%TOTAL% FAILED
    exit /b 1
)
exit /b 0
