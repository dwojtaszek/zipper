@echo off
REM E2E test: CLI argument-interaction conflict rejection (Windows)

call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

set PASSED=0
set FAILED=0

echo [ INFO ] === CLI Argument Interaction Tests ===

call :assert_rejected "--loadfile-only + --include-load-file" --loadfile-only --count 5 --output-path ".\results\zi_test" --include-load-file
call :assert_rejected "--loadfile-only + --target-zip-size" --loadfile-only --count 5 --output-path ".\results\zi_test" --target-zip-size 10MB
call :assert_rejected "--production-set + --loadfile-only" --production-set --loadfile-only --count 5 --output-path ".\results\zi_test" --bates-prefix TEST
call :assert_rejected "--chaos-scenario + --chaos-types" --loadfile-only --count 5 --output-path ".\results\zi_test" --chaos-mode --chaos-scenario full-chaos --chaos-types quotes
call :assert_rejected "--chaos-mode without --loadfile-only" --type pdf --count 5 --output-path ".\results\zi_test" --chaos-mode
call :assert_rejected "--chaos-amount without --chaos-mode" --loadfile-only --count 5 --output-path ".\results\zi_test" --chaos-amount "5%%"
call :assert_rejected "--chaos-types without --chaos-mode" --loadfile-only --count 5 --output-path ".\results\zi_test" --chaos-types quotes
call :assert_rejected "--chaos-scenario without --chaos-mode" --loadfile-only --count 5 --output-path ".\results\zi_test" --chaos-scenario full-chaos
call :assert_rejected "--col-delim without --loadfile-only" --type pdf --count 5 --output-path ".\results\zi_test" --col-delim "char:|"
call :assert_rejected "--production-set without --bates-prefix" --production-set --count 5 --output-path ".\results\zi_test"
call :assert_rejected "--production-zip without --production-set" --type pdf --count 5 --output-path ".\results\zi_test" --production-zip
call :assert_rejected "--volume-size without --production-set" --type pdf --count 5 --output-path ".\results\zi_test" --volume-size 100
call :assert_rejected "--target-zip-size without --count" --type pdf --output-path ".\results\zi_test" --target-zip-size 10MB

call :assert_accepted "valid standard --loadfile-only" --loadfile-only --count 5 --output-path ".\results\zi_test"
call :assert_accepted "valid standard --loadfile-only with --col-delim" --loadfile-only --count 5 --output-path ".\results\zi_test" --col-delim "char:|"
call :assert_accepted "valid standard --production-set" --production-set --count 5 --bates-prefix TEST --type pdf --output-path ".\results\zi_test"
call :assert_accepted "--loadfile-only + --chaos-mode + --chaos-amount" --loadfile-only --count 5 --chaos-mode --chaos-amount "5%%" --output-path ".\results\zi_test"
call :assert_accepted "--production-set + --bates-prefix + --volume-size" --production-set --count 5 --bates-prefix TEST --volume-size 100 --output-path ".\results\zi_test"
call :assert_accepted "--loadfile-only + --col-delim + --quote-delim" --loadfile-only --count 5 --col-delim "char:|" --quote-delim "char:\"" --output-path ".\results\zi_test"

call :assert_rejected "invalid --hash-mode" --type pdf --count 5 --output-path ".\results\zi_test" --hash-mode invalid
call :assert_rejected "--hash-algorithms without --hash-mode" --type pdf --count 5 --output-path ".\results\zi_test" --hash-algorithms md5
call :assert_rejected "invalid --hash-algorithms" --type pdf --count 5 --output-path ".\results\zi_test" --hash-mode actual --hash-algorithms md5,sha512
call :assert_rejected "--hash-mode actual + --loadfile-only" --loadfile-only --count 5 --output-path ".\results\zi_test" --hash-mode actual
call :assert_accepted "valid --hash-mode actual" --type pdf --count 5 --output-path ".\results\zi_test" --hash-mode actual --hash-algorithms md5,sha256
call :assert_accepted "valid --hash-mode simulated" --loadfile-only --count 5 --output-path ".\results\zi_test" --hash-mode simulated
call :assert_rejected "--loadfile-only + --load-file-format csv" --loadfile-only --count 5 --output-path ".\results\zi_test" --load-file-format csv
call :assert_rejected "--loadfile-only + --load-file-format xml" --loadfile-only --count 5 --output-path ".\results\zi_test" --load-file-format xml
call :assert_rejected "--loadfile-only + --load-file-format concordance" --loadfile-only --count 5 --output-path ".\results\zi_test" --load-file-format concordance

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
set "ARGS=!ARGS:%1 =!"
set "TEMP_DIR=.\results\test-interactions-%RANDOM%"
mkdir "%TEMP_DIR%" 2>nul
set "STDERR_FILE=%TEMP_DIR%\stderr.txt"
%ZIPPER_CMD% !ARGS! >nul 2>"!STDERR_FILE!"
if errorlevel 1 (
    findstr /i /c:"Unhandled exception" /c:"NullReferenceException" /c:"Exception:" "!STDERR_FILE!" >nul
    if not errorlevel 1 (
        echo [ ERROR ] FAIL: %DESC% ^(expected validation error, got crash^)
        type "!STDERR_FILE!"
        set /a FAILED+=1
    ) else (
        echo [ INFO ] PASS: %DESC%
        set /a PASSED+=1
    )
) else (
    echo [ ERROR ] FAIL: %DESC% ^(expected rejection^)
    set /a FAILED+=1
)
del /f /q "!STDERR_FILE!" 2>nul
goto :eof

:assert_accepted
set "DESC=%~1"
set "ARGS=%*"
set "ARGS=!ARGS:%1 =!"
%ZIPPER_CMD% !ARGS! >nul 2>&1
if not errorlevel 1 (
    echo [ INFO ] PASS: %DESC%
    set /a PASSED+=1
) else (
    echo [ ERROR ] FAIL: %DESC% ^(expected success^)
    set /a FAILED+=1
)
goto :eof
