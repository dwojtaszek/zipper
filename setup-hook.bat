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
    echo REM ──────────────────────────────────────────────────────────
    echo REM Run dotnet format ^(auto-fix formatting^)
    echo REM ──────────────────────────────────────────────────────────
    echo where dotnet ^>nul 2^>nul
    echo if %%errorlevel%% equ 0 ^(
    echo     dotnet format --verbosity quiet 2^>nul
    echo     if %%errorlevel%% neq 0 ^(
    echo         echo Error: dotnet format failed 1^>&2
    echo         exit /b 1
    echo     ^)
    echo.
    echo     REM Check if formatting made changes
    echo     git diff --exit-code --quiet 2^>nul
    echo     if %%errorlevel%% neq 0 ^(
    echo         echo Code formatting changes required. Files have been auto-formatted. 1^>&2
    echo         echo Please review and commit again. 1^>&2
    echo         git --no-pager diff --stat
    echo         exit /b 1
    echo     ^)
    echo ^)
    echo.
    echo REM ──────────────────────────────────────────────────────────
    echo REM Run optimized test suite
    echo REM ──────────────────────────────────────────────────────────
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
