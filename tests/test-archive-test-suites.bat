@echo off
REM Archive Test workflow E2E (ticket #844): basic CLI interactions for
REM --archive-test-suite / --archive-test-cases on Windows.

call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

set "TEST_OUTPUT_DIR=.\results\archive-test-suites"
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%" >nul 2>&1

set PASSED=0
set FAILED=0
set SMOKE_CASES=3
set /a EXPECTED_FILES=%SMOKE_CASES%*2

echo [ INFO ] === Archive Test Suite E2E ===

REM 1. Smoke suite publication: flat pairs only.
set "OUT=%TEST_OUTPUT_DIR%\smoke"
%ZIPPER_CMD% --archive-test-suite smoke --seed 42 --output-path "%OUT%"
if errorlevel 1 (
    call :fail "smoke suite run exited non-zero"
    goto :summary
)
call :count_files "%OUT%" FILES
if !FILES! EQU %EXPECTED_FILES% (
    call :pass "smoke suite published 6 files (3 pairs)"
) else (
    call :fail "expected %EXPECTED_FILES% files, found !FILES!"
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

REM 2. Case selection: exactly the named Case Keys.
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

REM 3. Determinism: a reordered selection into a new directory repeats the Fixture IDs.
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

REM 4. Repeated run into the same directory fails and preserves the pairs.
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

REM 5. Closed flag set: a generation flag is rejected even at its default value.
call :assert_rejected "generation flag alongside --archive-test-suite" --archive-test-suite smoke --folders 1 --output-path "%TEST_OUTPUT_DIR%\x"

REM 6. --benchmark / --chaos-list mixing is rejected before their early exits.
call :assert_rejected "--archive-test-suite with --benchmark" --archive-test-suite smoke --benchmark
call :assert_rejected "--archive-test-suite with --chaos-list" --archive-test-suite smoke --chaos-list

REM 7. Validation failures.
call :assert_rejected "unknown suite" --archive-test-suite bogus --output-path "%TEST_OUTPUT_DIR%\x"
call :assert_rejected "unknown Case Key" --archive-test-suite smoke --archive-test-cases no-such-case --output-path "%TEST_OUTPUT_DIR%\x"
call :assert_rejected "missing --output-path" --archive-test-suite smoke

REM 8. Independent verifier: the published pairs pass cross-implementation
REM     verification (schema, identity, mutation audit, reader operations; #845).
REM     Prefer 'python'; fall back to the 'py -3' launcher when it is the only one.
set "PYCMD=python"
where python >nul 2>&1 || set "PYCMD=py -3"

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

REM 9. Tamper detection: one flipped Archive byte must fail verification. The
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

REM 10. Verifier self-test module (tamper reasons, sleeper deadline, prerequisites).
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
