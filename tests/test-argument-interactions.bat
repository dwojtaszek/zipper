@echo off
REM E2E test: CLI argument-interaction conflict rejection (Windows)

call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

set PASSED=0
set FAILED=0

echo [ INFO ] === CLI Argument Interaction Tests ===

call :assert_rejected "--loadfile-only + --include-load-file" --loadfile-only --count 5 --output-path "%TEMP%\zi_test" --include-load-file
call :assert_rejected "--loadfile-only + --target-zip-size" --loadfile-only --count 5 --output-path "%TEMP%\zi_test" --target-zip-size 10MB
call :assert_rejected "--production-set + --loadfile-only" --production-set --loadfile-only --count 5 --output-path "%TEMP%\zi_test" --bates-prefix TEST
call :assert_rejected "--chaos-scenario + --chaos-types" --loadfile-only --count 5 --output-path "%TEMP%\zi_test" --chaos-mode --chaos-scenario full-chaos --chaos-types quotes
call :assert_rejected "--chaos-mode without --loadfile-only" --type pdf --count 5 --output-path "%TEMP%\zi_test" --chaos-mode
call :assert_rejected "--chaos-amount without --chaos-mode" --loadfile-only --count 5 --output-path "%TEMP%\zi_test" --chaos-amount "5%%"
call :assert_rejected "--chaos-types without --chaos-mode" --loadfile-only --count 5 --output-path "%TEMP%\zi_test" --chaos-types quotes
call :assert_rejected "--chaos-scenario without --chaos-mode" --loadfile-only --count 5 --output-path "%TEMP%\zi_test" --chaos-scenario full-chaos
call :assert_rejected "--col-delim without --loadfile-only" --type pdf --count 5 --output-path "%TEMP%\zi_test" --col-delim "char:|"
call :assert_rejected "--production-set without --bates-prefix" --production-set --count 5 --output-path "%TEMP%\zi_test"
call :assert_rejected "--production-zip without --production-set" --type pdf --count 5 --output-path "%TEMP%\zi_test" --production-zip
call :assert_rejected "--volume-size without --production-set" --type pdf --count 5 --output-path "%TEMP%\zi_test" --volume-size 100
call :assert_rejected "--target-zip-size without --count" --type pdf --output-path "%TEMP%\zi_test" --target-zip-size 10MB

echo.
set /a TOTAL=!PASSED!+!FAILED!
if !FAILED! equ 0 (
    echo [ SUCCESS ] All argument interaction tests passed! ^(!PASSED!/!TOTAL!^)
) else (
    echo [ ERROR ] Argument interaction tests: !FAILED!/!TOTAL! FAILED
    exit /b 1
)
exit /b 0

:assert_rejected
set "DESC=%~1"
set "ARGS=%*"
set "ARGS=!ARGS:%~1 =!"
%ZIPPER_CMD% !ARGS! >nul 2>&1
if errorlevel 1 (
    echo [ INFO ] PASS: %DESC%
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: %DESC% ^(expected rejection^)
    set /a FAILED+=1
)
goto :eof
