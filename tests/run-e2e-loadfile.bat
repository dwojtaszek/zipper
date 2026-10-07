@echo off
setlocal enabledelayedexpansion

REM E2E tests for --loadfile-only and Chaos Engine features.
REM Builds the binary ONCE and reuses it.
REM
REM Covers: DAT loadfile-only, OPT loadfile-only, custom delimiters, EOL,
REM         chaos mode, properties JSON, dependency rejection.

REM --- Configuration ---

set TEST_OUTPUT_DIR=.\results\e2e-loadfile
set PROJECT=src\Zipper.csproj
set BUILD_DIR=src\bin\Release\net10.0

REM --- Build Once ---

echo [ INFO ] Building project (one-time)...
dotnet build %PROJECT% -c Release --nologo -v quiet 2>nul
if errorlevel 1 (
    echo [ ERROR ] Build failed. Run 'dotnet build %PROJECT% -c Release' for details.
    exit /b 1
)

REM Resolve binary path
if exist "%BUILD_DIR%\Zipper.exe" (
    set BINARY=%BUILD_DIR%\Zipper.exe
) else (
    set BINARY=dotnet run --project %PROJECT% --no-build -c Release --
)
echo [ INFO ] Using binary: %BINARY%

REM --- Setup ---

if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

set TESTS_PASSED=0
set TESTS_TOTAL=8

REM ================================================================
REM Test 1: Basic DAT loadfile-only generation
REM ================================================================
echo [ INFO ] START: DAT loadfile-only
%BINARY% --loadfile-only --count 100 --output-path "%TEST_OUTPUT_DIR%\dat_basic"
if errorlevel 1 (
    echo [ ERROR ] Test 1 failed: DAT loadfile-only execution
    goto :cleanup
)

set "dat_file="
for %%F in ("%TEST_OUTPUT_DIR%\dat_basic\*.dat") do set "dat_file=%%F"
if not defined dat_file (
    echo [ ERROR ] No .dat file found
    goto :cleanup
)

REM Verify line count
powershell -Command "(Get-Content -Path '%dat_file%').Count" > "%temp%\line_count.txt"
set /p line_count=<"%temp%\line_count.txt"
if "!line_count!" neq "101" (
    echo [ ERROR ] DAT line count: expected 101, got !line_count!
    goto :cleanup
)
echo [ INFO ] DAT line count OK (!line_count!)

REM Verify no ZIP was created
set "zip_file="
for %%F in ("%TEST_OUTPUT_DIR%\dat_basic\*.zip") do set "zip_file=%%F"
if defined zip_file (
    echo [ ERROR ] Expected no .zip file in loadfile-only mode
    goto :cleanup
)
echo [ INFO ] No ZIP file created (correct)

REM Verify properties JSON
set "props_file="
for %%F in ("%TEST_OUTPUT_DIR%\dat_basic\*_properties.json") do set "props_file=%%F"
if not defined props_file (
    echo [ ERROR ] No _properties.json file found
    goto :cleanup
)
findstr /c:"\"format\"" "%props_file%" >nul || ( echo [ ERROR ] Properties JSON missing format field & goto :cleanup )
findstr /c:"\"totalRecords\"" "%props_file%" >nul || ( echo [ ERROR ] Properties JSON missing totalRecords field & goto :cleanup )
findstr /c:"\"delimiters\"" "%props_file%" >nul || ( echo [ ERROR ] Properties JSON missing delimiters field & goto :cleanup )
echo [ INFO ] Properties JSON structure OK

echo [ SUCCESS ] Test 1: Basic DAT loadfile-only — PASSED
set /a TESTS_PASSED+=1

REM ================================================================
REM Test 2: OPT loadfile-only (Opticon 7-column format)
REM ================================================================
echo [ INFO ] START: OPT loadfile-only
%BINARY% --loadfile-only --loadfile-format opt --count 50 --output-path "%TEST_OUTPUT_DIR%\opt_basic"
if errorlevel 1 (
    echo [ ERROR ] Test 2 failed: OPT loadfile-only execution
    goto :cleanup
)

set "opt_file="
for %%F in ("%TEST_OUTPUT_DIR%\opt_basic\*.opt") do set "opt_file=%%F"
if not defined opt_file (
    echo [ ERROR ] No .opt file found
    goto :cleanup
)

REM Verify line count (documents expand to page level, so should be at least 50)
powershell -Command "(Get-Content -Path '%opt_file%').Count" > "%temp%\line_count.txt"
set /p opt_line_count=<"%temp%\line_count.txt"
if !opt_line_count! lss 50 (
    echo [ ERROR ] OPT line count: expected at least 50, got !opt_line_count!
    goto :cleanup
)

REM Check first line prefix
powershell -Command "(Get-Content -Path '%opt_file%' -TotalCount 1)" > "%temp%\header.txt"
set /p first_line=<"%temp%\header.txt"
echo "!first_line!" | findstr "^IMG" >nul
if errorlevel 1 (
    echo [ ERROR ] OPT first line doesn't start with IMG prefix
    goto :cleanup
)

powershell -NoProfile -Command "$bad = Get-Content -Path '%opt_file%' | Where-Object { (($_.ToCharArray() | Where-Object { $_ -eq ',' }).Count) -ne 6 }; if ($bad) { exit 1 }"
if errorlevel 1 (
    echo [ ERROR ] OPT lines don't have 6 commas / 7 columns
    goto :cleanup
)

powershell -NoProfile -Command "$first = Get-Content -Path '%opt_file%' -TotalCount 1; if (($first -split ',')[3] -ne 'Y') { exit 1 }"
if errorlevel 1 (
    echo [ ERROR ] OPT first line missing Y for doc-break
    goto :cleanup
)

echo [ SUCCESS ] Test 2: OPT loadfile-only — PASSED
set /a TESTS_PASSED+=1

REM ================================================================
REM Test 3: Custom delimiters with strict prefix
REM ================================================================
echo [ INFO ] START: Custom delimiters
%BINARY% --loadfile-only --count 20 --output-path "%TEST_OUTPUT_DIR%\dat_custom_delim" --col-delim "char:|" --quote-delim "char:'" --eol LF
if errorlevel 1 (
    echo [ ERROR ] Test 3 failed: Custom delimiters execution
    goto :cleanup
)

set "dat_file="
for %%F in ("%TEST_OUTPUT_DIR%\dat_custom_delim\*.dat") do set "dat_file=%%F"
if not defined dat_file (
    echo [ ERROR ] No .dat file found
    goto :cleanup
)

findstr /L "|" "%dat_file%" >nul || ( echo [ ERROR ] Pipe delimiter not found & goto :cleanup )
echo [ INFO ] Pipe delimiter found OK

echo [ SUCCESS ] Test 3: Custom delimiters — PASSED
set /a TESTS_PASSED+=1

REM ================================================================
REM Test 4: Chaos mode generates anomalies
REM ================================================================
echo [ INFO ] START: Chaos mode
%BINARY% --loadfile-only --count 200 --output-path "%TEST_OUTPUT_DIR%\dat_chaos" --chaos-mode --chaos-amount "5%%" --seed 12345
if errorlevel 1 (
    echo [ ERROR ] Test 4 failed: Chaos mode execution
    goto :cleanup
)

set "props_file="
for %%F in ("%TEST_OUTPUT_DIR%\dat_chaos\*_properties.json") do set "props_file=%%F"
if not defined props_file (
    echo [ ERROR ] No _properties.json file found for chaos test
    goto :cleanup
)

findstr /c:"\"enabled\": true" "%props_file%" >nul || ( echo [ ERROR ] ChaosMode.Enabled not true & goto :cleanup )
findstr /c:"\"totalAnomalies\"" "%props_file%" >nul || ( echo [ ERROR ] ChaosMode.TotalAnomalies missing & goto :cleanup )
findstr /c:"\"injectedAnomalies\"" "%props_file%" >nul || ( echo [ ERROR ] Missing InjectedAnomalies array & goto :cleanup )
powershell -NoProfile -Command "$json = Get-Content -Raw -Path '%props_file%' | ConvertFrom-Json; if ($json.chaosMode.totalAnomalies -le 0 -or @($json.chaosMode.injectedAnomalies).Count -le 0) { exit 1 }"
if errorlevel 1 (
    echo [ ERROR ] Chaos Engine did not inject any anomalies
    goto :cleanup
)

echo [ SUCCESS ] Test 4: Chaos mode — PASSED
set /a TESTS_PASSED+=1

REM ================================================================
REM Test 5: Chaos with specific types filter
REM ================================================================
echo [ INFO ] START: Chaos with type filter
%BINARY% --loadfile-only --count 100 --output-path "%TEST_OUTPUT_DIR%\dat_chaos_typed" --chaos-mode --chaos-amount "10" --chaos-types "quotes,columns" --seed 12345
if errorlevel 1 (
    echo [ ERROR ] Test 5 failed: Chaos typed execution
    goto :cleanup
)

set "props_file="
for %%F in ("%TEST_OUTPUT_DIR%\dat_chaos_typed\*_properties.json") do set "props_file=%%F"

if not defined props_file ( echo [ ERROR ] Properties file not found & goto :cleanup )
if not exist "%props_file%" ( echo [ ERROR ] Properties file missing: %props_file% & goto :cleanup )

REM Parse JSON and assert anomalies were actually injected
powershell -NoProfile -Command ^
    "$j = Get-Content '%props_file%' -Raw | ConvertFrom-Json; ^
    if ($j.chaosMode.totalAnomalies -le 0) { Write-Error 'totalAnomalies is 0'; exit 1 }; ^
    if (-not $j.chaosMode.injectedAnomalies -or $j.chaosMode.injectedAnomalies.Count -eq 0) { Write-Error 'injectedAnomalies is empty'; exit 1 }; ^
    $types = $j.chaosMode.injectedAnomalies | ForEach-Object { $_.errorType } | Sort-Object -Unique; ^
    if (-not ($types -contains 'quotes' -or $types -contains 'columns')) { Write-Error ('Expected quotes or columns, got: ' + ($types -join ',')); exit 1 }; ^
    if ($types -contains 'encoding') { Write-Error 'Found forbidden type: encoding'; exit 1 }; ^
    if ($types -contains 'eol') { Write-Error 'Found forbidden type: eol'; exit 1 }"
if errorlevel 1 ( echo [ ERROR ] Chaos type filter assertion failed & goto :cleanup )
echo [ INFO ] Chaos type filtering OK

echo [ SUCCESS ] Test 5: Chaos type filter — PASSED
set /a TESTS_PASSED+=1

REM ================================================================
REM Test 6: Rejection tests (dependency validation)
REM ================================================================
echo [ INFO ] START: Rejection tests

%BINARY% --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\accept_col_delim" --col-delim "char:|" 2>nul
if errorlevel 1 ( echo [ ERROR ] Should have accepted --col-delim in standard mode & goto :cleanup )

%BINARY% --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\reject_2" --chaos-mode 2>nul
if not errorlevel 1 ( echo [ ERROR ] Should have rejected --chaos-mode without --loadfile-only & goto :cleanup )

%BINARY% --loadfile-only --count 10 --output-path "%TEST_OUTPUT_DIR%\reject_3" --chaos-amount "5%%" 2>nul
if not errorlevel 1 ( echo [ ERROR ] Should have rejected --chaos-amount without --chaos-mode & goto :cleanup )

%BINARY% --loadfile-only --count 10 --output-path "%TEST_OUTPUT_DIR%\reject_4" --target-zip-size 100MB 2>nul
if not errorlevel 1 ( echo [ ERROR ] Should have rejected --loadfile-only with --target-zip-size & goto :cleanup )

%BINARY% --loadfile-only --count 10 --output-path "%TEST_OUTPUT_DIR%\reject_5" --col-delim "20" 2>nul
if not errorlevel 1 ( echo [ ERROR ] Should have rejected --col-delim without prefix & goto :cleanup )

%BINARY% --loadfile-only --loadfile-format csv --count 10 --output-path "%TEST_OUTPUT_DIR%\reject_6" --chaos-mode 2>nul
if not errorlevel 1 ( echo [ ERROR ] Should have rejected --chaos-mode with --loadfile-format csv & goto :cleanup )

%BINARY% --loadfile-only --count 10 --output-path "%TEST_OUTPUT_DIR%\reject_7" --chaos-mode --chaos-amount "abc" 2>nul
if not errorlevel 1 ( echo [ ERROR ] Should have rejected invalid --chaos-amount abc & goto :cleanup )

echo [ SUCCESS ] Test 6: Dependency rejection — PASSED
set /a TESTS_PASSED+=1

REM ================================================================
REM Test 7: Deterministic output with --seed
REM ================================================================
echo [ INFO ] START: Deterministic output
%BINARY% --loadfile-only --count 20 --output-path "%TEST_OUTPUT_DIR%\seed_run1" --seed 999
%BINARY% --loadfile-only --count 20 --output-path "%TEST_OUTPUT_DIR%\seed_run2" --seed 999

set "dat1="
set "dat2="
for %%F in ("%TEST_OUTPUT_DIR%\seed_run1\*.dat") do set "dat1=%%F"
for %%F in ("%TEST_OUTPUT_DIR%\seed_run2\*.dat") do set "dat2=%%F"

fc /b "%dat1%" "%dat2%" >nul
if errorlevel 1 (
    echo [ ERROR ] Deterministic runs produced different output
    goto :cleanup
)

echo [ SUCCESS ] Test 7: Deterministic output — PASSED
set /a TESTS_PASSED+=1

REM ================================================================
REM Test 8: Active subprocess cancellation (DAT+OPT)
REM ================================================================
echo [ INFO ] START: Active subprocess cancellation (DAT+OPT)
set "CANCEL_OUT=%TEST_OUTPUT_DIR%\cancel_loadfile"
if not exist "%CANCEL_OUT%" mkdir "%CANCEL_OUT%"
echo sentinel data> "%CANCEL_OUT%\sentinel.txt"

set "CANCEL_WORKER_SCRIPT=%~dp0_generation-cancellation.ps1"
set "CANCEL_WORKER_PID_FILE=%TEST_OUTPUT_DIR%\cancel_lfo_worker.pid"
set "CANCEL_PID_FILE=%TEST_OUTPUT_DIR%\cancel_lfo.pid"
set "CANCEL_DONE_FILE=%TEST_OUTPUT_DIR%\cancel_lfo.done"
set "CANCEL_RESULT_FILE=%TEST_OUTPUT_DIR%\cancel_lfo.result"
set "CANCEL_ERROR_FILE=%TEST_OUTPUT_DIR%\cancel_lfo.err"
set "CANCEL_WORKDIR=%CD%"
set "CANCEL_STAGING_TIMEOUT_SECONDS=15"
set "CANCEL_POLL_INTERVAL_MS=20"
set "CANCEL_CLEANUP_TIMEOUT_SECONDS=10"
set "CANCEL_EVIDENCE_PATH=%CANCEL_OUT%"
set "CANCEL_EVIDENCE_FILTER=*_properties.json"

if exist "%BUILD_DIR%\Zipper.exe" (
    set "CANCEL_EXE=%BUILD_DIR%\Zipper.exe"
    set CANCEL_ARGS=--loadfile-only --load-file-formats dat,opt --count 20000 --output-path "%CANCEL_OUT%"
) else (
    set "CANCEL_EXE=dotnet"
    set CANCEL_ARGS=run --project %PROJECT% --no-build -c Release -- --loadfile-only --load-file-formats dat,opt --count 20000 --output-path "%CANCEL_OUT%"
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%CANCEL_WORKER_SCRIPT%"
if errorlevel 1 (
    echo [ ERROR ] Test 8: PowerShell cancellation worker failed
    if exist "%CANCEL_ERROR_FILE%" type "%CANCEL_ERROR_FILE%"
    goto :cleanup
)

if not exist "%CANCEL_RESULT_FILE%" (
    echo [ ERROR ] Test 8: Cancellation result file missing
    goto :cleanup
)

set "CANCEL_RES="
set /p CANCEL_RES=<"%CANCEL_RESULT_FILE%"
for /f "tokens=1,2 delims=," %%A in ("!CANCEL_RES!") do (
    set "CANCEL_SIGNAL_SENT=%%A"
    set "CANCEL_EXIT_CODE=%%B"
)

if "!CANCEL_SIGNAL_SENT!" NEQ "1" (
    echo [ ERROR ] Test 8: Signal was not sent before exit
    goto :cleanup
)

if "!CANCEL_EXIT_CODE!" NEQ "130" (
    echo [ ERROR ] Test 8: Expected exit code 130, got !CANCEL_EXIT_CODE!
    goto :cleanup
)

if exist "%CANCEL_OUT%\*.dat" (
    echo [ ERROR ] Test 8: Leftover .dat files found after cancellation
    goto :cleanup
)
if exist "%CANCEL_OUT%\*.opt" (
    echo [ ERROR ] Test 8: Leftover .opt files found after cancellation
    goto :cleanup
)
if exist "%CANCEL_OUT%\*.json" (
    echo [ ERROR ] Test 8: Leftover .json files found after cancellation
    goto :cleanup
)

if not exist "%CANCEL_OUT%\sentinel.txt" (
    echo [ ERROR ] Test 8: Sentinel file was deleted
    goto :cleanup
)

echo [ SUCCESS ] Test 8: Active subprocess cancellation — PASSED
set /a TESTS_PASSED+=1

REM --- Cleanup ---
:cleanup
echo [ INFO ] Cleaning up...
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"

if %TESTS_PASSED% equ %TESTS_TOTAL% (
    echo [ SUCCESS ] All loadfile-only E2E tests passed! (%TESTS_PASSED%/%TESTS_TOTAL%)
    exit /b 0
) else (
    echo [ ERROR ] Loadfile-only E2E tests failed: %TESTS_PASSED%/%TESTS_TOTAL% passed
    exit /b 1
)
