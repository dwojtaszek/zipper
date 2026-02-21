@echo off

REM Sets up Git hooks for the zipper project:
REM - pre-commit: dotnet format + unit tests
REM - pre-push:   unit tests + basic E2E smoke suite
REM
REM Usage: setup-hook.bat

set HOOK_DIR=.git\hooks

REM Create the hooks directory if it doesn't exist.
if not exist "%HOOK_DIR%" (
    mkdir "%HOOK_DIR%"
)

REM ────────────────────────────────────────────────
REM 1. Pre-commit hook
REM ────────────────────────────────────────────────
set HOOK_FILE=%HOOK_DIR%\pre-commit

(
    echo @echo off
    echo.
    echo REM Pre-commit hook: dotnet format + unit tests
    echo.
    echo REM ──────────────────────────────────────────────────────────
    echo REM Run dotnet format ^(auto-fix formatting^)
    echo REM ──────────────────────────────────────────────────────────
    echo where dotnet ^>nul 2^>nul
    echo if %%errorlevel%% equ 0 ^(
    echo     dotnet format --verbosity quiet 2^>nul
    echo     if %%errorlevel%% neq 0 ^(
    echo         echo Error: dotnet format failed 1^>^&2
    echo         exit /b 1
    echo     ^)
    echo.
    echo     REM Check if formatting made changes
    echo     git diff --exit-code --quiet 2^>nul
    echo     if %%errorlevel%% neq 0 ^(
    echo         echo Code formatting changes required. Files have been auto-formatted. 1^>^&2
    echo         echo Please review and commit again. 1^>^&2
    echo         git --no-pager diff --stat
    echo         exit /b 1
    echo     ^)
    echo ^)
    echo.
    echo REM ──────────────────────────────────────────────────────────
    echo REM Run unit tests
    echo REM ──────────────────────────────────────────────────────────
    echo where dotnet ^>nul 2^>nul
    echo if %%errorlevel%% equ 0 ^(
    echo     dotnet test src\Zipper.Tests\Zipper.Tests.csproj --logger "console;verbosity=quiet" 2^>nul
    echo     if errorlevel 1 ^(
    echo         echo Unit tests failed. Run 'dotnet test' for details. 1^>^&2
    echo         exit /b 1
    echo     ^)
    echo ^)
    echo.
    echo exit /b 0
) > "%HOOK_FILE%"

echo [OK] Pre-commit hook installed (format + unit tests)

REM ────────────────────────────────────────────────
REM 2. Pre-push hook
REM ────────────────────────────────────────────────
set PUSH_HOOK_FILE=%HOOK_DIR%\pre-push
set PUSH_HOOK_TEMPLATE=.github\hooks\pre-push

if exist "%PUSH_HOOK_TEMPLATE%" (
    copy /y "%PUSH_HOOK_TEMPLATE%" "%PUSH_HOOK_FILE%" >nul
    echo [OK] Pre-push hook installed (unit tests + basic E2E smoke suite)
) else (
    echo [WARN] Pre-push hook template not found at %PUSH_HOOK_TEMPLATE%
)

echo.
echo Done! Hooks installed in %HOOK_DIR%\
echo   pre-commit: format + unit tests
echo   pre-push:   unit tests + basic E2E (5 cases)
echo.
echo Bypass with: git commit --no-verify / git push --no-verify
