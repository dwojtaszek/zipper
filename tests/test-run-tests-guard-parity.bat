@echo off
REM E2E test: tests/validate-e2e-parity.sh must catch an unguarded .bat child call.
REM
REM Guards #1040. run-tests.bat used to call child scripts without checking errorlevel, so a
REM child that returned 1 was swallowed and the wrapper still reported success.
REM
REM The static validator is the protection that matters: it reads run-tests.bat itself, so it
REM catches a guard removed from any real call site. This test pins the validator against the
REM regressions that would silently disable that protection. It is the Windows counterpart of
REM test-run-tests-guard-parity.sh and makes the same assertions.

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

if not exist "%SANDBOX%" mkdir "%SANDBOX%"

REM --- The committed runner must be clean: every child call guarded ---

bash "%VALIDATOR%" --strict > "%SANDBOX%.clean.out" 2>&1
if errorlevel 1 (
    call :fail "committed run-tests.bat is unguarded per the validator"
    type "%SANDBOX%.clean.out"
    goto :summary
)
call :pass "committed run-tests.bat has every .bat child call guarded"

findstr /C:"every .bat child call guarded" "%SANDBOX%.clean.out" >nul
if errorlevel 1 (
    call :fail "validator summary does not mention the guard check"
    goto :summary
)
call :pass "validator reports the guard check in its summary line"

REM --- Negative 1: a child call that loses its guard must be caught ---

copy /y "%VALIDATOR%" "%SANDBOX%\validate-e2e-parity.sh" >nul
copy /y "%RUNNER_BAT%" "%SANDBOX%\run-tests.bat" >nul
copy /y "%REPO_ROOT%\tests\run-tests.sh" "%SANDBOX%\run-tests.sh" >nul

powershell -NoProfile -Command "$p='%SANDBOX%\run-tests.bat'; $s=[IO.File]::ReadAllText($p); $old=\"call .\tests\test-multipage-tiff.bat`r`nif errorlevel 1 (`r`n    echo [ ERROR ] Multipage TIFF tests failed.`r`n    exit /b 1`r`n)`r`n\"; $new=\"call .\tests\test-multipage-tiff.bat`r`n\"; if (-not $s.Contains($old)) { Write-Error 'guard block not found'; exit 1 }; [IO.File]::WriteAllText($p, $s.Replace($old, $new))"
if errorlevel 1 (
    call :fail "could not build the unguarded-runner sandbox"
    goto :summary
)

bash "%SANDBOX%\validate-e2e-parity.sh" --strict > "%SANDBOX%.unguarded.out" 2>&1
if errorlevel 1 (
    call :pass "validator fails when a child call loses its guard"
) else (
    call :fail "validator passed a run-tests.bat with an unguarded child call"
)

findstr /C:"test-multipage-tiff.bat" "%SANDBOX%.unguarded.out" >nul
if errorlevel 1 (
    call :fail "validator does not name the offending child script"
    type "%SANDBOX%.unguarded.out"
) else (
    call :pass "validator names the offending child script"
)

REM --- Negative 2: a guard that reports but does not abort must be caught ---

copy /y "%RUNNER_BAT%" "%SANDBOX%\run-tests.bat" >nul

powershell -NoProfile -Command "$p='%SANDBOX%\run-tests.bat'; $s=[IO.File]::ReadAllText($p); $old=\"if errorlevel 1 (`r`n    echo [ ERROR ] Office formats tests failed.`r`n    exit /b 1`r`n)\"; $new=\"if errorlevel 1 (`r`n    echo [ ERROR ] Office formats tests failed.`r`n)\"; if (-not $s.Contains($old)) { Write-Error 'office-formats guard block not found'; exit 1 }; [IO.File]::WriteAllText($p, $s.Replace($old, $new))"
if errorlevel 1 (
    call :fail "could not build the non-aborting-guard sandbox"
    goto :summary
)

bash "%SANDBOX%\validate-e2e-parity.sh" --strict > "%SANDBOX%.noexit.out" 2>&1
if errorlevel 1 (
    call :pass "validator fails when a guard does not 'exit /b 1'"
) else (
    call :fail "validator passed a guard that prints but never exits non-zero"
)

findstr /C:"does not 'exit /b 1'" "%SANDBOX%.noexit.out" >nul
if errorlevel 1 (
    call :fail "validator does not report the non-aborting guard case"
    type "%SANDBOX%.noexit.out"
) else (
    call :pass "validator distinguishes a non-aborting guard from a missing one"
)

REM --- Negative 3: "if errorlevel 10" is not a guard for errorlevel 1 ---

copy /y "%RUNNER_BAT%" "%SANDBOX%\run-tests.bat" >nul

powershell -NoProfile -Command "$p='%SANDBOX%\run-tests.bat'; $s=[IO.File]::ReadAllText($p); $old=\"if errorlevel 1 (`r`n    echo [ ERROR ] Multipage TIFF tests failed.`r`n\"; $new=\"if errorlevel 10 (`r`n    echo [ ERROR ] Multipage TIFF tests failed.`r`n\"; if (-not $s.Contains($old)) { Write-Error 'TIFF guard block not found'; exit 1 }; [IO.File]::WriteAllText($p, $s.Replace($old, $new))"
if errorlevel 1 (
    call :fail "could not build the errorlevel-10 sandbox"
    goto :summary
)

bash "%SANDBOX%\validate-e2e-parity.sh" --strict > "%SANDBOX%.errlevel.out" 2>&1
if errorlevel 1 (
    call :pass "validator rejects 'if errorlevel 10' as a guard"
) else (
    call :fail "validator accepted 'if errorlevel 10' as a guard for errorlevel 1"
)

REM --- Negative 4: a guard that only echoes the exit text is not a guard ---

copy /y "%RUNNER_BAT%" "%SANDBOX%\run-tests.bat" >nul

powershell -NoProfile -Command "$p='%SANDBOX%\run-tests.bat'; $s=[IO.File]::ReadAllText($p); $old=\"call .\tests\test-cross-platform.bat`r`nif errorlevel 1 (`r`n    echo [ ERROR ] Cross-platform tests failed.`r`n    exit /b 1`r`n)`r`n\"; $new=\"call .\tests\test-cross-platform.bat`r`nif errorlevel 1 echo please exit /b 1 later`r`n\"; if (-not $s.Contains($old)) { Write-Error 'cross-platform guard block not found'; exit 1 }; [IO.File]::WriteAllText($p, $s.Replace($old, $new))"
if errorlevel 1 (
    call :fail "could not build the echo-only-guard sandbox"
    goto :summary
)

bash "%SANDBOX%\validate-e2e-parity.sh" --strict > "%SANDBOX%.echofake.out" 2>&1
if errorlevel 1 (
    call :pass "validator rejects a guard that only echoes the exit text"
) else (
    call :fail "validator accepted a guard that only echoes 'exit /b 1' as text"
)

REM --- Positive: the single-line guard form is accepted ---

copy /y "%RUNNER_BAT%" "%SANDBOX%\run-tests.bat" >nul

powershell -NoProfile -Command "$p='%SANDBOX%\run-tests.bat'; $s=[IO.File]::ReadAllText($p); $old=\"call .\tests\test-cross-platform.bat`r`nif errorlevel 1 (`r`n    echo [ ERROR ] Cross-platform tests failed.`r`n    exit /b 1`r`n)`r`n\"; $new=\"call .\tests\test-cross-platform.bat`r`nif errorlevel 1 exit /b 1`r`n\"; if (-not $s.Contains($old)) { Write-Error 'cross-platform guard block not found'; exit 1 }; [IO.File]::WriteAllText($p, $s.Replace($old, $new))"
if errorlevel 1 (
    call :fail "could not build the single-line-guard sandbox"
    goto :summary
)

bash "%SANDBOX%\validate-e2e-parity.sh" --strict > "%SANDBOX%.oneline.out" 2>&1
if errorlevel 1 (
    call :fail "validator rejected a valid single-line guard"
    type "%SANDBOX%.oneline.out"
) else (
    call :pass "validator accepts the single-line 'if errorlevel 1 exit /b 1' form"
)

REM --- Negative: run-tests.bat missing goto :main must be caught (#1405) ---

copy /y "%RUNNER_BAT%" "%SANDBOX%\run-tests.bat" >nul
powershell -NoProfile -Command "$p='%SANDBOX%\run-tests.bat'; $s=[IO.File]::ReadAllText($p); $old=\"goto :main`r`n\"; if (-not $s.Contains($old)) { $old=\"goto :main`n\" }; if (-not $s.Contains($old)) { Write-Error 'goto :main not found'; exit 1 }; [IO.File]::WriteAllText($p, $s.Replace($old, ''))"
if errorlevel 1 (
    call :fail "could not remove goto :main in sandbox"
    goto :summary
)

bash "%SANDBOX%\validate-e2e-parity.sh" --strict > "%SANDBOX%.nogotomain.out" 2>&1
if errorlevel 1 (
    call :pass "validator fails when run-tests.bat is missing 'goto :main'"
) else (
    call :fail "validator passed a run-tests.bat with no 'goto :main'"
    type "%SANDBOX%.nogotomain.out"
)
findstr /C:"without 'goto :main' before it" "%SANDBOX%.nogotomain.out" >nul
if errorlevel 1 (
    call :fail "validator does not report missing 'goto :main'"
    type "%SANDBOX%.nogotomain.out"
) else (
    call :pass "validator reports missing 'goto :main' entry point"
)

REM --- Negative: run-tests.bat missing :main label must be caught (#1405) ---

copy /y "%RUNNER_BAT%" "%SANDBOX%\run-tests.bat" >nul
powershell -NoProfile -Command "$p='%SANDBOX%\run-tests.bat'; $s=[IO.File]::ReadAllText($p); $old=\"`r`n:main`r`n\"; if (-not $s.Contains($old)) { $old=\"`n:main`n\"; $rep=\"`n\" } else { $rep=\"`r`n\" }; if (-not $s.Contains($old)) { Write-Error ':main not found'; exit 1 }; [IO.File]::WriteAllText($p, $s.Replace($old, $rep))"
if errorlevel 1 (
    call :fail "could not remove :main in sandbox"
    goto :summary
)

bash "%SANDBOX%\validate-e2e-parity.sh" --strict > "%SANDBOX%.nomainlabel.out" 2>&1
if errorlevel 1 (
    call :pass "validator fails when run-tests.bat is missing ':main' label"
) else (
    call :fail "validator passed a run-tests.bat with no ':main' label"
    type "%SANDBOX%.nomainlabel.out"
)
findstr /C:"no ':main' label found" "%SANDBOX%.nomainlabel.out" >nul
if errorlevel 1 (
    call :fail "validator does not report missing ':main' label"
    type "%SANDBOX%.nomainlabel.out"
) else (
    call :pass "validator reports missing ':main' label"
)

REM --- Negative: run-tests.bat misplaced :main label must be caught (#1405) ---

copy /y "%RUNNER_BAT%" "%SANDBOX%\run-tests.bat" >nul
powershell -NoProfile -Command "$p='%SANDBOX%\run-tests.bat'; $s=[IO.File]::ReadAllText($p); $old=\"`r`n:main`r`n\"; if (-not $s.Contains($old)) { $old=\"`n:main`n\"; $nl=\"`n\" } else { $nl=\"`r`n\" }; if (-not $s.Contains($old)) { Write-Error ':main not found'; exit 1 }; $s=$s.Replace($old, $nl); $m='Test Case 1 passed.'; if (-not $s.Contains($m)) { Write-Error 'Test Case 1 marker not found'; exit 1 }; $s=$s.Replace($m, $m + $nl + ':main' + $nl); [IO.File]::WriteAllText($p, $s)"
if errorlevel 1 (
    call :fail "could not misplace :main in sandbox"
    goto :summary
)

bash "%SANDBOX%\validate-e2e-parity.sh" --strict > "%SANDBOX%.misplacedmain.out" 2>&1
if errorlevel 1 (
    call :pass "validator fails when run-tests.bat has ':main' after test cases"
) else (
    call :fail "validator passed a run-tests.bat with ':main' after test cases"
    type "%SANDBOX%.misplacedmain.out"
)
findstr /C:"Entry point misplaced in run-tests.bat" "%SANDBOX%.misplacedmain.out" >nul
if errorlevel 1 (
    call :fail "validator does not report misplaced ':main' label"
    type "%SANDBOX%.misplacedmain.out"
) else (
    call :pass "validator reports misplaced ':main' label"
)

REM --- Negative: run-tests.bat terminating subroutine between :main and :end_of_tests must be caught (#1405) ---

copy /y "%RUNNER_BAT%" "%SANDBOX%\run-tests.bat" >nul
powershell -NoProfile -Command "$p='%SANDBOX%\run-tests.bat'; $s=[IO.File]::ReadAllText($p); $old=\"`r`n:main`r`n\"; if (-not $s.Contains($old)) { $old=\"`n:main`n\"; $nl=\"`n\" } else { $nl=\"`r`n\" }; if (-not $s.Contains($old)) { Write-Error ':main not found'; exit 1 }; $rogue=$nl + ':main' + $nl + ':rogue_helper' + $nl + 'goto :eof' + $nl; $s=$s.Replace($old, $rogue); [IO.File]::WriteAllText($p, $s)"
if errorlevel 1 (
    call :fail "could not insert terminating subroutine between :main and :end_of_tests in sandbox"
    goto :summary
)

bash "%SANDBOX%\validate-e2e-parity.sh" --strict > "%SANDBOX%.terminatingsub.out" 2>&1
if errorlevel 1 (
    call :pass "validator fails when run-tests.bat has terminating subroutine between :main and :end_of_tests"
) else (
    call :fail "validator passed a run-tests.bat with terminating subroutine between :main and :end_of_tests"
    type "%SANDBOX%.terminatingsub.out"
)
findstr /C:"Terminating helper subroutine found between :main and :end_of_tests" "%SANDBOX%.terminatingsub.out" >nul
if errorlevel 1 (
    call :fail "validator does not report terminating subroutine between :main and :end_of_tests"
    type "%SANDBOX%.terminatingsub.out"
) else (
    call :pass "validator reports terminating subroutine between :main and :end_of_tests"
)

call :cleanup

:summary
set /a TOTAL=PASSED+FAILED
echo.
if "%FAILED%"=="0" (
    echo [ SUCCESS ] All E2E validator guard-parity tests passed! (%PASSED%/%TOTAL%)
    exit /b 0
)
echo [ ERROR ] E2E validator guard-parity tests: %FAILED%/%TOTAL% FAILED
exit /b 1

:cleanup
if exist "%SANDBOX%" rd /s /q "%SANDBOX%"
for %%F in (clean unguarded noexit errlevel echofake oneline nogotomain nomainlabel misplacedmain terminatingsub) do (
    if exist "%SANDBOX%.%%F.out" del /q "%SANDBOX%.%%F.out"
)
goto :eof

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
