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

REM Test format-aware chaos scenarios (CSV/Concordance/EDRM-XML)
call :assert_chaos_scenario "csv-escaping-failures" "csv" "csv-quote-escape,csv-embedded-newline,csv-bom"
call :assert_chaos_scenario "concordance-import-failures" "concordance" "concordance-delimiter,concordance-quote-escape"
call :assert_chaos_scenario "edrm-xml-structural-failures" "edrm-xml" "xml-unclosed-tag,xml-bad-entity,xml-encoding-lie"

call :assert_cross_format "cross-format-desync"
call :assert_cross_format "broken-families"

call :adversarial_values

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

:adversarial_values
echo [ INFO ] Test: Chaos scenario adversarial-values
set "PYCMD="
python3 --version >nul 2>&1 && set "PYCMD=python3"
if not defined PYCMD python --version >nul 2>&1 && set "PYCMD=python"
if not defined PYCMD goto :adversarial_values_failed
%ZIPPER_CMD% --loadfile-only --count 6 --output-path "%TEST_OUTPUT_DIR%\adversarial_values" --chaos-mode --chaos-scenario adversarial-values --seed 42 >nul 2>&1
if errorlevel 1 goto :adversarial_values_failed
set "ADVERSARIAL_DAT="
for %%F in ("%TEST_OUTPUT_DIR%\adversarial_values\*.dat") do set "ADVERSARIAL_DAT=%%~fF"
if not defined ADVERSARIAL_DAT goto :adversarial_values_failed
%PYCMD% "%~dp0validate_adversarial_values.py" "%ADVERSARIAL_DAT%" 6 >nul 2>&1
if errorlevel 1 goto :adversarial_values_failed

%ZIPPER_CMD% --loadfile-only --count 0 --output-path "%TEST_OUTPUT_DIR%\adversarial_values_header" --chaos-mode --chaos-scenario adversarial-values --seed 42 >nul 2>&1
if errorlevel 1 goto :adversarial_values_failed
set "ADVERSARIAL_HEADER="
for %%F in ("%TEST_OUTPUT_DIR%\adversarial_values_header\*.dat") do set "ADVERSARIAL_HEADER=%%~fF"
if not defined ADVERSARIAL_HEADER goto :adversarial_values_failed
%PYCMD% "%~dp0validate_adversarial_values.py" "%ADVERSARIAL_HEADER%" 0 >nul 2>&1
if errorlevel 1 goto :adversarial_values_failed

echo [ INFO ] PASS: adversarial-values emits valid tail values and header-only output
set /a PASSED+=1
goto :eof

:adversarial_values_failed
echo [ ERROR ] FAIL: adversarial-values output or parser validity
set /a FAILED+=1
goto :eof

:assert_chaos_scenario
set "SCEN=%~1"
set "FMT=%~2"
set "EXPECTED=%~3"
set "SCEN_DIR=%TEST_OUTPUT_DIR%\scenario_%SCEN%"
%ZIPPER_CMD% --loadfile-only --loadfile-format %FMT% --count 100 --output-path "%SCEN_DIR%" --chaos-mode --chaos-scenario "%SCEN%" --seed 42 >nul 2>&1
if errorlevel 1 (
    echo [ ERROR ] FAIL: %SCEN% scenario rejected
    set /a FAILED+=1
    goto :eof
)
powershell -NoProfile -Command "$p=Get-Content -Raw (Get-ChildItem '%SCEN_DIR%\*_properties.json' | Select-Object -First 1).FullName | ConvertFrom-Json; $expected='%EXPECTED%'.Split(','); $actual=@($p.chaosMode.injectedAnomalies | ForEach-Object { $_.errorType }); if (@($expected | Where-Object { $_ -notin $actual }).Count -eq 0 -and $p.chaosMode.totalAnomalies -gt 0) { exit 0 } else { exit 1 }" >nul 2>&1
if not errorlevel 1 (
    echo [ INFO ] PASS: %SCEN% injects and audits its format-specific anomalies
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: %SCEN% output or audit
    set /a FAILED+=1
)
goto :eof

:assert_cross_format
set "SCEN=%~1"
set "SCEN_DIR=%TEST_OUTPUT_DIR%\cross_format_%SCEN%"
%ZIPPER_CMD% --loadfile-only --load-file-formats dat,opt --count 100 --output-path "%SCEN_DIR%\run1" --chaos-mode --chaos-scenario "%SCEN%" --seed 42 >nul 2>&1
if errorlevel 1 (
    echo [ ERROR ] FAIL: %SCEN% cross-format scenario rejected
    set /a FAILED+=1
    goto :eof
)
set "DAT_FILE="
set "OPT_FILE="
set "DAT_PROPS="
set "OPT_PROPS="
for %%F in ("%SCEN_DIR%\run1\*.dat") do set "DAT_FILE=%%~fF"
for %%F in ("%SCEN_DIR%\run1\*.opt") do set "OPT_FILE=%%~fF"
for %%F in ("%SCEN_DIR%\run1\*_properties.json") do (
    echo %%~nxF | findstr /i /e ".opt_properties.json" >nul
    if errorlevel 1 (set "DAT_PROPS=%%~fF") else (set "OPT_PROPS=%%~fF")
)
if not defined DAT_FILE goto :cross_format_failed
if not defined OPT_FILE goto :cross_format_failed
if not defined DAT_PROPS goto :cross_format_failed
if not defined OPT_PROPS goto :cross_format_failed
powershell -NoProfile -Command "$d=Get-Content -Raw '%DAT_PROPS%' | ConvertFrom-Json; $o=Get-Content -Raw '%OPT_PROPS%' | ConvertFrom-Json; $dOk=$d.fileName -like '*.dat' -and $d.chaosMode.totalAnomalies -gt 0; $oOk=$o.fileName -like '*.opt' -and $o.chaosMode.totalAnomalies -gt 0; $peers=@($d.chaosMode.injectedAnomalies + $o.chaosMode.injectedAnomalies | Where-Object { -not $_.file -or -not $_.peer -or -not $_.peer.file -or -not $_.peer.lineNumber }); if ($dOk -and $oOk -and $peers.Count -eq 0) { exit 0 } else { exit 1 }" >nul 2>&1
if errorlevel 1 goto :cross_format_failed

%ZIPPER_CMD% --loadfile-only --load-file-formats dat,opt --count 100 --output-path "%SCEN_DIR%\run2" --chaos-mode --chaos-scenario "%SCEN%" --seed 42 >nul 2>&1
if errorlevel 1 goto :cross_format_failed
set "DAT_FILE2="
set "OPT_FILE2="
for %%F in ("%SCEN_DIR%\run2\*.dat") do set "DAT_FILE2=%%~fF"
for %%F in ("%SCEN_DIR%\run2\*.opt") do set "OPT_FILE2=%%~fF"
if not defined DAT_FILE2 goto :cross_format_failed
if not defined OPT_FILE2 goto :cross_format_failed
fc /b "%DAT_FILE%" "%DAT_FILE2%" >nul 2>&1
if errorlevel 1 goto :cross_format_failed
fc /b "%OPT_FILE%" "%OPT_FILE2%" >nul 2>&1
if errorlevel 1 goto :cross_format_failed

echo [ INFO ] PASS: %SCEN% corrupts and audits both DAT and OPT coherently
set /a PASSED+=1
goto :eof

:cross_format_failed
echo [ ERROR ] FAIL: %SCEN% cross-format output or audit
set /a FAILED+=1
goto :eof
