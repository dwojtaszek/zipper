@echo off
REM Installs the version-controlled git hooks from .github\hooks\ into the
REM correct hooks directory for the current checkout (supports worktrees).
REM
REM Hooks:
REM   pre-commit   - dotnet format + unit tests (staged snapshot, skips docs-only)
REM   post-commit  - records success marker for pre-push skip-if-recent
REM   pre-push     - unit tests + basic E2E smoke (skips unit tests if pre-commit just ran)
REM
REM Usage: setup-hook.bat

setlocal

set "TEMPLATE_DIR=.github\hooks"

REM Works for both normal repos (returns .git) and worktrees (returns the
REM shared common dir under the main repo).
set "GIT_COMMON_DIR="
for /f "delims=" %%G in ('git rev-parse --git-common-dir 2^>nul') do set "GIT_COMMON_DIR=%%G"
if "%GIT_COMMON_DIR%"=="" (
    echo Error: not inside a git repository.
    exit /b 1
)
set "HOOK_DIR=%GIT_COMMON_DIR%\hooks"

if not exist "%HOOK_DIR%" mkdir "%HOOK_DIR%"

call :install_hook pre-commit
call :install_hook post-commit
call :install_hook pre-push

echo.
echo Done. Hooks installed from %TEMPLATE_DIR%\ into %HOOK_DIR%\
echo   pre-commit  -^> format + unit tests (stashed staged snapshot)
echo   post-commit -^> writes success marker for pre-push skip optimization
echo   pre-push    -^> unit tests (skippable) + basic E2E (5 cases)
echo.
echo Bypass with: git commit --no-verify / git push --no-verify
endlocal
exit /b 0

:install_hook
set "name=%~1"
set "src=%TEMPLATE_DIR%\%name%"
set "dst=%HOOK_DIR%\%name%"
if not exist "%src%" (
    echo Warning: template %src% not found -- skipping %name%
    goto :eof
)
copy /y "%src%" "%dst%" >nul
if errorlevel 1 (
    echo Error: failed to copy %src% -^> %dst%
    echo   (check permissions, disk space, and that the destination is writable)
    exit /b 1
)
echo Installed: %dst%
goto :eof
