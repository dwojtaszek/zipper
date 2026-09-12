@echo off
REM Archive Test workflow E2E (tickets #844/#846): CLI interactions for
REM --archive-test-suite / --archive-test-cases on Windows — publication,
REM validation failures, exit codes, frozen smoke membership, replay
REM determinism, and independent verification.

call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

set "TEST_OUTPUT_DIR=.\results\archive-test-suites"
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%" >nul 2>&1

set PASSED=0
set FAILED=0
set SMOKE_CASES=5
set /a EXPECTED_FILES=%SMOKE_CASES%*2
set ALL_CASES=49

REM Stored/header-only byte goldens (#846): stored entries plus fixed timestamps
REM are byte-stable across runtimes, so the Fixture IDs are frozen and asserted on
REM every CI platform. Deflate has no cross-runtime promise; replay-checked only.
REM Golden mirrored in ArchiveTestSuiteReplayTests.cs, test-archive-test-suites.sh,
REM and docs/archive-test-suites.md.
set "FROZEN_VALID_STORED_ID=atc-662d70277000fd379d41c3d096e0ef7d3075b668f7a33860bdf52117ab2e04ca"
set "FROZEN_VALID_EMPTY_ID=atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e950"

REM Shared Python logic: prefer 'python'; fall back to 'py -3'.
set "PYCMD=python"
where python >nul 2>&1 || set "PYCMD=py -3"

echo [ INFO ] === Archive Test Suite E2E ===

REM 1. Pinned schema-validation prerequisite (ticket #846): install the frozen
REM    Ajv explicitly and validate the committed frozen vector before anything
REM    else — no network install per fixture, no skip-as-pass.
npm exec --yes -- ajv-cli@5.0.0 test -s tests\fixtures\archive-test-case.schema.json -d tests\fixtures\archive-tests\valid-empty.json --valid --spec=draft7 >nul 2>&1
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

:summary
echo [ INFO ] Passed: %PASSED%, Failed: %FAILED%
if %FAILED% GTR 0 (
    echo [ ERROR ] Archive Test E2E failed.
    exit /b 1
)
echo [ SUCCESS ] Archive Test E2E passed.
exit /b 0

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
