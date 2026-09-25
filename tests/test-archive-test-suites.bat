@echo off
REM Archive Test workflow E2E (tickets #844/#846): CLI interactions for
REM --archive-test-suite / --archive-test-cases on Windows — publication,
REM validation failures, exit codes, frozen smoke membership, replay
REM determinism, and independent verification.

call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "TEST_OUTPUT_DIR=.\results\archive-test-suites"
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%" >nul 2>&1

set PASSED=0
set FAILED=0
set SMOKE_CASES=5
set /a EXPECTED_FILES=%SMOKE_CASES%*2
set ALL_CASES=106

REM Stored/header-only byte goldens (#846): stored entries plus fixed timestamps
REM are byte-stable across runtimes, so the Fixture IDs are frozen and asserted on
REM every CI platform. Deflate has no cross-runtime promise; replay-checked only.
REM Golden mirrored in ArchiveTestSuiteReplayTests.cs, test-archive-test-suites.sh,
REM and docs/archive-test-suites.md.
set "FROZEN_VALID_STORED_ID=atc-d4998f7846f15ca3c2d709a07579a0ba6723f5f7b524ca39953622407b90a24a"
set "FROZEN_VALID_EMPTY_ID=atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e950"

REM Shared Python logic: prefer 'python'; fall back to 'py -3'.
set "PYCMD=python"
where python >nul 2>&1 || set "PYCMD=py -3"

echo [ INFO ] === Archive Test Suite E2E ===

REM 1. Pinned Ajv restore + prerequisite (tickets #846/#932): restore the
REM    frozen Ajv CLI explicitly with `npm ci` when missing — the only network
REM    step — then validate the committed frozen vector. Verification itself
REM    runs the restored package via node directly and never downloads; a
REM    missing restore fails instead of skipping.
if exist "tests\archive-tests\node_modules\ajv-cli\dist\index.js" (
    call :pass "pinned Ajv CLI already restored"
) else (
    where npm >nul 2>&1
    if errorlevel 1 (
        call :fail "npm must be installed to restore the pinned Ajv CLI"
    ) else (
        call npm ci --ignore-scripts --prefix tests/archive-tests >nul 2>&1
        if errorlevel 1 (
            call :fail "npm ci must restore the pinned Ajv CLI"
        ) else (
            call :pass "pinned Ajv CLI restored via npm ci"
        )
    )
)
%PYCMD% -c "import importlib.util, sys; spec = importlib.util.spec_from_file_location('vf', 'tests/archive-tests/verify-fixtures.py'); vf = importlib.util.module_from_spec(spec); spec.loader.exec_module(vf); v = vf.AjvValidator(vf.DEFAULT_SCHEMA, vf.DEFAULT_AJV); err = v.check_prerequisite(); sys.exit(1 if err else 0 if v.validate('tests/fixtures/archive-tests/valid-empty.json') else 1)" >nul 2>&1
if errorlevel 1 (
    call :fail "pinned Ajv prerequisite must be installed and validate the frozen vector"
) else (
    call :pass "pinned Ajv prerequisite installed and validated the frozen vector"
)

REM 2. Smoke suite publication: the five frozen Case Keys as flat pairs.
set "OUT=%TEST_OUTPUT_DIR%\smoke"
%ZIPPER_CMD% --archive-test-suite smoke --seed 42 --output-path "%OUT%"
if errorlevel 1 (
    call :fail "smoke suite run exited non-zero"
    goto :summary
)
call :count_files "%OUT%" FILES
if !FILES! EQU %EXPECTED_FILES% (
    call :pass "smoke suite published !FILES! files (%SMOKE_CASES% pairs)"
) else (
    call :fail "expected %EXPECTED_FILES% files, found !FILES!"
)

set ZIPS=0
for %%Z in ("%OUT%\*.zip") do set /a ZIPS+=1
set JSONS=0
for %%J in ("%OUT%\*.json") do set /a JSONS+=1
REM Per-term checks: a canceling sum would pass a wrong zip/json mix (#846).
set SPLIT_OK=1
if !ZIPS! NEQ %SMOKE_CASES% set SPLIT_OK=0
if !JSONS! NEQ %SMOKE_CASES% set SPLIT_OK=0
if !SPLIT_OK! EQU 1 (
    call :pass "smoke suite published %SMOKE_CASES% .zip and %SMOKE_CASES% .json files"
) else (
    call :fail "smoke suite published !ZIPS! .zip and !JSONS! .json files (expected %SMOKE_CASES% each)"
)

REM No subfolders or outer Archive (flat layout).
set SUBDIRS=0
for /d %%D in ("%OUT%\*") do set /a SUBDIRS+=1
if !SUBDIRS! EQU 0 (
    call :pass "no subfolders or outer Archive"
) else (
    call :fail "found !SUBDIRS! subdirectories under the output"
)

REM JSON fixtureId/fileName agree with the pair basenames.
set NAMES_OK=1
for %%J in ("%OUT%\*.json") do (
    set "BASE=%%~nJ"
    findstr /C:"!BASE!.zip" "%%J" >nul 2>&1
    if errorlevel 1 set NAMES_OK=0
    if not exist "%OUT%\!BASE!.zip" set NAMES_OK=0
)
if !NAMES_OK! EQU 1 (
    call :pass "JSON fixtureId/fileName match basenames"
) else (
    call :fail "JSON fixtureId/fileName mismatch"
)

REM Frozen cross-platform Fixture IDs (stored/header-only byte goldens).
set STORED_OK=1
if not exist "%OUT%\%FROZEN_VALID_STORED_ID%.zip" set STORED_OK=0
if not exist "%OUT%\%FROZEN_VALID_STORED_ID%.json" set STORED_OK=0
if !STORED_OK! EQU 1 (
    call :pass "stored control matches the frozen cross-platform Fixture ID"
) else (
    call :fail "stored control must match the frozen cross-platform Fixture ID"
)
set EMPTY_OK=1
if not exist "%OUT%\%FROZEN_VALID_EMPTY_ID%.zip" set EMPTY_OK=0
if not exist "%OUT%\%FROZEN_VALID_EMPTY_ID%.json" set EMPTY_OK=0
if !EMPTY_OK! EQU 1 (
    call :pass "empty control matches the frozen Fixture ID"
) else (
    call :fail "empty control must match the frozen Fixture ID"
)

REM 3. Unopenable Archive with a still-readable Expectation File: the
REM    missing-eocd smoke member cannot be opened by a reference reader, yet
REM    its JSON parses standalone.
set "ME_JSON="
for /f "delims=" %%P in ('%PYCMD% -c "import glob,json,sys; print(next((p for p in glob.glob(sys.argv[1]+'/*.json') if json.load(open(p))['caseKey']=='missing-eocd'), ''))" "%OUT%" 2^>nul') do set "ME_JSON=%%P"
if not defined ME_JSON (
    call :fail "smoke suite must contain the missing-eocd Case Key"
) else (
    set "ME_ZIP=!ME_JSON:.json=.zip!"
    %PYCMD% -c "import sys,zipfile; zipfile.ZipFile(sys.argv[1])" "!ME_ZIP!" >nul 2>&1
    if errorlevel 1 (
        call :pass "missing-eocd Archive is unopenable by a reference reader"
    ) else (
        call :fail "missing-eocd Archive must be unopenable by a reference reader"
    )
    %PYCMD% -c "import json,sys; json.load(open(sys.argv[1]))" "!ME_JSON!" >nul 2>&1
    if errorlevel 1 (
        call :fail "the Expectation File of an unopenable Archive must be readable standalone"
    ) else (
        call :pass "the Expectation File of an unopenable Archive is still readable standalone"
    )
)

REM 4. Case selection: exactly the named Case Keys.
set "OUT=%TEST_OUTPUT_DIR%\two-cases"
%ZIPPER_CMD% --archive-test-suite malformed --archive-test-cases crc-both-mismatch,missing-eocd --seed 42 --output-path "%OUT%"
if errorlevel 1 (
    call :fail "case selection run exited non-zero"
    goto :summary
)
call :count_files "%OUT%" FILES
if !FILES! EQU 4 (
    call :pass "case selection published exactly 2 pairs"
) else (
    call :fail "expected 4 files, found !FILES!"
)

REM 5. Replay determinism: reordered selection repeats the Fixture IDs; a full
REM    smoke replay into a fresh directory is byte-identical.
set "OUT2=%TEST_OUTPUT_DIR%\two-cases-replay"
%ZIPPER_CMD% --archive-test-suite malformed --archive-test-cases missing-eocd,crc-both-mismatch --seed 42 --output-path "%OUT2%"
if errorlevel 1 (
    call :fail "reordered replay run exited non-zero"
    goto :summary
)
dir /b /o:n "%OUT%" > "%TEMP%\atc-a.txt" 2>nul
dir /b /o:n "%OUT2%" > "%TEMP%\atc-b.txt" 2>nul
fc /b "%TEMP%\atc-a.txt" "%TEMP%\atc-b.txt" >nul 2>&1
if errorlevel 1 (
    call :fail "reordered selection must repeat identical Fixture IDs"
) else (
    call :pass "reordered selection repeats identical Fixture IDs"
)

set "SMOKE_REPLAY=%TEST_OUTPUT_DIR%\smoke-replay"
%ZIPPER_CMD% --archive-test-suite smoke --seed 42 --output-path "%SMOKE_REPLAY%"
if errorlevel 1 (
    call :fail "smoke replay run exited non-zero"
    goto :summary
)
%PYCMD% -c "import os,filecmp,sys; a,b=sys.argv[1:3]; names=set(os.listdir(a)); sys.exit(0 if names==set(os.listdir(b)) and all(filecmp.cmp(os.path.join(a,n),os.path.join(b,n),shallow=False) for n in names) else 1)" "%TEST_OUTPUT_DIR%\smoke" "%SMOKE_REPLAY%" >nul 2>&1
if errorlevel 1 (
    call :fail "fresh-directory smoke replay must be byte-identical"
) else (
    call :pass "fresh-directory smoke replay is byte-identical"
)

REM 6. Repeated run into the same directory fails and preserves the pairs.
%ZIPPER_CMD% --archive-test-suite smoke --output-path "%TEST_OUTPUT_DIR%\smoke" >nul 2>&1
if errorlevel 1 (
    call :count_files "%TEST_OUTPUT_DIR%\smoke" FILES
    if !FILES! EQU %EXPECTED_FILES% (
        call :pass "failed repeated run preserved the first run"
    ) else (
        call :fail "repeated run left !FILES! files"
    )
) else (
    call :fail "repeated run into existing directory must fail"
)

REM 7. Closed flag set: a generation flag is rejected even at its default value.
call :assert_rejected "generation flag alongside --archive-test-suite" --archive-test-suite smoke --folders 1 --output-path "%TEST_OUTPUT_DIR%\x"

REM 8. --benchmark / --chaos-list mixing is rejected before their early exits.
call :assert_rejected "--archive-test-suite with --benchmark" --archive-test-suite smoke --benchmark
call :assert_rejected "--archive-test-suite with --chaos-list" --archive-test-suite smoke --chaos-list

REM 9. Validation failures.
call :assert_rejected "unknown suite" --archive-test-suite bogus --output-path "%TEST_OUTPUT_DIR%\x"
call :assert_rejected "unknown Case Key" --archive-test-suite smoke --archive-test-cases no-such-case --output-path "%TEST_OUTPUT_DIR%\x"
call :assert_rejected "missing --output-path" --archive-test-suite smoke

REM 9.1. Negative seed validation: reject before output generation with exact error.
set "NEGATIVE_SEED_OUT=%TEST_OUTPUT_DIR%\negative-seed"
set "NEGATIVE_SEED_ERR=%TEST_OUTPUT_DIR%\negative-seed.stderr"
%ZIPPER_CMD% --archive-test-suite smoke --seed -1 --output-path "%NEGATIVE_SEED_OUT%" >nul 2>"%NEGATIVE_SEED_ERR%"
if errorlevel 1 (
    if exist "%NEGATIVE_SEED_OUT%" (
        call :fail "negative --seed must not create output"
    ) else (
        call :pass "negative --seed created no output"
    )
    findstr /L /X /C:"Error: --seed must be a non-negative integer." "%NEGATIVE_SEED_ERR%" >nul 2>&1
    if errorlevel 1 (
        call :fail "negative --seed must report exact error"
    ) else (
        call :pass "negative --seed rejected with exact error"
    )
) else (
    call :fail "negative --seed must fail"
)

REM 10. Independent verifier: smoke, malformed selection, and the complete
REM     catalogue (exactly one pair per unique Case Key, every pair verified;
REM     unsafe policy fixtures are never extracted).
%PYCMD% tests\archive-tests\verify-fixtures.py "%TEST_OUTPUT_DIR%\smoke" --report "%TEST_OUTPUT_DIR%\smoke-verification.json" >nul 2>&1
if errorlevel 1 (
    call :fail "independent verifier must pass the smoke pairs"
) else (
    call :pass "independent verifier passed the smoke pairs"
)

%PYCMD% tests\archive-tests\verify-fixtures.py "%TEST_OUTPUT_DIR%\two-cases" --report "%TEST_OUTPUT_DIR%\two-cases-verification.json" >nul 2>&1
if errorlevel 1 (
    call :fail "independent verifier must pass the malformed selection"
) else (
    call :pass "independent verifier passed the malformed selection"
)

REM Encoding suite (ticket #887): the three legacy-codec 0x5C trail-byte
REM controls, verified by the named CP932/Big5/GBK consumer oracle.
set "ENCODING_OUT=%TEST_OUTPUT_DIR%\encoding"
%ZIPPER_CMD% --archive-test-suite encoding --seed 42 --output-path "%ENCODING_OUT%"
if errorlevel 1 (
    call :fail "encoding suite run exited non-zero"
    goto :summary
)
set ENCODING_ZIPS=0
for %%Z in ("%ENCODING_OUT%\*.zip") do set /a ENCODING_ZIPS+=1
set ENCODING_JSONS=0
for %%J in ("%ENCODING_OUT%\*.json") do set /a ENCODING_JSONS+=1
if !ENCODING_ZIPS! EQU 3 (
    call :pass "encoding suite published 3 archives"
) else (
    call :fail "encoding suite published !ENCODING_ZIPS! archives (expected 3)"
)
if !ENCODING_JSONS! EQU 3 (
    call :pass "encoding suite published 3 sidecars"
) else (
    call :fail "encoding suite published !ENCODING_JSONS! sidecars (expected 3)"
)
%PYCMD% tests\archive-tests\verify-fixtures.py "%ENCODING_OUT%" --report "%TEST_OUTPUT_DIR%\encoding-verification.json" >nul 2>&1
if errorlevel 1 (
    call :fail "independent verifier must pass the encoding pairs (named CP932/Big5/GBK consumer)"
) else (
    call :pass "independent verifier passed the encoding pairs"
)

set "ALL_OUT=%TEST_OUTPUT_DIR%\all"
%ZIPPER_CMD% --archive-test-suite all --seed 42 --output-path "%ALL_OUT%"
if errorlevel 1 (
    call :fail "all suite run exited non-zero"
    goto :summary
)
call :count_files "%ALL_OUT%" ALL_FILES
set ALL_ZIPS=0
for %%Z in ("%ALL_OUT%\*.zip") do set /a ALL_ZIPS+=1
set ALL_JSONS=0
for %%J in ("%ALL_OUT%\*.json") do set /a ALL_JSONS+=1
set "DISTINCT="
for /f "delims=" %%C in ('%PYCMD% -c "import glob,json,sys; print(len({json.load(open(p))['caseKey'] for p in glob.glob(sys.argv[1]+'/*.json')}))" "%ALL_OUT%" 2^>nul') do set "DISTINCT=%%C"
if not defined DISTINCT set "DISTINCT=0"
set /a TWICE_DISTINCT=DISTINCT*2
if !DISTINCT! NEQ %ALL_CASES% (
    call :fail "all suite published the frozen %ALL_CASES% unique Case Keys (found !DISTINCT!)"
) else (
    call :pass "all suite published the frozen %ALL_CASES% unique Case Keys"
)
REM Per-term checks: a canceling sum would pass a wrong pair mix (#846).
set ALL_OK=1
if !ALL_FILES! NEQ !TWICE_DISTINCT! set ALL_OK=0
if !ALL_ZIPS! NEQ !DISTINCT! set ALL_OK=0
if !ALL_JSONS! NEQ !DISTINCT! set ALL_OK=0
if !ALL_OK! EQU 1 (
    call :pass "all suite published exactly one pair per unique Case Key"
) else (
    call :fail "all suite pair counts mismatch: !ALL_FILES! files, !ALL_ZIPS! zips, !ALL_JSONS! jsons, !DISTINCT! Case Keys"
)

%PYCMD% tests\archive-tests\verify-fixtures.py "%ALL_OUT%" --report "%TEST_OUTPUT_DIR%\all-verification.json" >nul 2>&1
if errorlevel 1 (
    call :fail "independent verifier must pass the complete catalogue"
) else (
    call :pass "independent verifier passed the complete catalogue"
)

REM 10.1. Cancellation (REQ-215/REQ-219): start the longest suite in a hidden
REM     console and signal only after its first owned atc-*.zip appears in an
REM     atc-staging-<GUID> sibling. Direct termination cannot provide graceful
REM     cancellation because it calls TerminateProcess, so console Ctrl+C is sent.
REM     Windows delegates Ctrl+C through the PowerShell worker; Bash signals its
REM     job-control process group directly.
REM     The requested destination is published by one final rename, so either
REM     race outcome must leave no owned staging directory behind.
set "CANCEL_TEMP_ID=%RANDOM%%RANDOM%"
set "CANCEL_PARENT=%TEST_OUTPUT_DIR%"
set "CANCEL_OUT=%CANCEL_PARENT%\cancellation"
set "CANCEL_WORKER_SCRIPT=%SCRIPT_DIR%_archive-test-cancellation.ps1"
set "CANCEL_WORKER_PID_FILE=%TEMP%\atc-cancel-worker-%CANCEL_TEMP_ID%.pid"
set "CANCEL_PID_FILE=%TEMP%\atc-cancel-worker-%CANCEL_TEMP_ID%.target-pid"
set "CANCEL_RESULT_FILE=%TEMP%\atc-cancel-worker-%CANCEL_TEMP_ID%.result"
set "CANCEL_ERROR_FILE=%TEMP%\atc-cancel-worker-%CANCEL_TEMP_ID%.error"
set "CANCEL_DONE_FILE=%TEMP%\atc-cancel-worker-%CANCEL_TEMP_ID%.done"
set "CANCEL_WORKER_PID="
set "CANCEL_PID="
set "CANCEL_STAGING_CHECKED_COUNT="
set "CANCEL_WORKDIR=%CD%"
set "CANCEL_POLL_INTERVAL_MS=10"
set "CANCEL_STAGING_TIMEOUT_SECONDS=30"
set "CANCEL_WORKER_PID_TIMEOUT_MS=30000"
set "CANCEL_TARGET_PID_TIMEOUT_MS=30000"
set "CANCEL_WORKER_DONE_TIMEOUT_MS=30000"
set "CANCEL_RESULT_TIMEOUT_MS=10000"
set "CANCEL_CLEANUP_TIMEOUT_SECONDS=10"
where powershell.exe >nul 2>&1
if errorlevel 1 (
    call :fail "PowerShell must be available for Windows cancellation"
    goto :summary
)
if exist "%ZIPPER_CMD%" (
    set "CANCEL_EXE=%ZIPPER_CMD%"
    set CANCEL_ARGS=--archive-test-suite all --seed 42 --output-path "%CANCEL_OUT%"
) else (
    for /f "tokens=1,* delims= " %%E in ("%ZIPPER_CMD%") do (
        set "CANCEL_EXE=%%E"
        set CANCEL_ARGS=%%F
    )
    set CANCEL_ARGS=!CANCEL_ARGS! --archive-test-suite all --seed 42 --output-path "%CANCEL_OUT%"
)
if not exist "%CANCEL_WORKER_SCRIPT%" (
    call :fail "Windows cancellation worker script is missing"
    goto :summary
)

start "" /b powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%CANCEL_WORKER_SCRIPT%"
if errorlevel 1 (
    call :fail "Windows cancellation worker failed to start"
    goto :summary
)
call :await_file "%CANCEL_WORKER_PID_FILE%" CANCEL_WORKER_PID "%CANCEL_WORKER_PID_TIMEOUT_MS%" "%CANCEL_POLL_INTERVAL_MS%"
if not defined CANCEL_WORKER_PID (
    if exist "%CANCEL_ERROR_FILE%" type "%CANCEL_ERROR_FILE%"
    call :fail "Windows cancellation worker did not hand off its PID"
    goto :summary
)
call :await_file "%CANCEL_PID_FILE%" CANCEL_PID "%CANCEL_TARGET_PID_TIMEOUT_MS%" "%CANCEL_POLL_INTERVAL_MS%"
if not defined CANCEL_PID (
    if exist "%CANCEL_ERROR_FILE%" type "%CANCEL_ERROR_FILE%"
    call :fail "Windows cancellation run did not hand off its PID"
    goto :summary
)
call :await_file "%CANCEL_DONE_FILE%" CANCEL_DONE "%CANCEL_WORKER_DONE_TIMEOUT_MS%" "%CANCEL_POLL_INTERVAL_MS%"
if not defined CANCEL_DONE (
    if exist "%CANCEL_ERROR_FILE%" type "%CANCEL_ERROR_FILE%"
    call :fail "Windows cancellation worker did not finish"
    goto :summary
)
call :await_process_exit "%CANCEL_WORKER_PID%" "%CANCEL_RESULT_TIMEOUT_MS%" "%CANCEL_POLL_INTERVAL_MS%"
if errorlevel 1 (
    call :fail "Windows cancellation worker did not exit within %CANCEL_RESULT_TIMEOUT_MS%ms after its DONE marker"
    goto :summary
)
REM Both processes are gone on this path, so drop the PIDs: a later failure
REM reaching :summary must not taskkill an id the OS may have reused. Failure
REM paths keep them so :cleanup_cancel_process can still force cleanup.
set "CANCEL_WORKER_PID="
set "CANCEL_PID="
call :await_file "%CANCEL_RESULT_FILE%" CANCEL_RESULT "%CANCEL_RESULT_TIMEOUT_MS%" "%CANCEL_POLL_INTERVAL_MS%"
if not defined CANCEL_RESULT (
    if exist "%CANCEL_ERROR_FILE%" type "%CANCEL_ERROR_FILE%"
    call :fail "Windows cancellation run did not report an exit code"
    goto :summary
)
set "CANCEL_SIGNAL_SENT="
set "CANCEL_EXIT_CODE="
set "CANCEL_RESULT_EXTRA="
for /f "tokens=1,2,* delims=," %%A in ("!CANCEL_RESULT!") do (
    set "CANCEL_SIGNAL_SENT=%%A"
    set "CANCEL_EXIT_CODE=%%B"
    set "CANCEL_RESULT_EXTRA=%%C"
)
if not defined CANCEL_SIGNAL_SENT (
    call :fail "Windows cancellation result omitted race outcome"
    goto :summary
)
if not defined CANCEL_EXIT_CODE (
    call :fail "Windows cancellation result omitted exit code"
    goto :summary
)
if defined CANCEL_RESULT_EXTRA (
    call :fail "Windows cancellation result contained unexpected fields"
    goto :summary
)
if "!CANCEL_SIGNAL_SENT!" NEQ "0" if "!CANCEL_SIGNAL_SENT!" NEQ "1" (
    call :fail "Windows cancellation result has invalid race outcome (!CANCEL_SIGNAL_SENT!)"
    goto :summary
)
set "CANCEL_EXIT_INVALID="
for /f "eol=| delims=0123456789" %%C in ("!CANCEL_EXIT_CODE!") do set "CANCEL_EXIT_INVALID=1"
if defined CANCEL_EXIT_INVALID (
    call :fail "Windows cancellation result has invalid exit code (!CANCEL_EXIT_CODE!)"
    goto :summary
)
if "!CANCEL_EXIT_CODE!" EQU "0" set "CANCEL_SIGNAL_SENT=0"

set CANCEL_ZIPS=0
if exist "%CANCEL_OUT%\atc-*.zip" for %%Z in ("%CANCEL_OUT%\atc-*.zip") do set /a CANCEL_ZIPS+=1
set CANCEL_JSONS=0
if exist "%CANCEL_OUT%\atc-*.json" for %%J in ("%CANCEL_OUT%\atc-*.json") do set /a CANCEL_JSONS+=1
set CANCEL_STAGING=0
if exist "%CANCEL_PARENT%\atc-staging-*" for /d %%D in ("%CANCEL_PARENT%\atc-staging-*") do set /a CANCEL_STAGING+=1

if !CANCEL_SIGNAL_SENT! EQU 1 (
    if !CANCEL_EXIT_CODE! EQU 130 (
        call :pass "cancellation race signal-won run exited 130"
    ) else (
        call :fail "cancellation race signal-won run exited !CANCEL_EXIT_CODE! instead of 130"
    )
    if !CANCEL_ZIPS! EQU 0 if !CANCEL_JSONS! EQU 0 (
        call :pass "cancellation race signal-won run published no Archive or Expectation File"
    ) else (
        call :fail "cancellation race signal-won run published !CANCEL_ZIPS! Archives and !CANCEL_JSONS! Expectation Files (expected zero each)"
    )
) else (
    if !CANCEL_SIGNAL_SENT! EQU 0 (
        if !CANCEL_EXIT_CODE! EQU 0 (
            call :pass "cancellation race finished-first run exited 0"
        ) else (
            call :fail "cancellation race finished-first run exited !CANCEL_EXIT_CODE! instead of 0"
        )
        if !CANCEL_ZIPS! EQU %ALL_CASES% if !CANCEL_JSONS! EQU %ALL_CASES% (
            call :pass "cancellation race finished-first run published all %ALL_CASES% pairs"
        ) else (
            call :fail "cancellation race finished-first run published !CANCEL_ZIPS! Archives and !CANCEL_JSONS! Expectation Files (expected %ALL_CASES% each)"
        )
    ) else (
        call :fail "Windows cancellation signal outcome was invalid (!CANCEL_SIGNAL_SENT!)"
    )
)
if !CANCEL_STAGING! EQU 0 (
    call :pass "cancellation left no owned staging directory"
) else (
    call :fail "cancellation left !CANCEL_STAGING! owned staging directories"
)
set "CANCEL_STAGING_CHECKED_COUNT=!CANCEL_STAGING!"

REM 11. Tamper detection: one flipped Archive byte must fail verification. The
REM     copy and the flip are guarded so the check cannot pass vacuously.
set "TAMPER=%TEST_OUTPUT_DIR%\tamper"
mkdir "%TAMPER%" >nul 2>&1
copy /y "%TEST_OUTPUT_DIR%\smoke\*.*" "%TAMPER%\" >nul
if errorlevel 1 (
    call :fail "tamper preparation copy failed"
    goto :summary
)
set "FIRST_TAMPER_ZIP="
for /f "delims=" %%Z in ('dir /b /o:n "%TAMPER%\*.zip"') do (
    if not defined FIRST_TAMPER_ZIP (
        set "FIRST_TAMPER_ZIP=%TAMPER%\%%Z"
    )
)
if not defined FIRST_TAMPER_ZIP (
    call :fail "tamper preparation found no Archive to flip"
    goto :summary
)
%PYCMD% tests\archive-tests\flip-last-byte.py "%FIRST_TAMPER_ZIP%" >nul 2>&1
if errorlevel 1 (
    call :fail "tamper preparation flip failed"
    goto :summary
)
%PYCMD% tests\archive-tests\verify-fixtures.py "%TAMPER%" --report "%TEST_OUTPUT_DIR%\tamper-verification.json" >nul 2>&1
if errorlevel 1 (
    call :pass "tampered Archive byte rejected by the verifier"
) else (
    call :fail "tampered Archive byte must fail verification"
)

REM 12. Verifier self-test module (tamper reasons, sleeper deadline, prerequisites).
%PYCMD% tests\archive-tests\test_verify_fixtures.py >nul 2>&1
if errorlevel 1 (
    call :fail "verifier self-test module must pass"
) else (
    call :pass "verifier self-test module passed"
)

REM 13. Full-codec verification profile via 7-Zip (ticket #930): valid coded
REM     controls must extract with matching content hashes; coded-corruption
REM     fixtures must be rejected via `t` (no extraction). Missing 7-Zip fails.
set "SEVEN_ZIP="
where 7zz >nul 2>&1
if not errorlevel 1 set "SEVEN_ZIP=7zz"
if not defined SEVEN_ZIP (
    where 7z >nul 2>&1
    if not errorlevel 1 set "SEVEN_ZIP=7z"
)
if not defined SEVEN_ZIP (
    call :fail "7-Zip (7zz/7z) must be installed for full-codec verification"
) else (
    call :pass "7-Zip full-codec adapter present"
    set "FULLCODEC_PY=%TEMP%\atc-fullcodec-%RANDOM%.py"
    > "!FULLCODEC_PY!" echo import glob, hashlib, json, os, subprocess, sys, tempfile
    >> "!FULLCODEC_PY!" echo out, seven = sys.argv[1:3]
    >> "!FULLCODEC_PY!" echo for key in ("valid-bzip2", "valid-deflate64", "valid-deflate64-long-match", "valid-mixed-methods", "bzip2-high-ratio-bounded", "valid-zip64-descriptor-signature", "valid-zip64-descriptor-no-signature"):
    >> "!FULLCODEC_PY!" echo     jp = next(p for p in glob.glob(os.path.join(out, "*.json")) if json.load(open(p))["caseKey"] == key)
    >> "!FULLCODEC_PY!" echo     d = json.load(open(jp))
    >> "!FULLCODEC_PY!" echo     zp = jp[:-5] + ".zip"
    >> "!FULLCODEC_PY!" echo     files = [e for e in d["entries"] if e["kind"] == "file"]
    >> "!FULLCODEC_PY!" echo     if not files: sys.exit(f"no file entries for {key}")
    >> "!FULLCODEC_PY!" echo     with tempfile.TemporaryDirectory() as td:
    >> "!FULLCODEC_PY!" echo         r = subprocess.run([seven, "x", zp, f"-o{td}", "-y"], capture_output=True)
    >> "!FULLCODEC_PY!" echo         if r.returncode != 0: sys.exit(f"extract failed for {key}")
    >> "!FULLCODEC_PY!" echo         for e in files:
    >> "!FULLCODEC_PY!" echo             fp = os.path.join(td, e["readableName"])
    >> "!FULLCODEC_PY!" echo             if not os.path.isfile(fp): sys.exit(f"missing extracted entry for {key}")
    >> "!FULLCODEC_PY!" echo             h = hashlib.sha256(open(fp, "rb").read()).hexdigest()
    >> "!FULLCODEC_PY!" echo             if h != e["contentSha256"]: sys.exit(f"hash mismatch for {key}")
    %PYCMD% "!FULLCODEC_PY!" "%ALL_OUT%" "!SEVEN_ZIP!" >nul 2>&1
    if errorlevel 1 (
        call :fail "7-Zip must extract valid coded controls with matching hashes"
    ) else (
        call :pass "7-Zip extracted valid coded controls with matching content hashes"
    )
    del "!FULLCODEC_PY!" >nul 2>&1
    set "CORRUPT_OK=1"
    for %%K in (bzip2-corrupt-block-magic bzip2-truncated-stream bzip2-wrong-crc deflate64-corrupt-stream deflate64-truncated-stream) do (
        set "CK_ZIP="
        for /f "delims=" %%P in ('%PYCMD% -c "import glob,json,sys; print(next((p[:-5]+'.zip' for p in glob.glob(sys.argv[1]+'/*.json') if json.load(open(p))['caseKey']==sys.argv[2]), ''))" "%ALL_OUT%" "%%K" 2^>nul') do set "CK_ZIP=%%P"
        if not defined CK_ZIP (
            set "CORRUPT_OK=0"
        ) else (
            if not exist "!CK_ZIP!" (
                set "CORRUPT_OK=0"
            ) else (
                "!SEVEN_ZIP!" t "!CK_ZIP!" >nul 2>&1
                if not errorlevel 1 set "CORRUPT_OK=0"
            )
        )
    )
    if !CORRUPT_OK! EQU 1 (
        call :pass "7-Zip rejected all #900 coded-corruption fixtures"
    ) else (
        call :fail "7-Zip must reject all #900 coded-corruption fixtures"
    )
    REM Ticket #933: spanning declarations and count disagreements are tested
    REM with `t` (never extracted). Four are rejected by 7-Zip; the locator
    REM total-disks declaration is ignored by 7-Zip, so only its presence is
    REM asserted, not a rejection.
    set "DISPUTE_OK=1"
    for %%K in (multidisk-eocd-declared multidisk-central-entry-declared eocd-entry-count-mismatch zip64-eocd-entry-count-mismatch) do (
        set "D_ZIP="
        for /f "delims=" %%P in ('%PYCMD% -c "import glob,json,sys; print(next((p[:-5]+'.zip' for p in glob.glob(sys.argv[1]+'/*.json') if json.load(open(p))['caseKey']==sys.argv[2]), ''))" "%ALL_OUT%" "%%K" 2^>nul') do set "D_ZIP=%%P"
        if not defined D_ZIP (
            set "DISPUTE_OK=0"
        ) else (
            if not exist "!D_ZIP!" (
                set "DISPUTE_OK=0"
            ) else (
                "!SEVEN_ZIP!" t "!D_ZIP!" >nul 2>&1
                if not errorlevel 1 set "DISPUTE_OK=0"
            )
        )
    )
    set "LOC_ZIP="
    for /f "delims=" %%P in ('%PYCMD% -c "import glob,json,sys; print(next((p[:-5]+'.zip' for p in glob.glob(sys.argv[1]+'/*.json') if json.load(open(p))['caseKey']=='zip64-locator-disk-mismatch'), ''))" "%ALL_OUT%" 2^>nul') do set "LOC_ZIP=%%P"
    if not defined LOC_ZIP set "DISPUTE_OK=0"
    if defined LOC_ZIP if not exist "!LOC_ZIP!" set "DISPUTE_OK=0"
    if !DISPUTE_OK! EQU 1 (
        call :pass "7-Zip rejected the #933 spanning and count-disagreement fixtures"
    ) else (
        call :fail "7-Zip must reject the #933 spanning and count-disagreement fixtures"
    )
    REM Ticket #935: mixed one-bad-member archives must fail as a whole (`t`
    REM rejects) while the healthy Store and Deflate siblings extract by name
    REM with matching hashes — the bad member's bytes are never decoded.
    set "MIXED_PY=%TEMP%\atc-mixed-%RANDOM%.py"
    > "!MIXED_PY!" echo import glob, hashlib, json, os, subprocess, sys, tempfile
    >> "!MIXED_PY!" echo out, seven = sys.argv[1:3]
    >> "!MIXED_PY!" echo for key in ("mixed-methods-one-corrupt-member", "mixed-methods-one-unsupported-member"):
    >> "!MIXED_PY!" echo     jp = next(p for p in glob.glob(os.path.join(out, "*.json")) if json.load(open(p))["caseKey"] == key)
    >> "!MIXED_PY!" echo     d = json.load(open(jp))
    >> "!MIXED_PY!" echo     zp = jp[:-5] + ".zip"
    >> "!MIXED_PY!" echo     healthy = [e for e in d["entries"] if e["kind"] == "file" and e["readableName"] in ("a.txt", "b.bin")]
    >> "!MIXED_PY!" echo     if len(healthy) != 2: sys.exit(f"expected healthy siblings for {key}")
    >> "!MIXED_PY!" echo     if subprocess.run([seven, "t", zp], capture_output=True).returncode == 0: sys.exit(f"one-bad-member Archive must not verify as success: {key}")
    >> "!MIXED_PY!" echo     with tempfile.TemporaryDirectory() as td:
    >> "!MIXED_PY!" echo         names = [e["readableName"] for e in healthy]
    >> "!MIXED_PY!" echo         r = subprocess.run([seven, "x", zp, f"-o{td}", "-y", *names], capture_output=True)
    >> "!MIXED_PY!" echo         if r.returncode != 0: sys.exit(f"healthy sibling extraction failed for {key}")
    >> "!MIXED_PY!" echo         for e in healthy:
    >> "!MIXED_PY!" echo             fp = os.path.join(td, e["readableName"])
    >> "!MIXED_PY!" echo             if not os.path.isfile(fp): sys.exit(f"healthy sibling missing for {key}")
    >> "!MIXED_PY!" echo             h = hashlib.sha256(open(fp, "rb").read()).hexdigest()
    >> "!MIXED_PY!" echo             if h != e["contentSha256"]: sys.exit(f"healthy sibling hash mismatch for {key}")
    %PYCMD% "!MIXED_PY!" "%ALL_OUT%" "!SEVEN_ZIP!" >nul 2>&1
    if errorlevel 1 (
        call :fail "7-Zip must isolate the bad member while healthy siblings match"
    ) else (
        call :pass "7-Zip isolates the bad member while healthy siblings match"
    )
    del "!MIXED_PY!" >nul 2>&1
)

REM 14. Offline verification (ticket #932): with an empty npm cache and an
REM     unreachable registry, the full catalogue still verifies — proving
REM     verification downloads nothing. The pinned CLI runs via node directly,
REM     so these settings cannot affect it; they only turn any attempted
REM     download into a loud failure.
set "OFFLINE_CACHE=%TEST_OUTPUT_DIR%\npm-cache-empty"
if exist "%OFFLINE_CACHE%" rmdir /s /q "%OFFLINE_CACHE%"
mkdir "%OFFLINE_CACHE%" >nul 2>&1
set "npm_config_cache=%OFFLINE_CACHE%"
set "npm_config_registry=http://127.0.0.1:9/"
%PYCMD% tests\archive-tests\verify-fixtures.py "%ALL_OUT%" --report "%TEST_OUTPUT_DIR%\all-offline-verification.json" >nul 2>&1
if errorlevel 1 (
    call :fail "offline verification must pass the complete catalogue without downloads"
) else (
    call :pass "offline verification passed the complete catalogue without downloads"
)
set "npm_config_cache="
set "npm_config_registry="

:summary
call :cleanup_cancel_process
if defined CANCEL_PARENT (
    set "CANCEL_STAGING=0"
    if exist "%CANCEL_PARENT%\atc-staging-*" for /d %%D in ("%CANCEL_PARENT%\atc-staging-*") do set /a CANCEL_STAGING+=1
    if !CANCEL_STAGING! GTR 0 (
        if not defined CANCEL_STAGING_CHECKED_COUNT (
            call :fail "cancellation left !CANCEL_STAGING! owned staging directories"
        )
        if defined CANCEL_STAGING_CHECKED_COUNT if !CANCEL_STAGING! NEQ !CANCEL_STAGING_CHECKED_COUNT! (
            call :fail "cancellation left !CANCEL_STAGING! owned staging directories"
        )
        if exist "%CANCEL_PARENT%\atc-staging-*" for /d %%D in ("%CANCEL_PARENT%\atc-staging-*") do rmdir /s /q "%%~fD" >nul 2>&1
    )
)
set "CANCEL_WORKER_PID="
set "CANCEL_PID="
echo [ INFO ] Passed: %PASSED%, Failed: %FAILED%
if %FAILED% GTR 0 (
    echo [ ERROR ] Archive Test E2E failed.
    exit /b 1
)
echo [ SUCCESS ] Archive Test E2E passed.
exit /b 0

:await_file
set "WAIT_FILE_VALUE="
set "WAIT_FILE_PATH=%~1"
set "WAIT_FILE_TARGET=%~2"
set "WAIT_FILE_TIMEOUT_MS=%~3"
set "WAIT_FILE_POLL_INTERVAL_MS=%~4"
for /f "usebackq delims=" %%V in (`powershell.exe -NoProfile -Command "$deadline=[DateTime]::UtcNow.AddMilliseconds([int]$env:WAIT_FILE_TIMEOUT_MS); while([DateTime]::UtcNow -lt $deadline){ if(Test-Path -LiteralPath $env:WAIT_FILE_PATH){ $value=Get-Content -LiteralPath $env:WAIT_FILE_PATH -Raw -ErrorAction SilentlyContinue; if($null -ne $value){ $value=$value.Trim(); if($value.Length -gt 0){ Write-Output $value; exit 0 } } }; Start-Sleep -Milliseconds ([int]$env:WAIT_FILE_POLL_INTERVAL_MS) }; exit 1" 2^>nul`) do set "WAIT_FILE_VALUE=%%V"
set "%WAIT_FILE_TARGET%=%WAIT_FILE_VALUE%"
set "WAIT_FILE_PATH="
set "WAIT_FILE_TARGET="
set "WAIT_FILE_TIMEOUT_MS="
set "WAIT_FILE_POLL_INTERVAL_MS="
goto :eof

:await_process_exit
set "WAIT_PROCESS_PID=%~1"
set "WAIT_PROCESS_TIMEOUT_MS=%~2"
set "WAIT_PROCESS_POLL_INTERVAL_MS=%~3"
powershell.exe -NoProfile -Command "$deadline=[DateTime]::UtcNow.AddMilliseconds([int]$env:WAIT_PROCESS_TIMEOUT_MS); while([DateTime]::UtcNow -lt $deadline){ if($null -eq (Get-Process -Id ([int]$env:WAIT_PROCESS_PID) -ErrorAction SilentlyContinue)){ exit 0 }; Start-Sleep -Milliseconds ([int]$env:WAIT_PROCESS_POLL_INTERVAL_MS) }; exit 1" >nul 2>&1
if errorlevel 1 (
    set "WAIT_PROCESS_EXITED="
) else (
    set "WAIT_PROCESS_EXITED=1"
)
set "WAIT_PROCESS_PID="
set "WAIT_PROCESS_TIMEOUT_MS="
set "WAIT_PROCESS_POLL_INTERVAL_MS="
if defined WAIT_PROCESS_EXITED exit /b 0
exit /b 1

:cleanup_cancel_process
if defined CANCEL_WORKER_PID (
    taskkill /PID %CANCEL_WORKER_PID% /T /F >nul 2>&1
)
if defined CANCEL_PID (
    taskkill /PID %CANCEL_PID% /T /F >nul 2>&1
)
if defined CANCEL_WORKER_PID_FILE if exist "%CANCEL_WORKER_PID_FILE%" del /q "%CANCEL_WORKER_PID_FILE%" >nul 2>&1
if defined CANCEL_PID_FILE if exist "%CANCEL_PID_FILE%" del /q "%CANCEL_PID_FILE%" >nul 2>&1
if defined CANCEL_RESULT_FILE if exist "%CANCEL_RESULT_FILE%" del /q "%CANCEL_RESULT_FILE%" >nul 2>&1
if defined CANCEL_ERROR_FILE if exist "%CANCEL_ERROR_FILE%" del /q "%CANCEL_ERROR_FILE%" >nul 2>&1
if defined CANCEL_DONE_FILE if exist "%CANCEL_DONE_FILE%" del /q "%CANCEL_DONE_FILE%" >nul 2>&1
goto :eof

:count_files
set COUNT_TMP=0
for %%F in ("%~1\*.*") do set /a COUNT_TMP+=1
set "%2=!COUNT_TMP!"
goto :eof

:assert_rejected
%ZIPPER_CMD% %2 %3 %4 %5 %6 %7 %8 %9 >nul 2>&1
if errorlevel 1 (
    call :pass "%~1 rejected"
) else (
    call :fail "%~1 must be rejected"
)
goto :eof

:pass
set /a PASSED+=1
echo [ SUCCESS ] %~1
goto :eof

:fail
set /a FAILED+=1
echo [ ERROR ] %~1
goto :eof
