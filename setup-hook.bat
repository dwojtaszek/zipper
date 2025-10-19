@echo off

REM This script sets up a Git pre-commit hook to run the test suite.

set HOOK_DIR=.git\hooks
set HOOK_FILE=%HOOK_DIR%\pre-commit

REM Create the hooks directory if it doesn't exist.
if not exist "%HOOK_DIR%" (
    mkdir "%HOOK_DIR%"
)

REM Create the pre-commit hook.
(
    echo @echo off
    echo.
    echo REM Run optimized test suite ^(unit tests + one basic E2E test^) for faster pre-commit checks.
    echo call tests\run-tests-optimized.bat
    echo.
    echo REM If the tests fail, exit with a non-zero status to prevent the commit.
    echo if errorlevel 1 (
    echo     echo Tests failed. Aborting commit.
    echo     exit /b 1
    echo )
    echo.
    echo exit /b 0
) > "%HOOK_FILE%"

echo Pre-commit hook created successfully.
