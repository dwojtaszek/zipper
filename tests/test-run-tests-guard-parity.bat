@echo off
REM E2E test: tests/validate-e2e-parity.sh must catch an unguarded .bat child call.
REM
REM Guards #1040. run-tests.bat used to call child scripts without checking errorlevel, so a
REM child that returned 1 was swallowed and the wrapper still reported success. On this leg the
REM real cmd.exe is available, so the runtime proof runs here in addition to the validator
REM checks that test-run-tests-guard-parity.sh performs.

setlocal enabledelayedexpansion

set "REPO_ROOT=%~dp0.."
set "VALIDATOR=%REPO_ROOT%\tests\validate-e2e-parity.sh"
set "RUNNER_BAT=%REPO_ROOT%\tests\run-tests.bat"
set "SANDBOX=%TEMP%\zipper-guard-parity-%RANDOM%%RANDOM%"

set PASSED=0
set FAILED=0

call :print_info "=== E2E Validator Guard-Parity Tests (#1040) ==="

if not exist "%VALIDATOR%" (
    call :fail "tests\validate-e2e-parity.sh not found"
    goto :summary
)
if not exist "%RUNNER_BAT%" (
    call :fail "tests\run-tests.bat not found"
    goto :summary
)
call :pass "validator and runner are present"

REM --- The committed runner must be clean ---

bash "%VALIDATOR%" --strict > "%SANDBOX%.clean.out" 2>&1
if errorlevel 1 (
    call :fail "committed run-tests.bat is unguarded per the validator"
    type "%SANDBOX%.clean.out"
    goto :cleanup
)
call :pass "committed run-tests.bat has every .bat child call guarded"

findstr /C:"every .bat child call guarded" "%SANDBOX%.clean.out" >nul
if errorlevel 1 (
    call :fail "validator summary does not mention the guard check"
    goto :cleanup
)
call :pass "validator reports the guard check in its summary line"

REM --- Runtime proof: a failing child must abort the wrapper ---
REM --meta-test-child-fail-only routes through the same guard block the real child calls use,
REM with a stub child that always exits 1, so cmd.exe itself proves the abort.

echo [ INFO ] Running run-tests.bat with a deliberately failing child script...
cmd /c "%RUNNER_BAT%" --meta-test-child-fail-only
if errorlevel 1 (
    call :pass "run-tests.bat exits non-zero when a child script fails"
) else (
    call :fail "run-tests.bat exited 0 despite a failing child script!"
)

call :cleanup
goto :summary

:cleanup
if exist "%SANDBOX%.clean.out" del /q "%SANDBOX%.clean.out"
if exist "%SANDBOX%.childfail.out" del /q "%SANDBOX%.childfail.out"
if exist "%SANDBOX%.childfail.bat" del /q "%SANDBOX%.childfail.bat"
goto :eof

:summary
echo.
if "%FAILED%"=="0" (
    echo [ SUCCESS ] All E2E validator guard-parity tests passed! (%PASSED%/%PASSED%)
    exit /b 0
)
echo [ ERROR ] E2E validator guard-parity tests: %FAILED% FAILED
exit /b 1

:pass
set /a PASSED+=1
echo [ INFO ] PASS: %~1
goto :eof

:fail
set /a FAILED+=1
echo [ ERROR ] FAIL: %~1
goto :eof

:print_info
echo [ INFO ] %~1
goto :eof
